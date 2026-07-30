using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class WhisperingEarringStatsTests
{
    private const string WhisperingEarringRelicId = "RELIC.WHISPERING_EARRING";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_WhisperingEarringFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[WhisperingEarringRelicId] = new RelicAggregate
        {
            WhisperingEarringFirstRoundHpLost = 21m,
            WhisperingEarringCombats = 3,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);
        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[WhisperingEarringRelicId];
        Assert.Equal(21m, agg.WhisperingEarringFirstRoundHpLost);
        Assert.Equal(3, agg.WhisperingEarringCombats);
    }

    [Theory]
    [InlineData(1, CombatSide.Player, PlayerTurnPhase.None, false)]
    [InlineData(1, CombatSide.Player, PlayerTurnPhase.Start, true)]
    [InlineData(1, CombatSide.Player, PlayerTurnPhase.AutoPrePlay, true)]
    [InlineData(1, CombatSide.Player, PlayerTurnPhase.Play, true)]
    [InlineData(1, CombatSide.Player, PlayerTurnPhase.AutoPostPlay, true)]
    [InlineData(1, CombatSide.Player, PlayerTurnPhase.End, true)]
    [InlineData(1, CombatSide.Enemy, PlayerTurnPhase.None, true)]
    [InlineData(2, CombatSide.Player, PlayerTurnPhase.Start, false)]
    [InlineData(2, CombatSide.Enemy, PlayerTurnPhase.None, false)]
    public void FirstRoundWindow_UsesPlayerTurnStartThroughEnemyTurnEnd(
        int roundNumber,
        CombatSide side,
        PlayerTurnPhase phase,
        bool expected)
    {
        Assert.Equal(
            expected,
            RunTracker.ShouldTrackWhisperingEarringHpLossForTest(
                roundNumber,
                side,
                phase));
    }

    [Fact]
    public void RunTracker_WhisperingEarringHelpers_SumLossAndCountZeroLossCombats()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordWhisperingEarringCombatForTest(agg, 3);
        RunTracker.RecordWhisperingEarringHpLostForTest(agg, 8m);
        RunTracker.RecordWhisperingEarringHpLostForTest(agg, 13m);
        RunTracker.RecordWhisperingEarringHpLostForTest(agg, 0m);

        Assert.Equal(21m, agg.WhisperingEarringFirstRoundHpLost);
        Assert.Equal(3, agg.WhisperingEarringCombats);
    }

    [Fact]
    public void MergeRelicAggregateInto_WhisperingEarringFields_Accumulate()
    {
        var target = new RelicAggregate
        {
            WhisperingEarringFirstRoundHpLost = 8m,
            WhisperingEarringCombats = 1,
        };
        var source = new RelicAggregate
        {
            WhisperingEarringFirstRoundHpLost = 13m,
            WhisperingEarringCombats = 2,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(21m, target.WhisperingEarringFirstRoundHpLost);
        Assert.Equal(3, target.WhisperingEarringCombats);
    }

    [Fact]
    public void RelicTooltip_WhisperingEarring_ShowsTotalAndPerCombatAverage()
    {
        var relic = (WhisperingEarring)RuntimeHelpers.GetUninitializedObject(
            typeof(WhisperingEarring));
        var agg = new RelicAggregate
        {
            WhisperingEarringFirstRoundHpLost = 21m,
            WhisperingEarringCombats = 3,
        };

        var supported = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            agg,
            null,
            out var title,
            out var body);

        Assert.True(supported);
        Assert.Equal("Whispering Earring", title);
        Assert.Contains(
            "Total life lost, player's first turn through opponent's first turn",
            body);
        Assert.Contains(
            "Avg life lost, player's first turn through opponent's first turn per combat",
            body);
        Assert.Contains("[b]21[/b]", body);
        Assert.Contains("[b]7[/b]", body);
    }
}
