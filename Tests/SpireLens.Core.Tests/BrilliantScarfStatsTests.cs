using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class BrilliantScarfStatsTests
{
    private const string BrilliantScarfRelicId = "RELIC.BRILLIANT_SCARF";

    private static readonly MethodInfo BuildBrilliantScarfBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildBrilliantScarfBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildBrilliantScarfBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_BrilliantScarfFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.DiscountsOffered);
        Assert.Equal(0, agg.DiscountsTaken);
        Assert.Equal(0, agg.EnergySavedByDiscount);
    }

    [Fact]
    public void RelicAggregate_BrilliantScarfFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[BrilliantScarfRelicId] = new RelicAggregate
        {
            DiscountsOffered = 5,
            DiscountsTaken = 3,
            EnergySavedByDiscount = 7,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("discounts_offered", json);
        Assert.Contains("discounts_taken", json);
        Assert.Contains("energy_saved_by_discount", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[BrilliantScarfRelicId];
        Assert.Equal(5, restoredAgg.DiscountsOffered);
        Assert.Equal(3, restoredAgg.DiscountsTaken);
        Assert.Equal(7, restoredAgg.EnergySavedByDiscount);
    }

    [Fact]
    public void RunTracker_BrilliantScarfTestHelper_AccumulatesAndClamps()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordBrilliantScarfDiscountForTest(agg, offers: 5, taken: 3, energySaved: 7);
        RunTracker.RecordBrilliantScarfDiscountForTest(agg, offers: -1, taken: -2, energySaved: -3);

        Assert.Equal(5, agg.DiscountsOffered);
        Assert.Equal(3, agg.DiscountsTaken);
        Assert.Equal(7, agg.EnergySavedByDiscount);
    }

    [Fact]
    public void RelicTooltip_BrilliantScarf_ShowsDiscountRows()
    {
        var agg = new RelicAggregate
        {
            DiscountsOffered = 5,
            DiscountsTaken = 3,
            EnergySavedByDiscount = 7,
        };

        var body = (string)(BuildBrilliantScarfBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildBrilliantScarfBodyBBCode returned null."));

        Assert.Contains("Discounts offered", body);
        Assert.Contains("Discounts taken", body);
        Assert.Contains("Energy saved", body);
        Assert.Contains("[b]5[/b]", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[b]7[/b]", body);
    }
}
