using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Orbs;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class CrackedCoreStatsTests
{
    private const string CrackedCoreRelicId = "RELIC.CRACKED_CORE";

    private static readonly MethodInfo BuildCrackedCoreBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildCrackedCoreBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildCrackedCoreBodyBBCode not found.");

    [Fact]
    public void Patches_TargetCrackedCoreAndOrbLifecycleMethods()
    {
        Assert.NotNull(typeof(CrackedCore).GetMethod(nameof(CrackedCore.BeforeSideTurnStart)));
        Assert.NotNull(typeof(LightningOrb).GetMethod(
            nameof(LightningOrb.Passive),
            new[] { typeof(PlayerChoiceContext), typeof(Creature) }));
        Assert.NotNull(typeof(LightningOrb).GetMethod(
            nameof(LightningOrb.Evoke),
            new[] { typeof(PlayerChoiceContext) }));
        Assert.NotNull(typeof(OrbQueue).GetMethod(
            nameof(OrbQueue.RemoveCapacity),
            new[] { typeof(int) }));
    }

    [Fact]
    public void RelicAggregate_CrackedCoreFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.CrackedCoreOrbEvokes);
        Assert.Equal(0, agg.CrackedCoreOrbPassiveTriggers);
        Assert.Equal(0, agg.CrackedCoreOrbFizzles);
    }

    [Fact]
    public void RelicAggregate_CrackedCoreFields_JsonRoundtripPreservesValues()
    {
        var run = new RunData();
        run.RelicAggregates[CrackedCoreRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"cracked_core_orb_evokes\"", json);
        Assert.Contains("\"cracked_core_orb_passive_triggers\"", json);
        Assert.Contains("\"cracked_core_orb_fizzles\"", json);
        Assert.NotNull(restored);
        AssertPopulatedAggregate(restored!.RelicAggregates[CrackedCoreRelicId]);
    }

    [Fact]
    public void RunTracker_CrackedCoreHelpers_AccumulateLifecycleOutcomes()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordCrackedCoreOrbEvokedForTest(agg, 3);
        RunTracker.RecordCrackedCoreOrbPassiveForTest(agg, 7);
        RunTracker.RecordCrackedCoreOrbFizzledForTest(agg);

        AssertPopulatedAggregate(agg);
    }

    [Fact]
    public void RunTracker_CrackedCoreHelpers_IgnoreNegativeCounts()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordCrackedCoreOrbEvokedForTest(agg, -1);
        RunTracker.RecordCrackedCoreOrbPassiveForTest(agg, -2);
        RunTracker.RecordCrackedCoreOrbFizzledForTest(agg, -3);

        Assert.Equal(0, agg.CrackedCoreOrbEvokes);
        Assert.Equal(0, agg.CrackedCoreOrbPassiveTriggers);
        Assert.Equal(0, agg.CrackedCoreOrbFizzles);
    }

    [Fact]
    public void RelicAggregate_CrackedCoreFields_Merge()
    {
        var target = PopulatedAggregate();

        RunTracker.MergeRelicAggregateInto(target, PopulatedAggregate());

        Assert.Equal(6, target.CrackedCoreOrbEvokes);
        Assert.Equal(14, target.CrackedCoreOrbPassiveTriggers);
        Assert.Equal(2, target.CrackedCoreOrbFizzles);
    }

    [Fact]
    public void RelicTooltip_CrackedCore_ShowsStartingOrbLifecycle()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("Times orb was evoked", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("Times orb passive triggered", body);
        Assert.Contains("[b]7[/b]", body);
        Assert.Contains("Times orb fizzled", body);
        Assert.Contains("[b]1[/b]", body);
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void RelicTooltip_CrackedCore_DispatchesForModel()
    {
        var relic = (CrackedCore)RuntimeHelpers.GetUninitializedObject(typeof(CrackedCore));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Cracked Core", title);
        Assert.Contains("Times orb was evoked", body);
    }

    [Fact]
    public void RelicAggregate_OlderShapeWithoutCrackedCoreFields_DefaultsToZero()
    {
        var agg = JsonSerializer.Deserialize<RelicAggregate>("{}", RunStorage.Options);

        Assert.NotNull(agg);
        Assert.Equal(0, agg!.CrackedCoreOrbEvokes);
        Assert.Equal(0, agg.CrackedCoreOrbPassiveTriggers);
        Assert.Equal(0, agg.CrackedCoreOrbFizzles);
    }

    private static RelicAggregate PopulatedAggregate()
    {
        var agg = new RelicAggregate();
        RunTracker.RecordCrackedCoreOrbEvokedForTest(agg, 3);
        RunTracker.RecordCrackedCoreOrbPassiveForTest(agg, 7);
        RunTracker.RecordCrackedCoreOrbFizzledForTest(agg);
        return agg;
    }

    private static void AssertPopulatedAggregate(RelicAggregate agg)
    {
        Assert.Equal(3, agg.CrackedCoreOrbEvokes);
        Assert.Equal(7, agg.CrackedCoreOrbPassiveTriggers);
        Assert.Equal(1, agg.CrackedCoreOrbFizzles);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildCrackedCoreBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildCrackedCoreBodyBBCode returned null."));
}
