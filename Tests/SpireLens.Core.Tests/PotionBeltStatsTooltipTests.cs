using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class PotionBeltStatsTooltipTests
{
    [Fact]
    public void Summary_UsesCombatRewardBreakdownsAndObservedOutcomes()
    {
        var entries = new[]
        {
            Entry(
                "POTION.FIRE_POTION",
                "Fire Potion",
                "Potion reward",
                "Combat",
                "Common",
                acquired: true,
                used: true),
            Entry(
                "POTION.SWIFT_POTION",
                "Swift Potion",
                "Potion reward",
                "Elite combat",
                "Uncommon",
                rejected: true),
            Entry(
                "POTION.FRUIT_JUICE",
                "Fruit Juice",
                "Potion reward",
                "Boss combat",
                "Rare",
                acquired: true),
            Entry(
                "POTION.EXPLOSIVE_AMPOULE",
                "Explosive Ampoule",
                "Potion reward",
                "Combat",
                "Rare"),
            Entry(
                "POTION.BLOOD_POTION",
                "Blood Potion",
                "Potion reward",
                "Event",
                "Common",
                rejected: true),
            Entry(
                "POTION.WEAK_POTION",
                "Weak Potion",
                "Shop",
                "Shop",
                "Common",
                acquired: true,
                discarded: true),
            Entry(
                "POTION.FEAR_POTION",
                "Fear Potion",
                "Shop",
                "Shop",
                "Uncommon"),
        };

        var summary = PotionBeltStatsTooltip.Summarize(entries, floors: 8);

        Assert.Equal(7, summary.TotalPotionsOffered);
        Assert.Equal(4, summary.CombatRewardPotionsOffered);
        Assert.Equal(1, summary.CommonCombatRewardPotions);
        Assert.Equal(1, summary.UncommonCombatRewardPotions);
        Assert.Equal(2, summary.RareCombatRewardPotions);
        Assert.Equal(1, summary.FruitJuicesInCombatRewards);
        Assert.Equal(2, summary.RejectedPotionsAtRewardScreen);
        Assert.Equal(1, summary.PotionsOfferedInEvents);
        Assert.Equal(2, summary.PotionsOfferedInShops);
        Assert.Equal(1, summary.PotionsPurchasedInShops);
        Assert.Equal(1, summary.TotalPotionActivations);
        Assert.Equal(1, summary.TotalPotionDiscards);
    }

    [Fact]
    public void Tooltip_ShowsEveryRequestedRowAndCombatOfferFloorRate()
    {
        var body = PotionBeltStatsTooltip.BuildBodyBBCode(
            new PotionBeltStatsSummary
            {
                Floors = 8,
                TotalPotionsOffered = 7,
                CombatRewardPotionsOffered = 4,
                CommonCombatRewardPotions = 1,
                UncommonCombatRewardPotions = 1,
                RareCombatRewardPotions = 2,
                FruitJuicesInCombatRewards = 1,
                RejectedPotionsAtRewardScreen = 2,
                PotionsOfferedInEvents = 1,
                PotionsOfferedInShops = 2,
                PotionsPurchasedInShops = 1,
                TotalPotionActivations = 3,
                TotalPotionDiscards = 1,
            });

        Assert.Contains("Combat reward potions offered   [b]4[/b]", body);
        Assert.Contains(
            "Avg potions offered per floor   [b]0.88[/b]",
            body);
        Assert.Contains("Common combat reward potions   [b]1[/b]", body);
        Assert.Contains("Uncommon combat reward potions   [b]1[/b]", body);
        Assert.Contains("Rare combat reward potions   [b]2[/b]", body);
        Assert.Contains("Fruit Juices in combat rewards   [b]1[/b]", body);
        Assert.Contains("Rejected potions at reward screen   [b]2[/b]", body);
        Assert.Contains("Potions offered in events   [b]1[/b]", body);
        Assert.Contains("Potions offered in shops   [b]2[/b]", body);
        Assert.Contains("Potions purchased in shops   [b]1[/b]", body);
        Assert.Contains("Total potion activations   [b]3[/b]", body);
        Assert.Contains("Total potion discards   [b]1[/b]", body);
    }

    private static PotionRunHistoryEntry Entry(
        string potionId,
        string displayName,
        string acquisitionMethod,
        string locationKind,
        string rarity,
        bool acquired = false,
        bool used = false,
        bool discarded = false,
        bool rejected = false)
        => new()
        {
            PotionId = potionId,
            DisplayName = displayName,
            AcquisitionMethod = acquisitionMethod,
            SeenLocationKind = locationKind,
            Rarity = rarity,
            Acquired = acquired,
            Used = used,
            Discarded = discarded,
            RejectedAtRewardScreen = rejected,
        };
}
