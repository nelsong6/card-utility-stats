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
        Assert.Null(agg.OriginalMaxHp);
        Assert.Null(agg.NewMaxHp);
    }

    [Fact]
    public void RelicAggregate_LeesWaffleFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[LeesWaffleRelicId] = new RelicAggregate
        {
            Activations = 1,
            TotalHealingRestored = 23m,
            OriginalMaxHp = 70m,
            NewMaxHp = 77m,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("total_healing_restored", json);
        Assert.Contains("original_max_hp", json);
        Assert.Contains("new_max_hp", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[LeesWaffleRelicId];
        Assert.Equal(1, restoredAgg.Activations);
        Assert.Equal(23m, restoredAgg.TotalHealingRestored);
        Assert.Equal(70m, restoredAgg.OriginalMaxHp);
        Assert.Equal(77m, restoredAgg.NewMaxHp);
    }

    [Fact]
    public void RunTracker_RecordLeesWafflePickupHpGained_AccumulatesObservedHpGain()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordLeesWafflePickupHpGainedForTest(agg, 23m, 70m, 77m);
        RunTracker.RecordLeesWafflePickupHpGainedForTest(agg, 7m, 77m, 84m);
        RunTracker.RecordLeesWafflePickupHpGainedForTest(agg, -5m);

        Assert.Equal(2, agg.Activations);
        Assert.Equal(30m, agg.TotalHealingRestored);
        Assert.Equal(70m, agg.OriginalMaxHp);
        Assert.Equal(84m, agg.NewMaxHp);
    }

    [Fact]
    public void RelicTooltip_LeesWaffle_ShowsHpGained()
    {
        var agg = new RelicAggregate
        {
            Activations = 1,
            TotalHealingRestored = 23m,
            OriginalMaxHp = 70m,
            NewMaxHp = 77m,
        };

        var body = (string)(BuildLeesWaffleBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildLeesWaffleBodyBBCode returned null."));

        Assert.Contains("Original max HP", body);
        Assert.Contains("New max HP", body);
        Assert.Contains("Max HP gained", body);
        Assert.Contains("HP gained", body);
        Assert.Contains("[b]23[/b]", body);
        Assert.Contains("[b]70[/b]", body);
        Assert.Contains("[b]77[/b]", body);
        Assert.Contains("[b]7[/b]", body);
        Assert.DoesNotContain("HP healed", body);
    }
}
