using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Models.Cards;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class AlchemizeStatsTests
{
    private const string AlchemizeCardId = "CARD.ALCHEMIZE";

    private static readonly MethodInfo AppendAlchemizePotionStatsMethod =
        typeof(CardHoverShowPatch).GetMethod(
            "AppendAlchemizePotionStats",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("AppendAlchemizePotionStats not found.");

    [Fact]
    public void CardAggregate_AlchemizePotionFields_DefaultToZero()
    {
        var agg = new CardAggregate();

        Assert.Equal(0, agg.PotionsGained);
        Assert.Equal(0, agg.CommonPotionsGained);
        Assert.Equal(0, agg.UncommonPotionsGained);
        Assert.Equal(0, agg.RarePotionsGained);
        Assert.Equal(0, agg.PotionsSkipped);
    }

    [Fact]
    public void CardAggregate_AlchemizePotionFields_JsonRoundtripPreservesFields()
    {
        var run = new RunData();
        run.Aggregates[$"{AlchemizeCardId}#1"] = new CardAggregate
        {
            PotionsGained = 5,
            CommonPotionsGained = 2,
            UncommonPotionsGained = 2,
            RarePotionsGained = 1,
            PotionsSkipped = 3,
        };

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"potions_gained\"", json);
        Assert.Contains("\"common_potions_gained\"", json);
        Assert.Contains("\"uncommon_potions_gained\"", json);
        Assert.Contains("\"rare_potions_gained\"", json);
        Assert.Contains("\"potions_skipped\"", json);
        Assert.NotNull(restored);

        var agg = restored!.Aggregates[$"{AlchemizeCardId}#1"];
        Assert.Equal(5, agg.PotionsGained);
        Assert.Equal(2, agg.CommonPotionsGained);
        Assert.Equal(2, agg.UncommonPotionsGained);
        Assert.Equal(1, agg.RarePotionsGained);
        Assert.Equal(3, agg.PotionsSkipped);
    }

    [Fact]
    public void RunTracker_AlchemizePotionResults_CountSuccessRarityAndFailures()
    {
        var agg = new CardAggregate();

        RunTracker.RecordAlchemizePotionResultForTest(agg, success: true, PotionRarity.Common);
        RunTracker.RecordAlchemizePotionResultForTest(agg, success: true, PotionRarity.Common);
        RunTracker.RecordAlchemizePotionResultForTest(agg, success: true, PotionRarity.Uncommon);
        RunTracker.RecordAlchemizePotionResultForTest(agg, success: true, PotionRarity.Rare);
        RunTracker.RecordAlchemizePotionResultForTest(agg, success: false, rarity: null);
        RunTracker.RecordAlchemizePotionResultForTest(agg, success: false, PotionRarity.Rare);

        Assert.Equal(4, agg.PotionsGained);
        Assert.Equal(2, agg.CommonPotionsGained);
        Assert.Equal(1, agg.UncommonPotionsGained);
        Assert.Equal(1, agg.RarePotionsGained);
        Assert.Equal(2, agg.PotionsSkipped);
    }

    [Fact]
    public void CardAggregatePooler_AlchemizePotionFields_MergeAcrossInstances()
    {
        var pooled = CardAggregatePooler.PoolByDefinition(
            new Dictionary<string, CardAggregate>
            {
                [$"{AlchemizeCardId}#1"] = new()
                {
                    PotionsGained = 2,
                    CommonPotionsGained = 1,
                    UncommonPotionsGained = 1,
                    PotionsSkipped = 1,
                },
                [$"{AlchemizeCardId}#2"] = new()
                {
                    PotionsGained = 3,
                    CommonPotionsGained = 1,
                    UncommonPotionsGained = 1,
                    RarePotionsGained = 1,
                    PotionsSkipped = 2,
                },
                ["CARD.NEUTRALIZE#1"] = new()
                {
                    PotionsGained = 99,
                    PotionsSkipped = 99,
                },
            },
            AlchemizeCardId);

        Assert.NotNull(pooled);
        Assert.Equal(5, pooled!.PotionsGained);
        Assert.Equal(2, pooled.CommonPotionsGained);
        Assert.Equal(2, pooled.UncommonPotionsGained);
        Assert.Equal(1, pooled.RarePotionsGained);
        Assert.Equal(3, pooled.PotionsSkipped);
    }

    [Fact]
    public void AlchemizeTooltip_FullViewMatchesWhiteBeastPotionRows()
    {
        var sb = new StringBuilder();
        var agg = new CardAggregate
        {
            PotionsGained = 5,
            CommonPotionsGained = 2,
            UncommonPotionsGained = 2,
            RarePotionsGained = 1,
            PotionsSkipped = 3,
        };

        AppendAlchemizePotionStats(sb, agg, compact: false);
        var body = sb.ToString();

        Assert.Contains("Potions gained", body);
        Assert.Contains("Potions skipped", body);
        Assert.Contains("common potions", body);
        Assert.Contains("uncommon potions", body);
        Assert.Contains("rare potions", body);
        Assert.Contains("[b]5[/b]", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]1[/b]", body);
    }

    [Fact]
    public void AlchemizeTooltip_CompactViewKeepsOnlyPotionTotals()
    {
        var sb = new StringBuilder();
        var agg = new CardAggregate
        {
            PotionsGained = 2,
            CommonPotionsGained = 1,
            RarePotionsGained = 1,
            PotionsSkipped = 1,
        };

        AppendAlchemizePotionStats(sb, agg, compact: true);
        var body = sb.ToString();

        Assert.Contains("Potions gained", body);
        Assert.Contains("Potions skipped", body);
        Assert.DoesNotContain("common potions", body);
        Assert.DoesNotContain("uncommon potions", body);
        Assert.DoesNotContain("rare potions", body);
    }

    [Fact]
    public void CardAggregate_OlderShapeWithoutAlchemizePotionFields_DefaultsToZero()
    {
        var agg = JsonSerializer.Deserialize<CardAggregate>("{}", RunStorage.Options);

        Assert.NotNull(agg);
        Assert.Equal(0, agg!.PotionsGained);
        Assert.Equal(0, agg.CommonPotionsGained);
        Assert.Equal(0, agg.UncommonPotionsGained);
        Assert.Equal(0, agg.RarePotionsGained);
        Assert.Equal(0, agg.PotionsSkipped);
    }

    private static void AppendAlchemizePotionStats(
        StringBuilder sb,
        CardAggregate agg,
        bool compact)
    {
        var card = (Alchemize)RuntimeHelpers.GetUninitializedObject(typeof(Alchemize));
        _ = AppendAlchemizePotionStatsMethod.Invoke(null, new object?[] { sb, card, agg, compact });
    }
}
