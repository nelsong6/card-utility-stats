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

public class RuinedHelmetStatsTests
{
    private const string RuinedHelmetRelicId = "RELIC.RUINED_HELMET";

    private static readonly MethodInfo BuildRuinedHelmetBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildRuinedHelmetBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildRuinedHelmetBodyBBCode not found.");

    [Fact]
    public void Patches_TargetExactRuinedHelmetStrengthModifierCallbacks()
    {
        var modifyTarget = typeof(RuinedHelmet).GetMethod(
            nameof(RuinedHelmet.TryModifyPowerAmountReceived));
        var appliedTarget = typeof(RuinedHelmet).GetMethod(
            nameof(RuinedHelmet.AfterModifyingPowerAmountReceived));

        Assert.NotNull(modifyTarget);
        Assert.Equal(
            new[]
            {
                typeof(PowerModel),
                typeof(Creature),
                typeof(decimal),
                typeof(Creature),
                typeof(decimal).MakeByRefType(),
            },
            modifyTarget!.GetParameters().Select(parameter => parameter.ParameterType));

        Assert.NotNull(appliedTarget);
        Assert.Equal(
            new[] { typeof(PowerModel) },
            appliedTarget!.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void RunTracker_RuinedHelmetTrackingWindow_IncludesRoomEntrySetup(
        bool hasPendingCombat,
        bool combatIsInProgress,
        bool expected)
    {
        Assert.Equal(
            expected,
            RunTracker.IsRuinedHelmetStrengthTrackingWindow(
                hasPendingCombat,
                combatIsInProgress));
    }

    [Fact]
    public void RelicAggregate_RuinedHelmetFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.StrengthAdded);
        Assert.Equal(0, agg.RuinedHelmetCombats);
    }

    [Fact]
    public void RelicAggregate_RuinedHelmetFields_JsonRoundtripPreservesValues()
    {
        var run = new RunData();
        run.RelicAggregates[RuinedHelmetRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"activations\"", json);
        Assert.Contains("\"strength_added\"", json);
        Assert.Contains("\"ruined_helmet_combats\"", json);
        Assert.DoesNotContain("ruined_helmet_strength_added_this_combat", json);
        Assert.NotNull(restored);
        AssertPopulatedAggregate(restored!.RelicAggregates[RuinedHelmetRelicId]);
    }

    [Fact]
    public void RunTracker_RuinedHelmetHelpers_AccumulateActivationsBonusAndHeldCombats()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordRuinedHelmetStrengthGainForTest(agg, 2m);
        RunTracker.RecordRuinedHelmetStrengthGainForTest(agg, 5.5m);
        RunTracker.RecordRuinedHelmetCombatForTest(agg, 3);

        AssertPopulatedAggregate(agg);
    }

    [Fact]
    public void RunTracker_RuinedHelmetHelpers_IgnoreNegativeValuesAndCounts()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordRuinedHelmetStrengthGainForTest(agg, -2m);
        RunTracker.RecordRuinedHelmetStrengthGainForTest(agg, 0m);
        RunTracker.RecordRuinedHelmetCombatForTest(agg, -1);

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.StrengthAdded);
        Assert.Equal(0, agg.RuinedHelmetCombats);
    }

    [Fact]
    public void RelicAggregate_RuinedHelmetFields_Merge()
    {
        var target = PopulatedAggregate();

        RunTracker.MergeRelicAggregateInto(target, PopulatedAggregate());

        Assert.Equal(4, target.Activations);
        Assert.Equal(15m, target.StrengthAdded);
        Assert.Equal(6, target.RuinedHelmetCombats);
    }

    [Fact]
    public void RelicTooltip_RuinedHelmet_ShowsActivationsAndStrengthAverages()
    {
        var agg = PopulatedAggregate();
        agg.RuinedHelmetStrengthAddedThisCombat = 2m;

        var body = BuildBody(agg);

        Assert.Contains("Times activated", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("Activated this combat", body);
        Assert.Contains("[b]true[/b]", body);
        Assert.Contains("Total strength gained", body);
        Assert.Contains("[b]7.5[/b]", body);
        Assert.Contains("Strength gained this combat", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("Avg strength gained per activation", body);
        Assert.Contains("[b]3.75[/b]", body);
        Assert.Contains("Avg strength gained per combat", body);
        Assert.Contains("[b]2.5[/b]", body);
    }

    [Fact]
    public void RelicTooltip_RuinedHelmet_ShowsZeroAveragesWithoutDenominators()
    {
        var body = BuildBody(new RelicAggregate { StrengthAdded = 4m });

        Assert.Contains("Times activated", body);
        Assert.Contains("Activated this combat", body);
        Assert.Contains("[b]false[/b]", body);
        Assert.Contains("Total strength gained", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.Contains("Strength gained this combat", body);
        Assert.Contains("Avg strength gained per activation", body);
        Assert.Contains("Avg strength gained per combat", body);
        Assert.Contains("[b]0[/b]", body);
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void RelicTooltip_RuinedHelmet_DispatchesForModel()
    {
        var relic = (RuinedHelmet)RuntimeHelpers.GetUninitializedObject(typeof(RuinedHelmet));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Ruined Helmet", title);
        Assert.Contains("Total strength gained", body);
    }

    [Fact]
    public void RelicAggregate_OlderShapeWithoutRuinedHelmetFields_DefaultsToZero()
    {
        var agg = JsonSerializer.Deserialize<RelicAggregate>("{}", RunStorage.Options);

        Assert.NotNull(agg);
        Assert.Equal(0, agg!.Activations);
        Assert.Equal(0m, agg.StrengthAdded);
        Assert.Equal(0, agg.RuinedHelmetCombats);
    }

    private static RelicAggregate PopulatedAggregate()
    {
        var agg = new RelicAggregate();
        RunTracker.RecordRuinedHelmetStrengthGainForTest(agg, 2m);
        RunTracker.RecordRuinedHelmetStrengthGainForTest(agg, 5.5m);
        RunTracker.RecordRuinedHelmetCombatForTest(agg, 3);
        return agg;
    }

    private static void AssertPopulatedAggregate(RelicAggregate agg)
    {
        Assert.Equal(2, agg.Activations);
        Assert.Equal(7.5m, agg.StrengthAdded);
        Assert.Equal(3, agg.RuinedHelmetCombats);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildRuinedHelmetBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildRuinedHelmetBodyBBCode returned null."));
}
