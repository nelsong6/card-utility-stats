using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class SturdyClampStatsTests
{
    private const string SturdyClampRelicId = "RELIC.STURDY_CLAMP";

    private static readonly MethodInfo BuildSturdyClampBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildSturdyClampBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildSturdyClampBodyBBCode not found.");

    [Fact]
    public void Patch_TargetsSturdyClampAfterPreventingBlockClearWithExpectedParameters()
    {
        var target = typeof(SturdyClamp).GetMethod(nameof(SturdyClamp.AfterPreventingBlockClear));

        Assert.NotNull(target);
        Assert.Equal(
            new[] { typeof(AbstractModel), typeof(Creature) },
            target!.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void RelicAggregate_SturdyClampFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.SturdyClampBlockRetained);
        Assert.Equal(0, agg.SturdyClampExcessBlockOverTen);
        Assert.Equal(0, agg.SturdyClampTurns);
        Assert.Equal(0, agg.SturdyClampCombats);
    }

    [Fact]
    public void RelicAggregate_SturdyClampFields_JsonRoundtripPreservesValues()
    {
        var run = new RunData();
        run.RelicAggregates[SturdyClampRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"sturdy_clamp_block_retained\"", json);
        Assert.Contains("\"sturdy_clamp_excess_block_over_ten\"", json);
        Assert.Contains("\"sturdy_clamp_turns\"", json);
        Assert.Contains("\"sturdy_clamp_combats\"", json);
        Assert.NotNull(restored);

        AssertPopulatedAggregate(restored!.RelicAggregates[SturdyClampRelicId]);
    }

    [Fact]
    public void RunTracker_SturdyClampHelpers_AccumulateObservedRetentionAndZeroTurns()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordSturdyClampTurnForTest(agg, 13, 10);
        RunTracker.RecordSturdyClampTurnForTest(agg, 7, 7);
        RunTracker.RecordSturdyClampTurnForTest(agg, 0, 0);
        RunTracker.RecordSturdyClampCombatForTest(agg, 2);

        AssertPopulatedAggregate(agg);
    }

    [Fact]
    public void RunTracker_SturdyClampHelpers_ClampNegativeValuesAndCounts()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordSturdyClampTurnForTest(agg, -5, -2);
        RunTracker.RecordSturdyClampCombatForTest(agg, -1);

        Assert.Equal(1, agg.SturdyClampTurns);
        Assert.Equal(0, agg.SturdyClampBlockRetained);
        Assert.Equal(0, agg.SturdyClampExcessBlockOverTen);
        Assert.Equal(0, agg.SturdyClampCombats);
    }

    [Fact]
    public void RelicAggregate_SturdyClampFields_Merge()
    {
        var target = PopulatedAggregate();

        RunTracker.MergeRelicAggregateInto(target, PopulatedAggregate());

        Assert.Equal(34, target.SturdyClampBlockRetained);
        Assert.Equal(6, target.SturdyClampExcessBlockOverTen);
        Assert.Equal(6, target.SturdyClampTurns);
        Assert.Equal(4, target.SturdyClampCombats);
    }

    [Fact]
    public void RelicTooltip_SturdyClamp_ShowsRequestedAverages()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("avg block retained per turn", body);
        Assert.Contains("avg block retained per combat", body);
        Assert.Contains("avg excess block over 10 per turn", body);
        Assert.Contains("avg excess block over 10 per combat", body);
        Assert.Contains("[b]5.67[/b]", body);
        Assert.Contains("[b]8.5[/b]", body);
        Assert.Contains("[b]1[/b]", body);
        Assert.Contains("[b]1.5[/b]", body);
        Assert.Contains("[color=#b5b5b5]/[/color]", body);
    }

    [Fact]
    public void RelicTooltip_SturdyClamp_ShowsZeroAveragesWithoutDenominators()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("avg block retained per turn", body);
        Assert.Contains("avg excess block over 10 per combat", body);
        Assert.Contains("[b]0[/b]", body);
    }

    [Fact]
    public void RelicTooltip_SturdyClamp_UsesWidePanelWithoutChangingDefaultPolicy()
    {
        var relic = (SturdyClamp)RuntimeHelpers.GetUninitializedObject(typeof(SturdyClamp));

        Assert.Equal(420f, RelicHoverShowPatch.GetPreferredStatsTooltipWidth(relic));
        Assert.Null(RelicHoverShowPatch.GetPreferredStatsTooltipWidth(null));
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void RelicTooltip_SturdyClamp_DispatchesForModel()
    {
        var relic = (SturdyClamp)RuntimeHelpers.GetUninitializedObject(typeof(SturdyClamp));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Sturdy Clamp", title);
        Assert.Contains("Average block retained by Sturdy Clamp per turn.", body);
        Assert.Contains("Average block above 10 retained by Sturdy Clamp per combat.", body);
        Assert.Contains("res://images/ui/combat/block.png", body);
        Assert.Contains("retained", body);
        Assert.Contains("excess over 10", body);
    }

    [Fact]
    public void RelicAggregate_OlderShapeWithoutSturdyClampFields_DefaultsToZero()
    {
        var agg = JsonSerializer.Deserialize<RelicAggregate>("{}", RunStorage.Options);

        Assert.NotNull(agg);
        Assert.Equal(0, agg!.SturdyClampBlockRetained);
        Assert.Equal(0, agg.SturdyClampExcessBlockOverTen);
        Assert.Equal(0, agg.SturdyClampTurns);
        Assert.Equal(0, agg.SturdyClampCombats);
    }

    private static RelicAggregate PopulatedAggregate()
    {
        var agg = new RelicAggregate();
        RunTracker.RecordSturdyClampTurnForTest(agg, 13, 10);
        RunTracker.RecordSturdyClampTurnForTest(agg, 7, 7);
        RunTracker.RecordSturdyClampTurnForTest(agg, 0, 0);
        RunTracker.RecordSturdyClampCombatForTest(agg, 2);
        return agg;
    }

    private static void AssertPopulatedAggregate(RelicAggregate agg)
    {
        Assert.Equal(17, agg.SturdyClampBlockRetained);
        Assert.Equal(3, agg.SturdyClampExcessBlockOverTen);
        Assert.Equal(3, agg.SturdyClampTurns);
        Assert.Equal(2, agg.SturdyClampCombats);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildSturdyClampBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildSturdyClampBodyBBCode returned null."));
}
