using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class TungstenRodStatsTests
{
    private const string TungstenRodRelicId = "RELIC.TUNGSTEN_ROD";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_TungstenRodFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[TungstenRodRelicId] = FixtureAggregate();

        var json = JsonSerializer.Serialize(run, SerializerOptions);
        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        AssertAggregate(restored!.RelicAggregates[TungstenRodRelicId]);
    }

    [Theory]
    [InlineData(5, 4, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(0, 0, 0)]
    [InlineData(3, 3, 0)]
    [InlineData(3, -2, 3)]
    public void CalculateDamagePrevented_UsesClampedPositiveDelta(
        int amountBefore,
        int amountAfter,
        int expected)
    {
        Assert.Equal(
            expected,
            RunTracker.CalculateTungstenRodDamagePreventedForTest(
                amountBefore,
                amountAfter));
    }

    [Theory]
    [InlineData(CardType.Curse, true, null, false, false, false, "Curse")]
    [InlineData(CardType.Status, true, null, false, false, false, "Status")]
    [InlineData(CardType.Skill, true, null, false, true, false, "SelfInflicted")]
    [InlineData(null, false, PowerType.Buff, true, false, false, "SelfInflicted")]
    [InlineData(null, false, PowerType.Debuff, true, true, false, "Enemy")]
    [InlineData(null, false, null, false, false, true, "Enemy")]
    [InlineData(null, false, null, false, true, false, "SelfInflicted")]
    [InlineData(null, false, null, false, false, false, "Other")]
    public void SourceClassification_UsesMutuallyExclusiveReliableSignals(
        CardType? cardType,
        bool cardOwnedByPlayer,
        PowerType? powerType,
        bool powerOwnedByPlayer,
        bool dealerIsPlayer,
        bool dealerIsEnemy,
        string expected)
    {
        Assert.Equal(
            Enum.Parse<TungstenRodPreventionSource>(expected),
            RunTracker.ClassifyTungstenRodPreventionSourceForTest(
                cardType,
                cardOwnedByPlayer,
                powerType,
                powerOwnedByPlayer,
                dealerIsPlayer,
                dealerIsEnemy));
    }

    [Fact]
    public void Recording_PreventionAlwaysAddsTotal_AndAtMostOneSourceBucket()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordTungstenRodDamagePreventedForTest(
            agg,
            4m,
            TungstenRodPreventionSource.SelfInflicted);
        RunTracker.RecordTungstenRodDamagePreventedForTest(
            agg,
            3m,
            TungstenRodPreventionSource.Curse);
        RunTracker.RecordTungstenRodDamagePreventedForTest(
            agg,
            2m,
            TungstenRodPreventionSource.Status);
        RunTracker.RecordTungstenRodDamagePreventedForTest(
            agg,
            8m,
            TungstenRodPreventionSource.Enemy);
        RunTracker.RecordTungstenRodDamagePreventedForTest(
            agg,
            1m,
            TungstenRodPreventionSource.Other);
        RunTracker.RecordTungstenRodTurnForTest(agg, 6);
        RunTracker.RecordTungstenRodCombatForTest(agg, 3);

        AssertAggregate(agg);
    }

    [Fact]
    public void MergeRelicAggregateInto_TungstenRodFields_Accumulate()
    {
        var target = new RelicAggregate
        {
            TungstenRodDamagePrevented = 7m,
            TungstenRodSelfDamagePrevented = 1m,
            TungstenRodCurseDamagePrevented = 1m,
            TungstenRodStatusDamagePrevented = 1m,
            TungstenRodEnemyDamagePrevented = 3m,
            TungstenRodTurns = 2,
            TungstenRodCombats = 1,
        };
        var source = new RelicAggregate
        {
            TungstenRodDamagePrevented = 11m,
            TungstenRodSelfDamagePrevented = 3m,
            TungstenRodCurseDamagePrevented = 2m,
            TungstenRodStatusDamagePrevented = 1m,
            TungstenRodEnemyDamagePrevented = 5m,
            TungstenRodTurns = 4,
            TungstenRodCombats = 2,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        AssertAggregate(target);
    }

    [Fact]
    public void RelicTooltip_TungstenRod_ShowsTotalsAndHeldPeriodAverages()
    {
        var relic = (TungstenRod)RuntimeHelpers.GetUninitializedObject(
            typeof(TungstenRod));

        var supported = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            FixtureAggregate(),
            null,
            out var title,
            out var body);

        Assert.True(supported);
        Assert.Equal("Tungsten Rod", title);
        Assert.Contains("Damage prevented", body);
        Assert.Contains("Avg lost life prevented per turn", body);
        Assert.Contains("Avg lost life prevented per combat", body);
        Assert.Contains("Self-inflicted lost life prevented", body);
        Assert.Contains("Curse-inflicted lost life prevented", body);
        Assert.Contains("Status-inflicted lost life prevented", body);
        Assert.Contains("Enemy-source lost life prevented", body);
    }

    private static RelicAggregate FixtureAggregate()
        => new()
        {
            TungstenRodDamagePrevented = 18m,
            TungstenRodSelfDamagePrevented = 4m,
            TungstenRodCurseDamagePrevented = 3m,
            TungstenRodStatusDamagePrevented = 2m,
            TungstenRodEnemyDamagePrevented = 8m,
            TungstenRodTurns = 6,
            TungstenRodCombats = 3,
        };

    private static void AssertAggregate(RelicAggregate agg)
    {
        Assert.Equal(18m, agg.TungstenRodDamagePrevented);
        Assert.Equal(4m, agg.TungstenRodSelfDamagePrevented);
        Assert.Equal(3m, agg.TungstenRodCurseDamagePrevented);
        Assert.Equal(2m, agg.TungstenRodStatusDamagePrevented);
        Assert.Equal(8m, agg.TungstenRodEnemyDamagePrevented);
        Assert.Equal(6, agg.TungstenRodTurns);
        Assert.Equal(3, agg.TungstenRodCombats);
    }
}
