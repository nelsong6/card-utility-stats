using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class StoneHumidifierStatsTests
{
    private const string StoneHumidifierRelicId = "RELIC.STONE_HUMIDIFIER";

    private static readonly MethodInfo BuildStoneHumidifierBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildStoneHumidifierBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildStoneHumidifierBodyBBCode not found.");

    [Fact]
    public void Patch_TargetsStoneHumidifierAfterRestSiteHealWithExpectedParameters()
    {
        var target = typeof(StoneHumidifier).GetMethod(nameof(StoneHumidifier.AfterRestSiteHeal));

        Assert.NotNull(target);
        Assert.Equal(
            new[] { typeof(Player), typeof(bool) },
            target!.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void RelicAggregate_StoneHumidifierFields_DefaultToZeroAndEmpty()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.MaxHpGained);
        Assert.Empty(agg.MaxHpActivations);
    }

    [Fact]
    public void RelicAggregate_StoneHumidifierFields_JsonRoundtripPreservesNestedActivations()
    {
        var run = new RunData();
        run.RelicAggregates[StoneHumidifierRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"max_hp_activations\"", json);
        Assert.Contains("\"starting_hp\"", json);
        Assert.Contains("\"resulting_hp\"", json);
        Assert.NotNull(restored);

        AssertPopulatedAggregate(restored!.RelicAggregates[StoneHumidifierRelicId]);
    }

    [Fact]
    public void RunTracker_StoneHumidifierHelper_AccumulatesObservedGainsAndSnapshots()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordStoneHumidifierMaxHpGainForTest(agg, 70m, 75m);
        RunTracker.RecordStoneHumidifierMaxHpGainForTest(agg, 80m, 84m);

        AssertPopulatedAggregate(agg);
    }

    [Fact]
    public void RunTracker_StoneHumidifierHelper_CountsZeroGainAndClampsNegativeHp()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordStoneHumidifierMaxHpGainForTest(agg, -5m, -1m);

        Assert.Equal(1, agg.Activations);
        Assert.Equal(0m, agg.MaxHpGained);
        var activation = Assert.Single(agg.MaxHpActivations);
        Assert.Equal(0m, activation.StartingHp);
        Assert.Equal(0m, activation.ResultingHp);
    }

    [Fact]
    public void RelicAggregate_StoneHumidifierFields_MergePreservesActivationOrder()
    {
        var target = new RelicAggregate();
        RunTracker.RecordStoneHumidifierMaxHpGainForTest(target, 70m, 75m);
        var source = new RelicAggregate();
        RunTracker.RecordStoneHumidifierMaxHpGainForTest(source, 80m, 84m);

        RunTracker.MergeRelicAggregateInto(target, source);

        AssertPopulatedAggregate(target);
    }

    [Fact]
    public void RelicTooltip_StoneHumidifier_ShowsTotalsAndEveryActivationSnapshot()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("Times triggered", body);
        Assert.Contains("Max HP gained", body);
        Assert.Contains("Activation 1 starting HP", body);
        Assert.Contains("Activation 1 resulting HP", body);
        Assert.Contains("Activation 2 starting HP", body);
        Assert.Contains("Activation 2 resulting HP", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]9[/b]", body);
        Assert.Contains("[b]70[/b]", body);
        Assert.Contains("[b]75[/b]", body);
        Assert.Contains("[b]80[/b]", body);
        Assert.Contains("[b]84[/b]", body);
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void RelicTooltip_StoneHumidifier_DispatchesForModel()
    {
        var relic = (StoneHumidifier)RuntimeHelpers.GetUninitializedObject(typeof(StoneHumidifier));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Stone Humidifier", title);
        Assert.Contains("Times triggered", body);
    }

    [Fact]
    public void RelicAggregate_OlderShapeWithoutStoneHumidifierFields_DefaultsToEmpty()
    {
        var agg = JsonSerializer.Deserialize<RelicAggregate>("{}", RunStorage.Options);

        Assert.NotNull(agg);
        Assert.Equal(0, agg!.Activations);
        Assert.Equal(0m, agg.MaxHpGained);
        Assert.Empty(agg.MaxHpActivations);
    }

    private static RelicAggregate PopulatedAggregate()
    {
        var agg = new RelicAggregate();
        RunTracker.RecordStoneHumidifierMaxHpGainForTest(agg, 70m, 75m);
        RunTracker.RecordStoneHumidifierMaxHpGainForTest(agg, 80m, 84m);
        return agg;
    }

    private static void AssertPopulatedAggregate(RelicAggregate agg)
    {
        Assert.Equal(2, agg.Activations);
        Assert.Equal(9m, agg.MaxHpGained);
        Assert.Collection(
            agg.MaxHpActivations,
            activation =>
            {
                Assert.Equal(70m, activation.StartingHp);
                Assert.Equal(75m, activation.ResultingHp);
            },
            activation =>
            {
                Assert.Equal(80m, activation.StartingHp);
                Assert.Equal(84m, activation.ResultingHp);
            });
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildStoneHumidifierBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildStoneHumidifierBodyBBCode returned null."));
}
