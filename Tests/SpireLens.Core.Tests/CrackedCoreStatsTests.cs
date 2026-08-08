using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Orbs;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class CrackedCoreStatsTests
{
    private const string CrackedCoreRelicId = "RELIC.CRACKED_CORE";
    private const string InfusedCoreRelicId = "RELIC.INFUSED_CORE";

    private static readonly MethodInfo BuildStartingLightningCoreBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildStartingLightningCoreBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "BuildStartingLightningCoreBodyBBCode not found.");

    [Fact]
    public void Patches_TargetStartingCoresAndOrbLifecycleMethods()
    {
        Assert.NotNull(typeof(CrackedCore).GetMethod(nameof(CrackedCore.BeforeSideTurnStart)));
        var infusedCoreStart = typeof(InfusedCore).GetMethod(
            nameof(InfusedCore.AfterSideTurnStart),
            new[]
            {
                typeof(CombatSide),
                typeof(IReadOnlyList<Creature>),
                typeof(ICombatState),
            });
        Assert.NotNull(infusedCoreStart);
        Assert.Equal(
            new[] { "side", "participants", "combatState" },
            infusedCoreStart!.GetParameters()
                .Select(parameter => parameter.Name)
                .ToArray());
        Assert.NotNull(typeof(LightningOrb).GetMethod(
            nameof(LightningOrb.Passive),
            new[] { typeof(PlayerChoiceContext), typeof(Creature) }));
        Assert.NotNull(typeof(LightningOrb).GetMethod(
            nameof(LightningOrb.Evoke),
            new[] { typeof(PlayerChoiceContext) }));
        Assert.NotNull(typeof(OrbQueue).GetMethod(
            nameof(OrbQueue.RemoveCapacity),
            new[] { typeof(int) }));

        var applyLightningDamage = typeof(LightningOrb).GetMethod(
            "ApplyLightningDamage",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(applyLightningDamage);
        Assert.Equal(
            new[]
            {
                typeof(decimal),
                typeof(Creature),
                typeof(PlayerChoiceContext),
                typeof(bool),
            },
            applyLightningDamage!.GetParameters()
                .Select(parameter => parameter.ParameterType)
                .ToArray());
        Assert.Equal(
            new[] { "value", "target", "choiceContext", "isEvoke" },
            applyLightningDamage.GetParameters()
                .Select(parameter => parameter.Name)
                .ToArray());

        var damage = typeof(CreatureCmd).GetMethod(
            nameof(CreatureCmd.Damage),
            new[]
            {
                typeof(PlayerChoiceContext),
                typeof(IEnumerable<Creature>),
                typeof(decimal),
                typeof(ValueProp),
                typeof(Creature),
            });
        Assert.NotNull(damage);
        Assert.Equal(
            new[] { "choiceContext", "targets", "amount", "props", "dealer" },
            damage!.GetParameters()
                .Select(parameter => parameter.Name)
                .ToArray());
    }

    [Fact]
    public void RelicAggregate_CrackedCoreFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.CrackedCoreOrbEvokes);
        Assert.Equal(0, agg.CrackedCoreOrbPassiveTriggers);
        Assert.Equal(0, agg.CrackedCoreOrbFizzles);
        Assert.Equal(0, agg.TotalDamageAttempted);
        Assert.Equal(0, agg.TotalDamageDealt);
        Assert.Equal(0, agg.TotalDamageBlocked);
        Assert.Equal(0, agg.TotalDamageOverkill);
        Assert.Equal(0, agg.Kills);
        Assert.Equal(0, agg.TotalTargets);
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
        Assert.Contains("\"total_damage_attempted\"", json);
        Assert.Contains("\"total_damage_dealt\"", json);
        Assert.Contains("\"total_damage_blocked\"", json);
        Assert.Contains("\"total_damage_overkill\"", json);
        Assert.Contains("\"kills\"", json);
        Assert.Contains("\"total_targets\"", json);
        Assert.NotNull(restored);
        AssertPopulatedAggregate(restored!.RelicAggregates[CrackedCoreRelicId]);
    }

    [Fact]
    public void RelicAggregate_InfusedCore_UsesStartingLightningCoreFields()
    {
        var run = new RunData();
        run.RelicAggregates[InfusedCoreRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.NotNull(restored);
        AssertPopulatedAggregate(restored!.RelicAggregates[InfusedCoreRelicId]);
    }

    [Fact]
    public void RunTracker_CrackedCoreHelpers_AccumulateLifecycleAndDamageOutcomes()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordCrackedCoreOrbEvokedForTest(agg, 3);
        RunTracker.RecordCrackedCoreOrbPassiveForTest(agg, 7);
        RunTracker.RecordCrackedCoreOrbFizzledForTest(agg);
        RecordRepresentativeDamage(agg);

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
        Assert.Equal(30, target.TotalDamageAttempted);
        Assert.Equal(20, target.TotalDamageDealt);
        Assert.Equal(4, target.TotalDamageBlocked);
        Assert.Equal(6, target.TotalDamageOverkill);
        Assert.Equal(2, target.Kills);
        Assert.Equal(4, target.TotalTargets);
    }

    [Fact]
    public void RelicTooltip_CrackedCore_ShowsStartingOrbLifecycle()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("[hint=", body);
        Assert.Contains("res://images/orbs/lightning_orb.png", body);
        Assert.Equal(
            9,
            body.Split(
                "res://images/orbs/lightning_orb.png",
                StringSplitOptions.None).Length - 1);
        Assert.Contains("starting Lightning orb was evoked", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("Passive activations of the starting Lightning orb", body);
        Assert.Contains("[b]7[/b]", body);
        Assert.Contains("Starting Lightning orbs removed without being evoked", body);
        Assert.Contains("[b]1[/b]", body);
        Assert.Contains("Damage attempted", body);
        Assert.Contains("[b]15[/b]", body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("damage"), body);
        Assert.Contains("[b]10[/b]", body);
        Assert.Contains("Damage blocked", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("Overkill", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("Kills", body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("targets_hit"), body);
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
        Assert.Contains("res://images/orbs/lightning_orb.png", body);
        Assert.Contains("starting Lightning orb was evoked", body);
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void RelicTooltip_InfusedCore_DispatchesForModel()
    {
        var relic = (InfusedCore)RuntimeHelpers.GetUninitializedObject(
            typeof(InfusedCore));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Infused Core", title);
        Assert.Contains("res://images/orbs/lightning_orb.png", body);
        Assert.Contains("starting Lightning orb was evoked", body);
        Assert.Contains("Damage dealt", body);
    }

    [Fact]
    public void RelicAggregate_OlderShapeWithoutCrackedCoreFields_DefaultsToZero()
    {
        var agg = JsonSerializer.Deserialize<RelicAggregate>("{}", RunStorage.Options);

        Assert.NotNull(agg);
        Assert.Equal(0, agg!.CrackedCoreOrbEvokes);
        Assert.Equal(0, agg.CrackedCoreOrbPassiveTriggers);
        Assert.Equal(0, agg.CrackedCoreOrbFizzles);
        Assert.Equal(0, agg.TotalDamageAttempted);
        Assert.Equal(0, agg.TotalDamageDealt);
        Assert.Equal(0, agg.TotalDamageBlocked);
        Assert.Equal(0, agg.TotalDamageOverkill);
        Assert.Equal(0, agg.Kills);
        Assert.Equal(0, agg.TotalTargets);
    }

    private static RelicAggregate PopulatedAggregate()
    {
        var agg = new RelicAggregate();
        RunTracker.RecordCrackedCoreOrbEvokedForTest(agg, 3);
        RunTracker.RecordCrackedCoreOrbPassiveForTest(agg, 7);
        RunTracker.RecordCrackedCoreOrbFizzledForTest(agg);
        RecordRepresentativeDamage(agg);
        return agg;
    }

    private static void RecordRepresentativeDamage(RelicAggregate agg)
    {
        RunTracker.RecordCrackedCoreOrbDamageForTest(
            agg,
            [
                (
                    BlockedDamage: 2,
                    UnblockedDamage: 6,
                    OverkillDamage: 0,
                    WasTargetKilled: false),
                (
                    BlockedDamage: 0,
                    UnblockedDamage: 4,
                    OverkillDamage: 3,
                    WasTargetKilled: true),
            ]);
    }

    private static void AssertPopulatedAggregate(RelicAggregate agg)
    {
        Assert.Equal(3, agg.CrackedCoreOrbEvokes);
        Assert.Equal(7, agg.CrackedCoreOrbPassiveTriggers);
        Assert.Equal(1, agg.CrackedCoreOrbFizzles);
        Assert.Equal(15, agg.TotalDamageAttempted);
        Assert.Equal(10, agg.TotalDamageDealt);
        Assert.Equal(2, agg.TotalDamageBlocked);
        Assert.Equal(3, agg.TotalDamageOverkill);
        Assert.Equal(1, agg.Kills);
        Assert.Equal(2, agg.TotalTargets);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildStartingLightningCoreBodyMethod.Invoke(
                null,
                new object?[] { agg })
            ?? throw new InvalidOperationException(
                "BuildStartingLightningCoreBodyBBCode returned null."));
}
