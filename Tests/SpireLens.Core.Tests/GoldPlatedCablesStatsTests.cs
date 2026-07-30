using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Orbs;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class GoldPlatedCablesStatsTests
{
    private const string RelicId = "RELIC.GOLD_PLATED_CABLES";

    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildGoldPlatedCablesBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "BuildGoldPlatedCablesBodyBBCode not found.");

    [Fact]
    public void Patches_TargetOrbPassiveModifierAndPlayerTurnEnd()
    {
        Assert.NotNull(typeof(Hook).GetMethod(
            nameof(Hook.AfterModifyingOrbPassiveTriggerCount),
            BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(typeof(OrbQueue).GetMethod(
            nameof(OrbQueue.BeforeTurnEnd),
            BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void RelicAggregate_GoldPlatedCablesFields_DefaultToEmpty()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0, agg.GoldPlatedCablesNoOrbTargets);
        Assert.Empty(agg.GoldPlatedCablesActivationsByOrbType);
    }

    [Fact]
    public void Recording_AccumulatesConfirmedOrbTypesAndEmptyTurnEnds()
    {
        var agg = PopulatedAggregate();

        Assert.Equal(5, agg.Activations);
        Assert.Equal(
            3,
            agg.GoldPlatedCablesActivationsByOrbType["ORB.LIGHTNING"]
                .Activations);
        Assert.Equal(
            "Lightning",
            agg.GoldPlatedCablesActivationsByOrbType["ORB.LIGHTNING"]
                .DisplayName);
        Assert.Equal(
            2,
            agg.GoldPlatedCablesActivationsByOrbType["ORB.FROST"]
                .Activations);
        Assert.Equal(2, agg.GoldPlatedCablesNoOrbTargets);
    }

    [Fact]
    public void RelicAggregate_GoldPlatedCablesFields_JsonRoundtrip()
    {
        var run = new RunData();
        run.RelicAggregates[RelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(
            json,
            RunStorage.Options);

        Assert.Contains(
            "\"gold_plated_cables_activations_by_orb_type\"",
            json);
        Assert.Contains("\"gold_plated_cables_no_orb_targets\"", json);
        Assert.NotNull(restored);
        Assert.Equal(
            3,
            restored!.RelicAggregates[RelicId]
                .GoldPlatedCablesActivationsByOrbType["ORB.LIGHTNING"]
                .Activations);
        Assert.Equal(
            2,
            restored.RelicAggregates[RelicId]
                .GoldPlatedCablesNoOrbTargets);
    }

    [Fact]
    public void RelicAggregate_GoldPlatedCablesFields_Merge()
    {
        var target = PopulatedAggregate();

        RunTracker.MergeRelicAggregateInto(target, PopulatedAggregate());

        Assert.Equal(10, target.Activations);
        Assert.Equal(
            6,
            target.GoldPlatedCablesActivationsByOrbType["ORB.LIGHTNING"]
                .Activations);
        Assert.Equal(
            4,
            target.GoldPlatedCablesActivationsByOrbType["ORB.FROST"]
                .Activations);
        Assert.Equal(4, target.GoldPlatedCablesNoOrbTargets);
    }

    [Fact]
    public void RelicTooltip_GoldPlatedCables_ShowsRequestedBreakdown()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("Activations with orb", body);
        Assert.Contains("Lightning activations", body);
        Assert.Contains("Frost activations", body);
        Assert.Contains("Dark activations", body);
        Assert.Contains("Plasma activations", body);
        Assert.Contains("Glass activations", body);
        Assert.Contains("Turns with no orb to target", body);
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void RelicTooltip_GoldPlatedCables_DispatchesForModel()
    {
        var relic = (GoldPlatedCables)RuntimeHelpers.GetUninitializedObject(
            typeof(GoldPlatedCables));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Gold-Plated Cables", title);
        Assert.Contains("Activations with orb", body);
    }

    [Fact]
    public void RelicAggregate_OlderShapeWithoutGoldPlatedCablesFields_Defaults()
    {
        var agg = JsonSerializer.Deserialize<RelicAggregate>(
            "{}",
            RunStorage.Options);

        Assert.NotNull(agg);
        Assert.Equal(0, agg!.GoldPlatedCablesNoOrbTargets);
        Assert.Empty(agg.GoldPlatedCablesActivationsByOrbType);
    }

    private static RelicAggregate PopulatedAggregate()
    {
        var agg = new RelicAggregate();
        RunTracker.RecordGoldPlatedCablesActivationForTest(
            agg,
            "ORB.LIGHTNING",
            "Lightning",
            3);
        RunTracker.RecordGoldPlatedCablesActivationForTest(
            agg,
            "ORB.FROST",
            "Frost",
            2);
        RunTracker.RecordGoldPlatedCablesNoOrbTargetForTest(agg, 2);
        return agg;
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException(
                "BuildGoldPlatedCablesBodyBBCode returned null."));
}
