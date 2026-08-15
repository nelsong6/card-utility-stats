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

public class DiscoveryStatsTests
{
    private const string DiscoveryCardId = "CARD.DISCOVERY";

    private static readonly MethodInfo AppendDiscoveryStatsMethod =
        typeof(CardHoverShowPatch).GetMethod(
            "AppendDiscoveryStats",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("AppendDiscoveryStats not found.");

    [Fact]
    public void CardAggregate_DiscoveryFields_DefaultToZero()
    {
        var agg = new CardAggregate();

        Assert.Equal(0, agg.DiscoveryCardsOffered);
        Assert.Equal(0, agg.DiscoveryCommonCardsOffered);
        Assert.Equal(0, agg.DiscoveryUncommonCardsOffered);
        Assert.Equal(0, agg.DiscoveryRareCardsOffered);
        Assert.Equal(0, agg.DiscoveryAttacksOffered);
        Assert.Equal(0, agg.DiscoverySkillsOffered);
        Assert.Equal(0, agg.DiscoveryPowersOffered);
        Assert.Equal(0, agg.DiscoveryCardsPicked);
        Assert.Equal(0, agg.DiscoveryCommonCardsPicked);
        Assert.Equal(0, agg.DiscoveryUncommonCardsPicked);
        Assert.Equal(0, agg.DiscoveryRareCardsPicked);
        Assert.Equal(0, agg.DiscoveryAttacksPicked);
        Assert.Equal(0, agg.DiscoverySkillsPicked);
        Assert.Equal(0, agg.DiscoveryPowersPicked);
        Assert.Equal(0, agg.DiscoveryEnergyDiscountTotal);
    }

    [Fact]
    public void CardAggregate_DiscoveryFields_JsonRoundtripPreservesFields()
    {
        var run = new RunData();
        run.Aggregates[$"{DiscoveryCardId}#1"] = CreateRepresentativeAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"discovery_cards_offered\"", json);
        Assert.Contains("\"discovery_common_cards_offered\"", json);
        Assert.Contains("\"discovery_uncommon_cards_offered\"", json);
        Assert.Contains("\"discovery_rare_cards_offered\"", json);
        Assert.Contains("\"discovery_attacks_offered\"", json);
        Assert.Contains("\"discovery_skills_offered\"", json);
        Assert.Contains("\"discovery_powers_offered\"", json);
        Assert.Contains("\"discovery_cards_picked\"", json);
        Assert.Contains("\"discovery_common_cards_picked\"", json);
        Assert.Contains("\"discovery_uncommon_cards_picked\"", json);
        Assert.Contains("\"discovery_rare_cards_picked\"", json);
        Assert.Contains("\"discovery_attacks_picked\"", json);
        Assert.Contains("\"discovery_skills_picked\"", json);
        Assert.Contains("\"discovery_powers_picked\"", json);
        Assert.Contains("\"discovery_energy_discount_total\"", json);
        Assert.NotNull(restored);

        AssertRepresentativeAggregate(restored!.Aggregates[$"{DiscoveryCardId}#1"]);
    }

    [Fact]
    public void RunTracker_DiscoveryOffers_CountEveryOptionByRarityAndType()
    {
        var agg = new CardAggregate();

        RunTracker.RecordDiscoveryCardOfferedForTest(
            agg, CardRarity.Common, CardType.Attack);
        RunTracker.RecordDiscoveryCardOfferedForTest(
            agg, CardRarity.Common, CardType.Skill);
        RunTracker.RecordDiscoveryCardOfferedForTest(
            agg, CardRarity.Uncommon, CardType.Power);
        RunTracker.RecordDiscoveryCardOfferedForTest(
            agg, CardRarity.Rare, CardType.Attack);

        Assert.Equal(4, agg.DiscoveryCardsOffered);
        Assert.Equal(2, agg.DiscoveryCommonCardsOffered);
        Assert.Equal(1, agg.DiscoveryUncommonCardsOffered);
        Assert.Equal(1, agg.DiscoveryRareCardsOffered);
        Assert.Equal(2, agg.DiscoveryAttacksOffered);
        Assert.Equal(1, agg.DiscoverySkillsOffered);
        Assert.Equal(1, agg.DiscoveryPowersOffered);
        Assert.Equal(0, agg.DiscoveryCardsPicked);
    }

    [Fact]
    public void RunTracker_DiscoveryPicks_CountRarityTypeAndNonnegativeDiscount()
    {
        var agg = new CardAggregate();

        RunTracker.RecordDiscoveryCardPickedForTest(
            agg, CardRarity.Common, CardType.Attack, costBefore: 1, costAfter: 0);
        RunTracker.RecordDiscoveryCardPickedForTest(
            agg, CardRarity.Common, CardType.Skill, costBefore: 0, costAfter: 0);
        RunTracker.RecordDiscoveryCardPickedForTest(
            agg, CardRarity.Uncommon, CardType.Power, costBefore: 3, costAfter: 0);
        RunTracker.RecordDiscoveryCardPickedForTest(
            agg, CardRarity.Uncommon, CardType.Skill, costBefore: 3, costAfter: 1);
        RunTracker.RecordDiscoveryCardPickedForTest(
            agg, CardRarity.Rare, CardType.Attack, costBefore: 1, costAfter: 0);
        RunTracker.RecordDiscoveryCardPickedForTest(
            agg, CardRarity.Rare, CardType.Skill, costBefore: 0, costAfter: 1);

        Assert.Equal(6, agg.DiscoveryCardsPicked);
        Assert.Equal(2, agg.DiscoveryCommonCardsPicked);
        Assert.Equal(2, agg.DiscoveryUncommonCardsPicked);
        Assert.Equal(2, agg.DiscoveryRareCardsPicked);
        Assert.Equal(2, agg.DiscoveryAttacksPicked);
        Assert.Equal(3, agg.DiscoverySkillsPicked);
        Assert.Equal(1, agg.DiscoveryPowersPicked);
        Assert.Equal(7, agg.DiscoveryEnergyDiscountTotal);
    }

    [Fact]
    public void CardAggregatePooler_DiscoveryFields_MergeAcrossInstances()
    {
        var pooled = CardAggregatePooler.PoolByDefinition(
            new Dictionary<string, CardAggregate>
            {
                [$"{DiscoveryCardId}#1"] = new()
                {
                    DiscoveryCardsOffered = 6,
                    DiscoveryCommonCardsOffered = 3,
                    DiscoveryUncommonCardsOffered = 2,
                    DiscoveryRareCardsOffered = 1,
                    DiscoveryAttacksOffered = 3,
                    DiscoverySkillsOffered = 2,
                    DiscoveryPowersOffered = 1,
                    DiscoveryCardsPicked = 2,
                    DiscoveryCommonCardsPicked = 1,
                    DiscoveryUncommonCardsPicked = 1,
                    DiscoveryAttacksPicked = 1,
                    DiscoverySkillsPicked = 1,
                    DiscoveryEnergyDiscountTotal = 3,
                },
                [$"{DiscoveryCardId}#2"] = new()
                {
                    DiscoveryCardsOffered = 9,
                    DiscoveryCommonCardsOffered = 5,
                    DiscoveryUncommonCardsOffered = 3,
                    DiscoveryRareCardsOffered = 1,
                    DiscoveryAttacksOffered = 3,
                    DiscoverySkillsOffered = 5,
                    DiscoveryPowersOffered = 1,
                    DiscoveryCardsPicked = 3,
                    DiscoveryCommonCardsPicked = 1,
                    DiscoveryUncommonCardsPicked = 1,
                    DiscoveryRareCardsPicked = 1,
                    DiscoveryAttacksPicked = 1,
                    DiscoverySkillsPicked = 1,
                    DiscoveryPowersPicked = 1,
                    DiscoveryEnergyDiscountTotal = 4,
                },
                ["CARD.NEUTRALIZE#1"] = new()
                {
                    DiscoveryCardsOffered = 99,
                    DiscoveryCardsPicked = 99,
                    DiscoveryEnergyDiscountTotal = 99,
                },
            },
            DiscoveryCardId);

        Assert.NotNull(pooled);
        Assert.Equal(15, pooled!.DiscoveryCardsOffered);
        Assert.Equal(8, pooled.DiscoveryCommonCardsOffered);
        Assert.Equal(5, pooled.DiscoveryUncommonCardsOffered);
        Assert.Equal(2, pooled.DiscoveryRareCardsOffered);
        Assert.Equal(6, pooled.DiscoveryAttacksOffered);
        Assert.Equal(7, pooled.DiscoverySkillsOffered);
        Assert.Equal(2, pooled.DiscoveryPowersOffered);
        Assert.Equal(5, pooled.DiscoveryCardsPicked);
        Assert.Equal(2, pooled.DiscoveryCommonCardsPicked);
        Assert.Equal(2, pooled.DiscoveryUncommonCardsPicked);
        Assert.Equal(1, pooled.DiscoveryRareCardsPicked);
        Assert.Equal(2, pooled.DiscoveryAttacksPicked);
        Assert.Equal(2, pooled.DiscoverySkillsPicked);
        Assert.Equal(1, pooled.DiscoveryPowersPicked);
        Assert.Equal(7, pooled.DiscoveryEnergyDiscountTotal);
    }

    [Fact]
    public void DiscoveryTooltip_FullViewPairsEveryOfferBucketWithItsPicks()
    {
        var sb = new StringBuilder();

        AppendDiscoveryStats(sb, CreateRepresentativeAggregate(), compact: false);
        var body = sb.ToString();

        Assert.Contains("Cards offered/picked", body);
        Assert.Contains("Commons offered/picked", body);
        Assert.Contains("Uncommons offered/picked", body);
        Assert.Contains("Rares offered/picked", body);
        Assert.Contains("Attacks offered/picked", body);
        Assert.Contains("Skills offered/picked", body);
        Assert.Contains("Powers offered/picked", body);
        Assert.Contains("[b]15/5[/b]", body);
        Assert.Contains("[b]8/2[/b]", body);
        Assert.Contains("[b]5/2[/b]", body);
        Assert.Contains("[b]2/1[/b]", body);
        Assert.Contains("[b]6/2[/b]", body);
        Assert.Contains("[b]7/2[/b]", body);
        Assert.Contains("avg discount of picked card", body);
        Assert.Contains("[b]1.4[/b]", body);
    }

    /// <summary>
    /// Older runs carry picks without offers. The pair still has to read as a
    /// pick count against an unobserved denominator rather than going missing.
    /// </summary>
    [Fact]
    public void DiscoveryTooltip_RunWithoutObservedOffers_StillReportsPicks()
    {
        var agg = CreateRepresentativeAggregate();
        agg.DiscoveryCardsOffered = 0;
        agg.DiscoveryCommonCardsOffered = 0;
        agg.DiscoveryUncommonCardsOffered = 0;
        agg.DiscoveryRareCardsOffered = 0;
        agg.DiscoveryAttacksOffered = 0;
        agg.DiscoverySkillsOffered = 0;
        agg.DiscoveryPowersOffered = 0;
        var sb = new StringBuilder();

        AppendDiscoveryStats(sb, agg, compact: false);
        var body = sb.ToString();

        Assert.Contains("[b]0/5[/b]", body);
        Assert.Contains("[b]0/2[/b]", body);
        Assert.Contains("[b]0/1[/b]", body);
    }

    [Fact]
    public void DiscoveryTooltip_CompactViewKeepsOnlyTheOfferedPickedTotal()
    {
        var sb = new StringBuilder();

        AppendDiscoveryStats(sb, CreateRepresentativeAggregate(), compact: true);
        var body = sb.ToString();

        Assert.Contains("Cards offered/picked", body);
        Assert.Contains("[b]15/5[/b]", body);
        Assert.DoesNotContain("Commons offered/picked", body);
        Assert.DoesNotContain("Uncommons offered/picked", body);
        Assert.DoesNotContain("Rares offered/picked", body);
        Assert.DoesNotContain("Attacks offered/picked", body);
        Assert.DoesNotContain("Skills offered/picked", body);
        Assert.DoesNotContain("Powers offered/picked", body);
        Assert.DoesNotContain("avg discount of picked card", body);
    }

    [Fact]
    public void CardAggregate_OlderShapeWithoutDiscoveryFields_DefaultsToZero()
    {
        var agg = JsonSerializer.Deserialize<CardAggregate>("{}", RunStorage.Options);

        Assert.NotNull(agg);
        Assert.Equal(0, agg!.DiscoveryCardsOffered);
        Assert.Equal(0, agg.DiscoveryCommonCardsOffered);
        Assert.Equal(0, agg.DiscoveryUncommonCardsOffered);
        Assert.Equal(0, agg.DiscoveryRareCardsOffered);
        Assert.Equal(0, agg.DiscoveryAttacksOffered);
        Assert.Equal(0, agg.DiscoverySkillsOffered);
        Assert.Equal(0, agg.DiscoveryPowersOffered);
        Assert.Equal(0, agg.DiscoveryCardsPicked);
        Assert.Equal(0, agg.DiscoveryCommonCardsPicked);
        Assert.Equal(0, agg.DiscoveryUncommonCardsPicked);
        Assert.Equal(0, agg.DiscoveryRareCardsPicked);
        Assert.Equal(0, agg.DiscoveryAttacksPicked);
        Assert.Equal(0, agg.DiscoverySkillsPicked);
        Assert.Equal(0, agg.DiscoveryPowersPicked);
        Assert.Equal(0, agg.DiscoveryEnergyDiscountTotal);
    }

    private static CardAggregate CreateRepresentativeAggregate() =>
        new()
        {
            DiscoveryCardsOffered = 15,
            DiscoveryCommonCardsOffered = 8,
            DiscoveryUncommonCardsOffered = 5,
            DiscoveryRareCardsOffered = 2,
            DiscoveryAttacksOffered = 6,
            DiscoverySkillsOffered = 7,
            DiscoveryPowersOffered = 2,
            DiscoveryCardsPicked = 5,
            DiscoveryCommonCardsPicked = 2,
            DiscoveryUncommonCardsPicked = 2,
            DiscoveryRareCardsPicked = 1,
            DiscoveryAttacksPicked = 2,
            DiscoverySkillsPicked = 2,
            DiscoveryPowersPicked = 1,
            DiscoveryEnergyDiscountTotal = 7,
        };

    private static void AssertRepresentativeAggregate(CardAggregate agg)
    {
        Assert.Equal(15, agg.DiscoveryCardsOffered);
        Assert.Equal(8, agg.DiscoveryCommonCardsOffered);
        Assert.Equal(5, agg.DiscoveryUncommonCardsOffered);
        Assert.Equal(2, agg.DiscoveryRareCardsOffered);
        Assert.Equal(6, agg.DiscoveryAttacksOffered);
        Assert.Equal(7, agg.DiscoverySkillsOffered);
        Assert.Equal(2, agg.DiscoveryPowersOffered);
        Assert.Equal(5, agg.DiscoveryCardsPicked);
        Assert.Equal(2, agg.DiscoveryCommonCardsPicked);
        Assert.Equal(2, agg.DiscoveryUncommonCardsPicked);
        Assert.Equal(1, agg.DiscoveryRareCardsPicked);
        Assert.Equal(2, agg.DiscoveryAttacksPicked);
        Assert.Equal(2, agg.DiscoverySkillsPicked);
        Assert.Equal(1, agg.DiscoveryPowersPicked);
        Assert.Equal(7, agg.DiscoveryEnergyDiscountTotal);
    }

    private static void AppendDiscoveryStats(
        StringBuilder sb,
        CardAggregate agg,
        bool compact)
    {
        var card = (Discovery)RuntimeHelpers.GetUninitializedObject(typeof(Discovery));
        _ = AppendDiscoveryStatsMethod.Invoke(null, new object?[] { sb, card, agg, compact });
    }
}
