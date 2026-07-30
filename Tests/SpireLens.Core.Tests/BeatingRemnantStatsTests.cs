using System;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class BeatingRemnantStatsTests
{
    private const string BeatingRemnantRelicId = "RELIC.BEATING_REMNANT";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_BeatingRemnantFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0m, agg.BeatingRemnantHpLossPrevented);
        Assert.Equal(0, agg.BeatingRemnantTurns);
        Assert.Equal(0, agg.BeatingRemnantCombats);
    }

    [Fact]
    public void RelicAggregate_BeatingRemnantFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[BeatingRemnantRelicId] = new RelicAggregate
        {
            BeatingRemnantHpLossPrevented = 18m,
            BeatingRemnantTurns = 6,
            BeatingRemnantCombats = 3,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);
        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.Contains("beating_remnant_hp_loss_prevented", json);
        Assert.Contains("beating_remnant_turns", json);
        Assert.Contains("beating_remnant_combats", json);
        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[BeatingRemnantRelicId];
        Assert.Equal(18m, agg.BeatingRemnantHpLossPrevented);
        Assert.Equal(6, agg.BeatingRemnantTurns);
        Assert.Equal(3, agg.BeatingRemnantCombats);
    }

    [Theory]
    [InlineData(30, 20, 10)]
    [InlineData(7, 7, 0)]
    [InlineData(5, -2, 5)]
    [InlineData(-3, -3, 0)]
    public void CalculateBeatingRemnantHpLossPrevented_UsesClampedPositiveDelta(
        int amountBefore,
        int amountAfter,
        int expected)
    {
        Assert.Equal(
            expected,
            RunTracker.CalculateBeatingRemnantHpLossPreventedForTest(
                amountBefore,
                amountAfter));
    }

    [Fact]
    public void RunTracker_BeatingRemnantHelpers_RecordPreventedHpAndDenominators()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordBeatingRemnantHpLossPreventedForTest(agg, 30m, 20m);
        RunTracker.RecordBeatingRemnantHpLossPreventedForTest(agg, 8m, 5m);
        RunTracker.RecordBeatingRemnantTurnForTest(agg, 4);
        RunTracker.RecordBeatingRemnantTurnForTest(agg, -1);
        RunTracker.RecordBeatingRemnantCombatForTest(agg, 2);
        RunTracker.RecordBeatingRemnantCombatForTest(agg, -1);

        Assert.Equal(13m, agg.BeatingRemnantHpLossPrevented);
        Assert.Equal(4, agg.BeatingRemnantTurns);
        Assert.Equal(2, agg.BeatingRemnantCombats);
    }

    [Fact]
    public void MergeRelicAggregateInto_BeatingRemnantFields_Accumulate()
    {
        var target = new RelicAggregate
        {
            BeatingRemnantHpLossPrevented = 5m,
            BeatingRemnantTurns = 2,
            BeatingRemnantCombats = 1,
        };
        var source = new RelicAggregate
        {
            BeatingRemnantHpLossPrevented = 13m,
            BeatingRemnantTurns = 4,
            BeatingRemnantCombats = 2,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(18m, target.BeatingRemnantHpLossPrevented);
        Assert.Equal(6, target.BeatingRemnantTurns);
        Assert.Equal(3, target.BeatingRemnantCombats);
    }

    [Fact]
    public void RelicTooltip_BeatingRemnant_ShowsTotalAndZeroInclusiveAverages()
    {
        var relic = (BeatingRemnant)RuntimeHelpers.GetUninitializedObject(
            typeof(BeatingRemnant));
        var agg = new RelicAggregate
        {
            BeatingRemnantHpLossPrevented = 18m,
            BeatingRemnantTurns = 6,
            BeatingRemnantCombats = 3,
        };

        var supported = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            agg,
            null,
            out var title,
            out var body);

        Assert.True(supported);
        Assert.Equal("Beating Remnant", title);
        Assert.Contains("HP loss prevented", body);
        Assert.Contains("Avg HP loss prevented per turn", body);
        Assert.Contains("Avg HP loss prevented per combat", body);
        Assert.Contains("[b]18[/b]", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[b]6[/b]", body);
    }
}
