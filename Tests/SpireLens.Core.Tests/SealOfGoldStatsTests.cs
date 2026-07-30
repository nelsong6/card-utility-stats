using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class SealOfGoldStatsTests
{
    private const string SealOfGoldRelicId = "RELIC.SEAL_OF_GOLD";

    private static readonly MethodInfo TargetMethod =
        typeof(SealOfGoldStatsPatch).GetMethod("TargetMethod", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Seal of Gold TargetMethod not found.");

    private static readonly MethodInfo BuildSealOfGoldBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildSealOfGoldBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildSealOfGoldBodyBBCode not found.");

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void Patch_TargetsSealOfGoldTurnStartWithExpectedParameters()
    {
        var target = TargetMethod.Invoke(null, null) as MethodBase;

        Assert.NotNull(target);
        Assert.Equal(typeof(SealOfGold), target!.DeclaringType);
        Assert.Equal(nameof(SealOfGold.AfterSideTurnStart), target.Name);
        Assert.Equal(
            new[]
            {
                typeof(CombatSide),
                typeof(IReadOnlyList<Creature>),
                typeof(ICombatState),
            },
            target.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void RelicAggregate_SealOfGoldFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0, agg.GoldLost);
        Assert.Equal(0, agg.GoldLossBlocked);
        Assert.Equal(0, agg.EnergyGenerated);
        Assert.Equal(0, agg.EnergyGeneratedCombats);
    }

    [Fact]
    public void RelicAggregate_SealOfGoldFields_JsonRoundtripPreservesValues()
    {
        var run = new RunData();
        run.RelicAggregates[SealOfGoldRelicId] = new RelicAggregate
        {
            Activations = 4,
            GoldLost = 17,
            GoldLossBlocked = 3,
            EnergyGenerated = 3,
            EnergyGeneratedCombats = 2,
        };

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"gold_lost\"", json);
        Assert.Contains("\"gold_loss_blocked\"", json);
        Assert.Contains("\"energy_generated\"", json);
        Assert.Contains("\"energy_generated_combats\"", json);
        Assert.NotNull(restored);

        var agg = restored!.RelicAggregates[SealOfGoldRelicId];
        Assert.Equal(4, agg.Activations);
        Assert.Equal(17, agg.GoldLost);
        Assert.Equal(3, agg.GoldLossBlocked);
        Assert.Equal(3, agg.EnergyGenerated);
        Assert.Equal(2, agg.EnergyGeneratedCombats);
    }

    [Fact]
    public void RunTracker_SealOfGold_AccumulatesObservedGoldAndEnergyOutcomes()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordSealOfGoldActivationForTest(
            agg,
            intendedGoldLoss: 5,
            initialGold: 20,
            finalGold: 15,
            initialEnergy: 3,
            finalEnergy: 4);
        RunTracker.RecordSealOfGoldActivationForTest(
            agg,
            intendedGoldLoss: 5,
            initialGold: 5,
            finalGold: 3,
            initialEnergy: 4,
            finalEnergy: 4);

        Assert.Equal(2, agg.Activations);
        Assert.Equal(7, agg.GoldLost);
        Assert.Equal(3, agg.GoldLossBlocked);
        Assert.Equal(1, agg.EnergyGenerated);
    }

    [Fact]
    public void RelicAggregate_SealOfGoldFields_Merge()
    {
        var target = new RelicAggregate
        {
            Activations = 2,
            GoldLost = 8,
            GoldLossBlocked = 2,
            EnergyGenerated = 2,
            EnergyGeneratedCombats = 1,
        };

        RunTracker.MergeRelicAggregateInto(target, new RelicAggregate
        {
            Activations = 3,
            GoldLost = 12,
            GoldLossBlocked = 3,
            EnergyGenerated = 2,
            EnergyGeneratedCombats = 2,
        });

        Assert.Equal(5, target.Activations);
        Assert.Equal(20, target.GoldLost);
        Assert.Equal(5, target.GoldLossBlocked);
        Assert.Equal(4, target.EnergyGenerated);
        Assert.Equal(3, target.EnergyGeneratedCombats);
    }

    [Fact]
    public void RelicTooltip_SealOfGold_ShowsGoldAndBossEnergyStats()
    {
        var body = BuildBody(new RelicAggregate
        {
            Activations = 4,
            GoldLost = 17,
            GoldLossBlocked = 3,
            EnergyGenerated = 3,
            EnergyGeneratedCombats = 2,
        });

        Assert.Contains("Times triggered", body);
        Assert.Contains("Gold loss attempted", body);
        Assert.Contains("Gold lost", body);
        Assert.Contains("Gold loss blocked", body);
        Assert.Contains("Energy gained total", body);
        Assert.Contains("Avg energy gained per combat", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.Contains("[b]20[/b]", body);
        Assert.Contains("[b]17[/b]", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[b]1.5[/b]", body);
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void RelicTooltip_SealOfGold_DispatchesForModel()
    {
        var relic = (SealOfGold)RuntimeHelpers.GetUninitializedObject(typeof(SealOfGold));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Seal of Gold", title);
        Assert.Contains("Gold loss attempted", body);
        Assert.Contains("Energy gained total", body);
    }

    [Fact]
    public void RelicAggregate_OlderShapeWithoutSealOfGoldFields_DefaultsToZero()
    {
        var agg = JsonSerializer.Deserialize<RelicAggregate>("{}", RunStorage.Options);

        Assert.NotNull(agg);
        Assert.Equal(0, agg!.GoldLost);
        Assert.Equal(0, agg.GoldLossBlocked);
        Assert.Equal(0, agg.EnergyGenerated);
        Assert.Equal(0, agg.EnergyGeneratedCombats);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildSealOfGoldBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildSealOfGoldBodyBBCode returned null."));
}
