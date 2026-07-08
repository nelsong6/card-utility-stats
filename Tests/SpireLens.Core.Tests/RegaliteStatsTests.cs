using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class RegaliteStatsTests
{
    private const string RegaliteRelicId = "RELIC.REGALITE";

    private static readonly MethodInfo BuildRegaliteBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildRegaliteBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildRegaliteBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_RegaliteFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.RegaliteCardsCreated);
        Assert.Equal(0, agg.AdditionalBlockGained);
        Assert.Equal(0, agg.RegaliteTurns);
        Assert.Equal(0, agg.RegaliteCombats);
    }

    [Fact]
    public void RelicAggregate_RegaliteFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[RegaliteRelicId] = new RelicAggregate
        {
            RegaliteCardsCreated = 6,
            AdditionalBlockGained = 12,
            RegaliteTurns = 4,
            RegaliteCombats = 2,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("regalite_cards_created", json);
        Assert.Contains("additional_block_gained", json);
        Assert.Contains("regalite_turns", json);
        Assert.Contains("regalite_combats", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[RegaliteRelicId];
        Assert.Equal(6, restoredAgg.RegaliteCardsCreated);
        Assert.Equal(12, restoredAgg.AdditionalBlockGained);
        Assert.Equal(4, restoredAgg.RegaliteTurns);
        Assert.Equal(2, restoredAgg.RegaliteCombats);
    }

    [Fact]
    public void RunTracker_RegaliteHelpers_AccumulateAndClamp()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordRegaliteCardCreatedForTest(agg, 6);
        RunTracker.RecordRegaliteCombatForTest(agg, 2);
        RunTracker.RecordRegaliteTurnForTest(agg, 4);
        RunTracker.RecordRegaliteCardCreatedForTest(agg, -1);
        RunTracker.RecordRegaliteCombatForTest(agg, -2);
        RunTracker.RecordRegaliteTurnForTest(agg, -4);

        Assert.Equal(6, agg.RegaliteCardsCreated);
        Assert.Equal(2, agg.RegaliteCombats);
        Assert.Equal(4, agg.RegaliteTurns);
    }

    [Fact]
    public void MergeRelicAggregateInto_RegaliteFields_Accumulates()
    {
        var target = new RelicAggregate
        {
            RegaliteCardsCreated = 4,
            AdditionalBlockGained = 8,
            RegaliteTurns = 3,
            RegaliteCombats = 1,
        };
        var source = new RelicAggregate
        {
            RegaliteCardsCreated = 2,
            AdditionalBlockGained = 4,
            RegaliteTurns = 1,
            RegaliteCombats = 1,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(6, target.RegaliteCardsCreated);
        Assert.Equal(12, target.AdditionalBlockGained);
        Assert.Equal(4, target.RegaliteTurns);
        Assert.Equal(2, target.RegaliteCombats);
    }

    [Fact]
    public void RelicTooltip_Regalite_ShowsTotalsAndAverages()
    {
        var body = BuildBody(new RelicAggregate
        {
            RegaliteCardsCreated = 6,
            AdditionalBlockGained = 12,
            RegaliteTurns = 4,
            RegaliteCombats = 2,
        });

        Assert.Contains("Cards created", body);
        Assert.Contains("[img=16x16]res://images/ui/combat/block.png[/img] block gained", body);
        Assert.Contains("[img=16x16]res://images/ui/combat/block.png[/img] avg block per turn", body);
        Assert.Contains("[img=16x16]res://images/ui/combat/block.png[/img] avg block per combat", body);
        Assert.Contains("[b]6[/b]", body);
        Assert.Contains("[b]12[/b]", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[b]6[/b]", body);
    }

    [Fact]
    public void RelicTooltip_Regalite_ShowsZeroRows()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Cards created", body);
        Assert.Contains("block gained", body);
        Assert.Contains("avg block per turn", body);
        Assert.Contains("avg block per combat", body);
        Assert.Contains("[b]0[/b]", body);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildRegaliteBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildRegaliteBodyBBCode returned null."));
}
