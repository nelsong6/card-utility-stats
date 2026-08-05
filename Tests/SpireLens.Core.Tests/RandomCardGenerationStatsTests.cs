using System.Collections.Generic;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Cards;
using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

public class RandomCardGenerationStatsTests
{
    [Fact]
    public void ObservedGenerationAndExactUse_KeepUniqueUseSeparateFromReplays()
    {
        var aggregate = new RandomCardGenerationAggregate();

        RunTracker.RecordRandomCardGeneratedForTest(
            aggregate,
            "CARD.STRIKE_IRONCLAD",
            "Strike",
            CardRarity.Basic,
            CardType.Attack,
            energyCostBeforeDiscount: 1,
            discount: 1,
            upgradeLevel: 0,
            PileType.Draw);
        RunTracker.RecordRandomCardGeneratedForTest(
            aggregate,
            "CARD.CLOTHESLINE",
            "Clothesline",
            CardRarity.Uncommon,
            CardType.Attack,
            energyCostBeforeDiscount: 2,
            discount: 2,
            upgradeLevel: 1,
            PileType.Draw);
        RunTracker.RecordRandomGeneratedCardPlayForTest(
            aggregate,
            "CARD.STRIKE_IRONCLAD",
            "Strike",
            firstPlay: true);
        RunTracker.RecordRandomGeneratedCardPlayForTest(
            aggregate,
            "CARD.STRIKE_IRONCLAD",
            "Strike",
            firstPlay: false);

        Assert.Equal(2, aggregate.CardsGenerated);
        Assert.Equal(1, aggregate.GeneratedCardsPlayed);
        Assert.Equal(2, aggregate.GeneratedCardPlays);
        Assert.Equal(1, aggregate.BasicCardsGenerated);
        Assert.Equal(1, aggregate.UncommonCardsGenerated);
        Assert.Equal(2, aggregate.AttacksGenerated);
        Assert.Equal(1, aggregate.UpgradedCardsGenerated);
        Assert.Equal(3, aggregate.EnergyCostBeforeDiscountTotal);
        Assert.Equal(3, aggregate.EnergyDiscountGrantedTotal);
        Assert.Equal(2, aggregate.CardsAddedToDrawPile);
        Assert.Equal(
            1,
            aggregate.CardsById["CARD.STRIKE_IRONCLAD"].GeneratedCardsPlayed);
        Assert.Equal(
            2,
            aggregate.CardsById["CARD.STRIKE_IRONCLAD"].Plays);
    }

    [Fact]
    public void Promotion_MergesDirectAndPowerGenerationLedgers()
    {
        var run = new RunData();
        run.Aggregates["CARD.METAMORPHOSIS#1"] = new CardAggregate
        {
            RandomCardGeneration = Generation(
                "CARD.STRIKE_IRONCLAD",
                generated: 2,
                used: 1,
                plays: 1),
        };
        run.MetaStats.PowerAggregates["POWER.CREATIVE_AI"] = new PowerAggregate
        {
            PowerId = "POWER.CREATIVE_AI",
            DisplayName = "Creative AI",
            RandomCardGeneration = Generation(
                "CARD.BUFFER",
                generated: 1,
                used: 1,
                plays: 1),
        };

        var pending = new PendingCombat();
        pending.CombatAggregates["CARD.METAMORPHOSIS#1"] = new CardAggregate
        {
            RandomCardGeneration = Generation(
                "CARD.STRIKE_IRONCLAD",
                generated: 3,
                used: 2,
                plays: 3),
        };
        pending.MetaStats.PowerAggregates["POWER.CREATIVE_AI"] =
            new PowerAggregate
            {
                PowerId = "POWER.CREATIVE_AI",
                DisplayName = "Creative AI",
                RandomCardGeneration = Generation(
                    "CARD.BUFFER",
                    generated: 4,
                    used: 2,
                    plays: 3),
            };

        RunTracker.PromotePendingCombatIntoRun(pending, run);

        var card = run.Aggregates["CARD.METAMORPHOSIS#1"]
            .RandomCardGeneration!;
        Assert.Equal(5, card.CardsGenerated);
        Assert.Equal(3, card.GeneratedCardsPlayed);
        Assert.Equal(4, card.GeneratedCardPlays);
        Assert.Equal(
            5,
            card.CardsById["CARD.STRIKE_IRONCLAD"].Generated);

        var power = run.MetaStats.PowerAggregates["POWER.CREATIVE_AI"]
            .RandomCardGeneration!;
        Assert.Equal(5, power.CardsGenerated);
        Assert.Equal(3, power.GeneratedCardsPlayed);
        Assert.Equal(4, power.GeneratedCardPlays);
        Assert.Equal(5, power.CardsById["CARD.BUFFER"].Generated);
    }

    [Fact]
    public void OlderShapes_DefaultToNoGenerationAggregate()
    {
        var card = JsonSerializer.Deserialize<CardAggregate>(
            "{}",
            RunStorage.Options);
        var power = JsonSerializer.Deserialize<PowerAggregate>(
            "{}",
            RunStorage.Options);

        Assert.NotNull(card);
        Assert.NotNull(power);
        Assert.Null(card!.RandomCardGeneration);
        Assert.Null(power!.RandomCardGeneration);
    }

    private static RandomCardGenerationAggregate Generation(
        string cardId,
        int generated,
        int used,
        int plays)
        => new()
        {
            CardsGenerated = generated,
            GeneratedCardsPlayed = used,
            GeneratedCardPlays = plays,
            CardsById = new Dictionary<string, GeneratedCardOutcomeAggregate>
            {
                [cardId] = new()
                {
                    CardId = cardId,
                    DisplayName = cardId,
                    Generated = generated,
                    GeneratedCardsPlayed = used,
                    Plays = plays,
                },
            },
        };
}
