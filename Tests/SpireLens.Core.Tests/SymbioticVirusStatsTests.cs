using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class SymbioticVirusStatsTests
{
    private const string SymbioticVirusRelicId = "RELIC.SYMBIOTIC_VIRUS";

    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildSymbioticVirusBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "BuildSymbioticVirusBodyBBCode not found.");

    [Fact]
    public void Patches_TargetSymbioticVirusAndDarkOrbLifecycleMethods()
    {
        Assert.NotNull(typeof(SymbioticVirus).GetMethod(
            nameof(SymbioticVirus.AfterSideTurnStart)));
        Assert.NotNull(typeof(DarkOrb).GetMethod(
            nameof(DarkOrb.Passive),
            [typeof(PlayerChoiceContext), typeof(Creature)]));
        Assert.NotNull(typeof(DarkOrb).GetMethod(
            nameof(DarkOrb.Evoke),
            [typeof(PlayerChoiceContext)]));
    }

    [Fact]
    public void RelicAggregate_SymbioticVirusFields_JsonRoundtripPreservesValues()
    {
        var run = new RunData();
        run.RelicAggregates[SymbioticVirusRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(
            json,
            RunStorage.Options);

        Assert.Contains("\"symbiotic_virus_orb_evokes\"", json);
        Assert.Contains("\"symbiotic_virus_orb_passive_triggers\"", json);
        Assert.Contains("\"symbiotic_virus_orb_fizzles\"", json);
        Assert.NotNull(restored);
        AssertPopulatedAggregate(
            restored!.RelicAggregates[SymbioticVirusRelicId]);
    }

    [Fact]
    public void RunTracker_SymbioticVirusHelpers_AccumulateLifecycleOutcomes()
    {
        var aggregate = new RelicAggregate();

        RunTracker.RecordSymbioticVirusOrbEvokedForTest(aggregate, 3);
        RunTracker.RecordSymbioticVirusOrbPassiveForTest(aggregate, 7);
        RunTracker.RecordSymbioticVirusOrbFizzledForTest(aggregate);

        AssertPopulatedAggregate(aggregate);
    }

    [Fact]
    public void RelicAggregate_SymbioticVirusFields_Merge()
    {
        var target = PopulatedAggregate();

        RunTracker.MergeRelicAggregateInto(target, PopulatedAggregate());

        Assert.Equal(6, target.SymbioticVirusOrbEvokes);
        Assert.Equal(14, target.SymbioticVirusOrbPassiveTriggers);
        Assert.Equal(2, target.SymbioticVirusOrbFizzles);
    }

    [Fact]
    public void RelicTooltip_SymbioticVirus_ShowsStartingOrbLifecycle()
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
    public void RelicTooltip_SymbioticVirus_DispatchesForModel()
    {
        var relic = (SymbioticVirus)
            RuntimeHelpers.GetUninitializedObject(typeof(SymbioticVirus));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Symbiotic Virus", title);
        Assert.Contains("Times orb was evoked", body);
    }

    [Fact]
    public void RelicAggregate_OlderShapeWithoutSymbioticVirusFields_DefaultsToZero()
    {
        var aggregate = JsonSerializer.Deserialize<RelicAggregate>(
            "{}",
            RunStorage.Options);

        Assert.NotNull(aggregate);
        Assert.Equal(0, aggregate!.SymbioticVirusOrbEvokes);
        Assert.Equal(0, aggregate.SymbioticVirusOrbPassiveTriggers);
        Assert.Equal(0, aggregate.SymbioticVirusOrbFizzles);
    }

    private static RelicAggregate PopulatedAggregate()
    {
        var aggregate = new RelicAggregate();
        RunTracker.RecordSymbioticVirusOrbEvokedForTest(aggregate, 3);
        RunTracker.RecordSymbioticVirusOrbPassiveForTest(aggregate, 7);
        RunTracker.RecordSymbioticVirusOrbFizzledForTest(aggregate);
        return aggregate;
    }

    private static void AssertPopulatedAggregate(RelicAggregate aggregate)
    {
        Assert.Equal(3, aggregate.SymbioticVirusOrbEvokes);
        Assert.Equal(7, aggregate.SymbioticVirusOrbPassiveTriggers);
        Assert.Equal(1, aggregate.SymbioticVirusOrbFizzles);
    }

    private static string BuildBody(RelicAggregate aggregate)
        => (string)(BuildBodyMethod.Invoke(null, [aggregate])
            ?? throw new InvalidOperationException(
                "BuildSymbioticVirusBodyBBCode returned null."));
}
