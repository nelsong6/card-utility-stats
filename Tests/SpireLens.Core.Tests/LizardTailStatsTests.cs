using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class LizardTailStatsTests
{
    private const string LizardTailRelicId = "RELIC.LIZARD_TAIL";

    private static readonly MethodInfo BuildLizardTailBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildLizardTailBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildLizardTailBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_LizardTailFields_DefaultToZeroOrNull()
    {
        var agg = new RelicAggregate();

        Assert.Null(agg.FloorAcquired);
        Assert.Null(agg.FloorActivated);
        Assert.Equal(0m, agg.TotalHealingRestored);
    }

    [Fact]
    public void RelicAggregate_LizardTailFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[LizardTailRelicId] = new RelicAggregate
        {
            Activations = 1,
            FloorAcquired = 7,
            FloorActivated = 19,
            TotalHealingAttempted = 36m,
            TotalHealingRestored = 36m,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("floor_acquired", json);
        Assert.Contains("floor_activated", json);
        Assert.Contains("total_healing_restored", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[LizardTailRelicId];
        Assert.Equal(1, restoredAgg.Activations);
        Assert.Equal(7, restoredAgg.FloorAcquired);
        Assert.Equal(19, restoredAgg.FloorActivated);
        Assert.Equal(36m, restoredAgg.TotalHealingAttempted);
        Assert.Equal(36m, restoredAgg.TotalHealingRestored);
    }

    [Fact]
    public void MergeRelicAggregateInto_LizardTailFields_PreservesPickupAndUpdatesActivation()
    {
        var target = new RelicAggregate
        {
            FloorAcquired = 7,
            TotalHealingRestored = 12m,
        };
        var source = new RelicAggregate
        {
            FloorAcquired = 9,
            FloorActivated = 19,
            TotalHealingRestored = 24m,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(7, target.FloorAcquired);
        Assert.Equal(19, target.FloorActivated);
        Assert.Equal(36m, target.TotalHealingRestored);
    }

    [Fact]
    public void RelicFloorHelpers_KeepFirstAcquiredFloorAndLatestActivationFloor()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordRelicFloorAcquiredForTest(agg, 7);
        RunTracker.RecordRelicFloorAcquiredForTest(agg, 9);
        RunTracker.RecordRelicFloorActivatedForTest(agg, 19);
        RunTracker.RecordRelicFloorActivatedForTest(agg, 20);

        Assert.Equal(7, agg.FloorAcquired);
        Assert.Equal(20, agg.FloorActivated);
    }

    [Fact]
    public void RelicTooltip_LizardTailFields_ShowActivationAndHealing()
    {
        var body = BuildBody(new RelicAggregate
        {
            FloorAcquired = 7,
            FloorActivated = 19,
            TotalHealingRestored = 36m,
        });

        Assert.Contains("Floor acquired", body);
        Assert.Contains("[b]7[/b]", body);
        Assert.Contains("Floor activated", body);
        Assert.Contains("[b]19[/b]", body);
        Assert.Contains("HP healed", body);
        Assert.Contains("[b]36[/b]", body);
    }

    [Fact]
    public void RelicTooltip_LizardTailFields_ShowFallbackAndNoneYet()
    {
        var body = BuildBody(new RelicAggregate(), floorAcquiredFallback: 8);

        Assert.Contains("Floor acquired", body);
        Assert.Contains("[b]8[/b]", body);
        Assert.Contains("Floor activated", body);
        Assert.Contains("[b]none yet[/b]", body);
        Assert.Contains("HP healed", body);
        Assert.Contains("[b]0[/b]", body);
    }

    private static string BuildBody(RelicAggregate agg, int? floorAcquiredFallback = null)
        => (string)(BuildLizardTailBodyMethod.Invoke(null, new object?[] { agg, floorAcquiredFallback })
            ?? throw new InvalidOperationException("BuildLizardTailBodyBBCode returned null."));
}
