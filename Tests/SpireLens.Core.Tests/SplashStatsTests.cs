using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Cards;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class SplashStatsTests
{
    private const string SplashCardId = "CARD.SPLASH";

    private static readonly MethodInfo AppendSplashStatsMethod =
        typeof(CardHoverShowPatch).GetMethod(
            "AppendSplashStats",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("AppendSplashStats not found.");

    [Fact]
    public void CardAggregate_SplashFields_DefaultToZero()
    {
        var agg = new CardAggregate();

        Assert.Equal(0, agg.SplashAttacksTaken);
        Assert.Equal(0, agg.SplashCommonAttacksTaken);
        Assert.Equal(0, agg.SplashUncommonAttacksTaken);
        Assert.Equal(0, agg.SplashRareAttacksTaken);
        Assert.Equal(0, agg.SplashEnergyDiscountTotal);
    }

    [Fact]
    public void CardAggregate_SplashFields_JsonRoundtripPreservesFields()
    {
        var run = new RunData();
        run.Aggregates[$"{SplashCardId}#1"] = CreateRepresentativeAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"splash_attacks_taken\"", json);
        Assert.Contains("\"splash_common_attacks_taken\"", json);
        Assert.Contains("\"splash_uncommon_attacks_taken\"", json);
        Assert.Contains("\"splash_rare_attacks_taken\"", json);
        Assert.Contains("\"splash_energy_discount_total\"", json);
        Assert.NotNull(restored);

        AssertRepresentativeAggregate(restored!.Aggregates[$"{SplashCardId}#1"]);
    }

    [Fact]
    public void RunTracker_SplashTakes_CountRarityAndNonnegativeDiscount()
    {
        var agg = new CardAggregate();

        RunTracker.RecordSplashAttackTakenForTest(
            agg, CardRarity.Common, costBefore: 1, costAfter: 0);
        RunTracker.RecordSplashAttackTakenForTest(
            agg, CardRarity.Common, costBefore: 0, costAfter: 0);
        RunTracker.RecordSplashAttackTakenForTest(
            agg, CardRarity.Uncommon, costBefore: 3, costAfter: 0);
        RunTracker.RecordSplashAttackTakenForTest(
            agg, CardRarity.Uncommon, costBefore: 3, costAfter: 1);
        RunTracker.RecordSplashAttackTakenForTest(
            agg, CardRarity.Rare, costBefore: 1, costAfter: 0);
        RunTracker.RecordSplashAttackTakenForTest(
            agg, CardRarity.Rare, costBefore: 0, costAfter: 1);

        AssertRepresentativeAggregate(agg);
    }

    [Fact]
    public void CardAggregatePooler_SplashFields_MergeAcrossInstances()
    {
        var pooled = CardAggregatePooler.PoolByDefinition(
            new Dictionary<string, CardAggregate>
            {
                [$"{SplashCardId}#1"] = new()
                {
                    SplashAttacksTaken = 2,
                    SplashCommonAttacksTaken = 1,
                    SplashUncommonAttacksTaken = 1,
                    SplashEnergyDiscountTotal = 3,
                },
                [$"{SplashCardId}#2"] = new()
                {
                    SplashAttacksTaken = 4,
                    SplashCommonAttacksTaken = 1,
                    SplashUncommonAttacksTaken = 1,
                    SplashRareAttacksTaken = 2,
                    SplashEnergyDiscountTotal = 4,
                },
                ["CARD.NEUTRALIZE#1"] = new()
                {
                    SplashAttacksTaken = 99,
                    SplashEnergyDiscountTotal = 99,
                },
            },
            SplashCardId);

        Assert.NotNull(pooled);
        AssertRepresentativeAggregate(pooled!);
    }

    [Fact]
    public void SplashTooltip_ShowsRequestedRaritiesAndAverageDiscount()
    {
        var sb = new StringBuilder();

        AppendSplashStats(sb, CreateRepresentativeAggregate());
        var body = sb.ToString();

        Assert.Contains("Commons taken", body);
        Assert.Contains("Uncommons taken", body);
        Assert.Contains("Rares taken", body);
        Assert.Contains("avg discount", body);
        Assert.Contains("[b]1.2[/b]", body);
    }

    [Fact]
    public void CardAggregate_OlderShapeWithoutSplashFields_DefaultsToZero()
    {
        var agg = JsonSerializer.Deserialize<CardAggregate>("{}", RunStorage.Options);

        Assert.NotNull(agg);
        Assert.Equal(0, agg!.SplashAttacksTaken);
        Assert.Equal(0, agg.SplashCommonAttacksTaken);
        Assert.Equal(0, agg.SplashUncommonAttacksTaken);
        Assert.Equal(0, agg.SplashRareAttacksTaken);
        Assert.Equal(0, agg.SplashEnergyDiscountTotal);
    }

    private static CardAggregate CreateRepresentativeAggregate() =>
        new()
        {
            SplashAttacksTaken = 6,
            SplashCommonAttacksTaken = 2,
            SplashUncommonAttacksTaken = 2,
            SplashRareAttacksTaken = 2,
            SplashEnergyDiscountTotal = 7,
        };

    private static void AssertRepresentativeAggregate(CardAggregate agg)
    {
        Assert.Equal(6, agg.SplashAttacksTaken);
        Assert.Equal(2, agg.SplashCommonAttacksTaken);
        Assert.Equal(2, agg.SplashUncommonAttacksTaken);
        Assert.Equal(2, agg.SplashRareAttacksTaken);
        Assert.Equal(7, agg.SplashEnergyDiscountTotal);
    }

    private static void AppendSplashStats(StringBuilder sb, CardAggregate agg)
    {
        var card = (Splash)RuntimeHelpers.GetUninitializedObject(typeof(Splash));
        _ = AppendSplashStatsMethod.Invoke(null, new object?[] { sb, card, agg });
    }
}
