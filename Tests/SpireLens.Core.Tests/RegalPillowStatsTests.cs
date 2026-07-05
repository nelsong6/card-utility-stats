using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class RegalPillowStatsTests
{
    private const string RegalPillowRelicId = "RELIC.REGAL_PILLOW";

    private static readonly MethodInfo BuildRegalPillowBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildRegalPillowBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildRegalPillowBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_RegalPillowFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.TotalHealingAttempted);
        Assert.Equal(0m, agg.TotalHealingRestored);
        Assert.Equal(0m, agg.TotalHealingLost);
        Assert.Empty(agg.HealingLostReasons);
    }

    [Fact]
    public void RelicAggregate_RegalPillowFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        var agg = new RelicAggregate
        {
            Activations = 2,
            TotalHealingAttempted = 12m,
            TotalHealingRestored = 9m,
            TotalHealingLost = 3m,
        };
        agg.HealingLostReasons["full_hp"] = new HealingLostReasonAggregate
        {
            ReasonId = "full_hp",
            DisplayName = "full HP",
            Amount = 3m,
        };
        run.RelicAggregates[RegalPillowRelicId] = agg;

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("total_healing_attempted", json);
        Assert.Contains("total_healing_restored", json);
        Assert.Contains("total_healing_lost", json);
        Assert.Contains("healing_lost_reasons", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[RegalPillowRelicId];
        Assert.Equal(2, restoredAgg.Activations);
        Assert.Equal(12m, restoredAgg.TotalHealingAttempted);
        Assert.Equal(9m, restoredAgg.TotalHealingRestored);
        Assert.Equal(3m, restoredAgg.TotalHealingLost);
        Assert.Equal(3m, restoredAgg.HealingLostReasons["full_hp"].Amount);
    }

    [Fact]
    public void RunTracker_RegalPillowRestHeal_RecordsOnlyEffectiveBonusHealing()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordRegalPillowRestHealForTest(
            agg,
            incomingHealAmount: 10m,
            attemptedBonusHealing: 6m,
            initialCurrentHp: 35m,
            initialMissingHp: 15m,
            finalCurrentHp: 50m);

        Assert.Equal(1, agg.Activations);
        Assert.Equal(6m, agg.TotalHealingAttempted);
        Assert.Equal(5m, agg.TotalHealingRestored);
        Assert.Equal(1m, agg.TotalHealingLost);
        Assert.Equal(1m, agg.HealingLostReasons["full_hp"].Amount);
    }

    [Fact]
    public void RelicTooltip_RegalPillow_ShowsActivationsAndHealing()
    {
        var agg = new RelicAggregate
        {
            Activations = 2,
            TotalHealingRestored = 9m,
            TotalHealingLost = 3m,
        };
        agg.HealingLostReasons["full_hp"] = new HealingLostReasonAggregate
        {
            ReasonId = "full_hp",
            DisplayName = "full HP",
            Amount = 3m,
        };

        var body = BuildBody(agg);

        Assert.Contains("Activations", body);
        Assert.Contains("HP healed", body);
        Assert.Contains("healing lost", body);
        Assert.Contains("lost to full HP", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]9[/b]", body);
        Assert.Contains("[b]3[/b]", body);
    }

    [Fact]
    public void RelicTooltip_RegalPillow_ShowsZeroHealingRows()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Activations", body);
        Assert.Contains("HP healed", body);
        Assert.Contains("healing lost", body);
        Assert.Contains("[b]0[/b]", body);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildRegalPillowBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildRegalPillowBodyBBCode returned null."));
}
