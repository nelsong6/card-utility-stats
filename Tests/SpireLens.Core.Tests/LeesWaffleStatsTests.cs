using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class LeesWaffleStatsTests
{
    private const string LeesWaffleRelicId = "RELIC.LEES_WAFFLE";

    private static readonly MethodInfo BuildLeesWaffleBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildLeesWaffleBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildLeesWaffleBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_LeesWaffleFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.TotalHealingRestored);
    }

    [Fact]
    public void RelicAggregate_LeesWaffleFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[LeesWaffleRelicId] = new RelicAggregate
        {
            Activations = 1,
            TotalHealingRestored = 23m,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("total_healing_restored", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[LeesWaffleRelicId];
        Assert.Equal(1, restoredAgg.Activations);
        Assert.Equal(23m, restoredAgg.TotalHealingRestored);
    }

    [Fact]
    public void RunTracker_RecordLeesWafflePickupHpGained_AccumulatesObservedHpGain()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordLeesWafflePickupHpGainedForTest(agg, 23m);
        RunTracker.RecordLeesWafflePickupHpGainedForTest(agg, 7m);
        RunTracker.RecordLeesWafflePickupHpGainedForTest(agg, -5m);

        Assert.Equal(2, agg.Activations);
        Assert.Equal(30m, agg.TotalHealingRestored);
    }

    [Fact]
    public void RelicTooltip_LeesWaffle_ShowsHpGained()
    {
        var agg = new RelicAggregate
        {
            Activations = 1,
            TotalHealingRestored = 23m,
        };

        var body = (string)(BuildLeesWaffleBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildLeesWaffleBodyBBCode returned null."));

        Assert.Contains("HP gained", body);
        Assert.Contains("[b]23[/b]", body);
        Assert.DoesNotContain("HP healed", body);
    }
}
