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

public class JackOfAllTradesStatsTests
{
    private const string JackCardId = "CARD.JACK_OF_ALL_TRADES";

    private static readonly MethodInfo AppendJackOfAllTradesStatsMethod =
        typeof(CardHoverShowPatch).GetMethod(
            "AppendJackOfAllTradesStats",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("AppendJackOfAllTradesStats not found.");

    [Fact]
    public void CardAggregate_JackOfAllTradesFields_DefaultToZero()
    {
        var agg = new CardAggregate();

        Assert.Equal(0, agg.JackColorlessCardsAdded);
        Assert.Equal(0, agg.JackUncommonCardsAdded);
        Assert.Equal(0, agg.JackRareCardsAdded);
        Assert.Equal(0, agg.JackAttacksAdded);
        Assert.Equal(0, agg.JackSkillsAdded);
        Assert.Equal(0, agg.JackPowersAdded);
        Assert.Equal(0, agg.JackAddedCardCostTotal);
    }

    [Fact]
    public void CardAggregate_JackOfAllTradesFields_JsonRoundtripPreservesFields()
    {
        var run = new RunData();
        run.Aggregates[$"{JackCardId}#1"] = CreateRepresentativeAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"jack_colorless_cards_added\"", json);
        Assert.Contains("\"jack_uncommon_cards_added\"", json);
        Assert.Contains("\"jack_rare_cards_added\"", json);
        Assert.Contains("\"jack_attacks_added\"", json);
        Assert.Contains("\"jack_skills_added\"", json);
        Assert.Contains("\"jack_powers_added\"", json);
        Assert.Contains("\"jack_added_card_cost_total\"", json);
        Assert.NotNull(restored);

        AssertRepresentativeAggregate(restored!.Aggregates[$"{JackCardId}#1"]);
    }

    [Fact]
    public void RunTracker_JackOfAllTradesResults_CountRarityTypeAndNonnegativeCost()
    {
        var agg = new CardAggregate();

        RunTracker.RecordJackOfAllTradesCardAddedForTest(
            agg, CardRarity.Uncommon, CardType.Attack, energyCost: 1);
        RunTracker.RecordJackOfAllTradesCardAddedForTest(
            agg, CardRarity.Uncommon, CardType.Skill, energyCost: 0);
        RunTracker.RecordJackOfAllTradesCardAddedForTest(
            agg, CardRarity.Rare, CardType.Power, energyCost: 3);
        RunTracker.RecordJackOfAllTradesCardAddedForTest(
            agg, CardRarity.Rare, CardType.Skill, energyCost: 2);
        RunTracker.RecordJackOfAllTradesCardAddedForTest(
            agg, CardRarity.Uncommon, CardType.Attack, energyCost: 1);
        RunTracker.RecordJackOfAllTradesCardAddedForTest(
            agg, CardRarity.Uncommon, CardType.Skill, energyCost: -1);

        Assert.Equal(6, agg.JackColorlessCardsAdded);
        Assert.Equal(4, agg.JackUncommonCardsAdded);
        Assert.Equal(2, agg.JackRareCardsAdded);
        Assert.Equal(2, agg.JackAttacksAdded);
        Assert.Equal(3, agg.JackSkillsAdded);
        Assert.Equal(1, agg.JackPowersAdded);
        Assert.Equal(7, agg.JackAddedCardCostTotal);
    }

    [Fact]
    public void CardAggregatePooler_JackOfAllTradesFields_MergeAcrossInstances()
    {
        var pooled = CardAggregatePooler.PoolByDefinition(
            new Dictionary<string, CardAggregate>
            {
                [$"{JackCardId}#1"] = new()
                {
                    JackColorlessCardsAdded = 2,
                    JackUncommonCardsAdded = 1,
                    JackRareCardsAdded = 1,
                    JackAttacksAdded = 1,
                    JackSkillsAdded = 1,
                    JackAddedCardCostTotal = 3,
                },
                [$"{JackCardId}#2"] = new()
                {
                    JackColorlessCardsAdded = 3,
                    JackUncommonCardsAdded = 2,
                    JackRareCardsAdded = 1,
                    JackAttacksAdded = 1,
                    JackSkillsAdded = 1,
                    JackPowersAdded = 1,
                    JackAddedCardCostTotal = 4,
                },
                ["CARD.NEUTRALIZE#1"] = new()
                {
                    JackColorlessCardsAdded = 99,
                    JackAddedCardCostTotal = 99,
                },
            },
            JackCardId);

        Assert.NotNull(pooled);
        Assert.Equal(5, pooled!.JackColorlessCardsAdded);
        Assert.Equal(3, pooled.JackUncommonCardsAdded);
        Assert.Equal(2, pooled.JackRareCardsAdded);
        Assert.Equal(2, pooled.JackAttacksAdded);
        Assert.Equal(2, pooled.JackSkillsAdded);
        Assert.Equal(1, pooled.JackPowersAdded);
        Assert.Equal(7, pooled.JackAddedCardCostTotal);
    }

    [Fact]
    public void JackOfAllTradesTooltip_FullViewShowsOutcomeBreakdownAndAverageCost()
    {
        var sb = new StringBuilder();

        AppendJackOfAllTradesStats(sb, CreateRepresentativeAggregate(), compact: false);
        var body = sb.ToString();

        Assert.Contains("Colorless cards added", body);
        Assert.Contains("uncommons added", body);
        Assert.Contains("rares added", body);
        Assert.Contains("Attacks added", body);
        Assert.Contains("Skills added", body);
        Assert.Contains("Powers added", body);
        Assert.Contains("Avg cost of cards added", body);
        Assert.Contains("[b]1.4[/b]", body);
    }

    [Fact]
    public void JackOfAllTradesTooltip_CompactViewKeepsOnlyTotalCardsAdded()
    {
        var sb = new StringBuilder();

        AppendJackOfAllTradesStats(sb, CreateRepresentativeAggregate(), compact: true);
        var body = sb.ToString();

        Assert.Contains("Colorless cards added", body);
        Assert.DoesNotContain("uncommons added", body);
        Assert.DoesNotContain("rares added", body);
        Assert.DoesNotContain("Attacks added", body);
        Assert.DoesNotContain("Skills added", body);
        Assert.DoesNotContain("Powers added", body);
        Assert.DoesNotContain("Avg cost of cards added", body);
    }

    [Fact]
    public void CardAggregate_OlderShapeWithoutJackOfAllTradesFields_DefaultsToZero()
    {
        var agg = JsonSerializer.Deserialize<CardAggregate>("{}", RunStorage.Options);

        Assert.NotNull(agg);
        Assert.Equal(0, agg!.JackColorlessCardsAdded);
        Assert.Equal(0, agg.JackUncommonCardsAdded);
        Assert.Equal(0, agg.JackRareCardsAdded);
        Assert.Equal(0, agg.JackAttacksAdded);
        Assert.Equal(0, agg.JackSkillsAdded);
        Assert.Equal(0, agg.JackPowersAdded);
        Assert.Equal(0, agg.JackAddedCardCostTotal);
    }

    private static CardAggregate CreateRepresentativeAggregate() =>
        new()
        {
            JackColorlessCardsAdded = 5,
            JackUncommonCardsAdded = 3,
            JackRareCardsAdded = 2,
            JackAttacksAdded = 2,
            JackSkillsAdded = 2,
            JackPowersAdded = 1,
            JackAddedCardCostTotal = 7,
        };

    private static void AssertRepresentativeAggregate(CardAggregate agg)
    {
        Assert.Equal(5, agg.JackColorlessCardsAdded);
        Assert.Equal(3, agg.JackUncommonCardsAdded);
        Assert.Equal(2, agg.JackRareCardsAdded);
        Assert.Equal(2, agg.JackAttacksAdded);
        Assert.Equal(2, agg.JackSkillsAdded);
        Assert.Equal(1, agg.JackPowersAdded);
        Assert.Equal(7, agg.JackAddedCardCostTotal);
    }

    private static void AppendJackOfAllTradesStats(
        StringBuilder sb,
        CardAggregate agg,
        bool compact)
    {
        var card = (JackOfAllTrades)RuntimeHelpers.GetUninitializedObject(typeof(JackOfAllTrades));
        _ = AppendJackOfAllTradesStatsMethod.Invoke(null, new object?[] { sb, card, agg, compact });
    }
}
