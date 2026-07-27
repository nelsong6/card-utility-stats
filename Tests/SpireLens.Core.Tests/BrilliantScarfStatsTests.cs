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
    private const string EnergyIcon =
        "[img=16x16]"
        + "res://images/packed/sprite_fonts/ironclad_energy_icon.png[/img]";
    private const string StarIcon =
        "[img width=16 height=16 align=center]"
        + "res://images/packed/sprite_fonts/star_icon.png[/img]";

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

        Assert.Equal(0, agg.DiscountCombats);
        Assert.Equal(0, agg.DiscountTurns);
        Assert.Equal(0, agg.DiscountsOffered);
        Assert.Equal(0, agg.DiscountsTaken);
        Assert.Equal(0, agg.EnergySavedByDiscount);
        Assert.Equal(0, agg.BrilliantScarfEnergySavedForTurnAverage);
        Assert.Empty(agg.DiscountedCardCosts);
    }

    [Fact]
    public void RelicAggregate_BrilliantScarfFields_JsonRoundtrip_PreservesFields()
    {
        var energyTwoKey = RunTracker.BrilliantScarfDiscountCostKeyForTest(2, 0);
        var mixedKey = RunTracker.BrilliantScarfDiscountCostKeyForTest(1, 2);
        var run = new RunData();
        run.RelicAggregates[BrilliantScarfRelicId] = new RelicAggregate
        {
            DiscountCombats = 2,
            DiscountTurns = 6,
            DiscountsOffered = 5,
            DiscountsTaken = 3,
            EnergySavedByDiscount = 7,
            BrilliantScarfEnergySavedForTurnAverage = 7,
            DiscountedCardCosts =
            {
                [energyTwoKey] = new DiscountedCardCostAggregate
                {
                    EnergyCost = 2,
                    StarCost = 0,
                    Count = 2,
                },
                [mixedKey] = new DiscountedCardCostAggregate
                {
                    EnergyCost = 1,
                    StarCost = 2,
                    Count = 1,
                },
            },
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("discount_combats", json);
        Assert.Contains("discount_turns", json);
        Assert.Contains("discounts_offered", json);
        Assert.Contains("discounts_taken", json);
        Assert.Contains("energy_saved_by_discount", json);
        Assert.Contains("brilliant_scarf_energy_saved_for_turn_average", json);
        Assert.Contains("discounted_card_costs", json);
        Assert.Contains("energy_cost", json);
        Assert.Contains("star_cost", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[BrilliantScarfRelicId];
        Assert.Equal(2, restoredAgg.DiscountCombats);
        Assert.Equal(6, restoredAgg.DiscountTurns);
        Assert.Equal(5, restoredAgg.DiscountsOffered);
        Assert.Equal(3, restoredAgg.DiscountsTaken);
        Assert.Equal(7, restoredAgg.EnergySavedByDiscount);
        Assert.Equal(7, restoredAgg.BrilliantScarfEnergySavedForTurnAverage);
        Assert.Equal(2, restoredAgg.DiscountedCardCosts[energyTwoKey].EnergyCost);
        Assert.Equal(0, restoredAgg.DiscountedCardCosts[energyTwoKey].StarCost);
        Assert.Equal(2, restoredAgg.DiscountedCardCosts[energyTwoKey].Count);
        Assert.Equal(1, restoredAgg.DiscountedCardCosts[mixedKey].EnergyCost);
        Assert.Equal(2, restoredAgg.DiscountedCardCosts[mixedKey].StarCost);
        Assert.Equal(1, restoredAgg.DiscountedCardCosts[mixedKey].Count);
    }

    [Fact]
    public void RunTracker_BrilliantScarfTestHelper_AccumulatesAndClamps()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordBrilliantScarfDiscountForTest(
            agg,
            offers: 5,
            taken: 3,
            energySaved: 7,
            combats: 2,
            turns: 6);
        RunTracker.RecordBrilliantScarfDiscountForTest(
            agg,
            offers: -1,
            taken: -2,
            energySaved: -3,
            combats: -4,
            turns: -5);

        Assert.Equal(2, agg.DiscountCombats);
        Assert.Equal(6, agg.DiscountTurns);
        Assert.Equal(5, agg.DiscountsOffered);
        Assert.Equal(3, agg.DiscountsTaken);
        Assert.Equal(7, agg.EnergySavedByDiscount);
        Assert.Equal(7, agg.BrilliantScarfEnergySavedForTurnAverage);
    }

    [Fact]
    public void RelicAggregate_BrilliantScarfTurnDenominator_Merges()
    {
        var target = new RelicAggregate
        {
            DiscountTurns = 2,
            BrilliantScarfEnergySavedForTurnAverage = 2,
        };
        var source = new RelicAggregate
        {
            DiscountTurns = 4,
            BrilliantScarfEnergySavedForTurnAverage = 5,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(6, target.DiscountTurns);
        Assert.Equal(7, target.BrilliantScarfEnergySavedForTurnAverage);
    }

    [Fact]
    public void RunTracker_BrilliantScarfDiscountCosts_AccumulatesAndClamps()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordBrilliantScarfDiscountCostForTest(agg, energyCost: 2, starCost: 0);
        RunTracker.RecordBrilliantScarfDiscountCostForTest(agg, energyCost: 2, starCost: 0, count: 3);
        RunTracker.RecordBrilliantScarfDiscountCostForTest(agg, energyCost: 1, starCost: 2);
        RunTracker.RecordBrilliantScarfDiscountCostForTest(agg, energyCost: -1, starCost: -2);
        RunTracker.RecordBrilliantScarfDiscountCostForTest(agg, energyCost: 4, starCost: 0, count: -1);

        var energyTwo = agg.DiscountedCardCosts[RunTracker.BrilliantScarfDiscountCostKeyForTest(2, 0)];
        var mixed = agg.DiscountedCardCosts[RunTracker.BrilliantScarfDiscountCostKeyForTest(1, 2)];
        var zero = agg.DiscountedCardCosts[RunTracker.BrilliantScarfDiscountCostKeyForTest(0, 0)];

        Assert.Equal(2, energyTwo.EnergyCost);
        Assert.Equal(0, energyTwo.StarCost);
        Assert.Equal(4, energyTwo.Count);
        Assert.Equal(1, mixed.EnergyCost);
        Assert.Equal(2, mixed.StarCost);
        Assert.Equal(1, mixed.Count);
        Assert.Equal(0, zero.EnergyCost);
        Assert.Equal(0, zero.StarCost);
        Assert.Equal(1, zero.Count);
        Assert.False(agg.DiscountedCardCosts.ContainsKey(RunTracker.BrilliantScarfDiscountCostKeyForTest(4, 0)));
    }

    [Fact]
    public void RelicTooltip_BrilliantScarf_ShowsDiscountRows()
    {
        var agg = new RelicAggregate
        {
            DiscountCombats = 2,
            DiscountTurns = 4,
            DiscountsOffered = 5,
            DiscountsTaken = 3,
            EnergySavedByDiscount = 7,
            BrilliantScarfEnergySavedForTurnAverage = 7,
        };

        var body = (string)(BuildBrilliantScarfBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildBrilliantScarfBodyBBCode returned null."));

        Assert.Contains("Combats held", body);
        Assert.Contains("Discounts offered", body);
        Assert.Contains("Discounts taken", body);
        Assert.Contains("Energy saved", body);
        Assert.Contains("saved / turn", body);
        Assert.Contains("saved / combat", body);
        Assert.Contains("saved / use", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]5[/b]", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[b]7[/b]", body);
        Assert.Contains("[b]1.75[/b]", body);
        Assert.Contains("[b]3.5[/b]", body);
        Assert.Contains("[b]2.33[/b]", body);
    }

    [Fact]
    public void RelicTooltip_BrilliantScarf_ShowsZeroAveragesWhenNoDenominators()
    {
        var body = (string)(BuildBrilliantScarfBodyMethod.Invoke(null, new object?[] { new RelicAggregate() })
            ?? throw new InvalidOperationException("BuildBrilliantScarfBodyBBCode returned null."));

        Assert.Contains("Combats held", body);
        Assert.Contains("saved / turn", body);
        Assert.Contains("saved / combat", body);
        Assert.Contains("saved / use", body);
        Assert.Contains("[b]0[/b]", body);
    }

    [Fact]
    public void RelicTooltip_BrilliantScarf_ShowsZeroEnergyCostBucketsAndDynamicMixedCosts()
    {
        var agg = new RelicAggregate();
        RunTracker.RecordBrilliantScarfDiscountCostForTest(agg, energyCost: 2, starCost: 0, count: 2);
        RunTracker.RecordBrilliantScarfDiscountCostForTest(agg, energyCost: 4, starCost: 0);
        RunTracker.RecordBrilliantScarfDiscountCostForTest(agg, energyCost: 1, starCost: 2);

        var body = (string)(BuildBrilliantScarfBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildBrilliantScarfBodyBBCode returned null."));

        AssertContainsRow(body, $"0 {EnergyIcon} cost reduced", "0");
        AssertContainsRow(body, $"1 {EnergyIcon} cost reduced", "0");
        AssertContainsRow(body, $"2 {EnergyIcon} cost reduced", "2");
        AssertContainsRow(body, $"3 {EnergyIcon} cost reduced", "0");
        AssertContainsRow(body, $"4 {EnergyIcon} cost reduced", "1");
        AssertContainsRow(body, $"1 {EnergyIcon} 2 {StarIcon} cost reduced", "1");
    }

    private static void AssertContainsRow(string body, string label, string value)
    {
        Assert.Contains(label, body);
        Assert.Contains($"[color=#e0e0e0]{label}[/color][/cell][cell expand=0 padding=0,0,12,0][right][b]{value}[/b][/right]", body);
    }
}
