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

        var rows = body.Split('\n');
        var offered = StatConceptGlossary.RenderHintedGlyph("offered");
        var activation = StatConceptGlossary.RenderHintedGlyph("activation");
        var inScope = StatConceptGlossary.RenderHintedGlyph("in");
        var all = StatConceptGlossary.RenderHintedGlyph("all");
        Assert.Equal(12, rows.Length);

        var allCombatOffers = Assert.Single(
            rows,
            row => row.Contains("[b]4[/b]"));
        AssertConceptOrder(
            allCombatOffers,
            "potion",
            "offered",
            "in",
            "all",
            "combat");
        Assert.Contains(rows, row => row.Contains("Average:")
            && row.Contains("Floor:")
            && row.Contains("Offered:")
            && row.Contains("Potion:")
            && row.Contains("[b]0.88[/b]"));
        Assert.Contains(rows, row => row.Contains("Combat:")
            && row.Contains("Offered:")
            && row.Contains("Common potion:")
            && row.Contains("[b]1[/b]"));
        Assert.Contains(rows, row => row.Contains("Combat:")
            && row.Contains("Offered:")
            && row.Contains("Uncommon potion:")
            && row.Contains("[b]1[/b]"));
        Assert.Contains(rows, row => row.Contains("Combat:")
            && row.Contains("Offered:")
            && row.Contains("Rare potion:")
            && row.Contains("[b]2[/b]"));
        Assert.Contains(rows, row => row.Contains("Combat:")
            && row.Contains("Offered:")
            && row.Contains("Fruit Juice:")
            && row.Contains("[b]1[/b]"));
        Assert.Contains(rows, row => row.Contains("Offered:")
            && row.Contains("Wasted:")
            && row.Contains("Potion:")
            && row.Contains("[b]2[/b]"));
        Assert.Contains(rows, row => row.Contains("Unknown room:")
            && row.Contains("Offered:")
            && row.Contains("Potion:")
            && row.Contains("[b]1[/b]"));
        Assert.Contains(rows, row => row.Contains("Merchant:")
            && row.Contains("Offered:")
            && row.Contains("Potion:")
            && row.Contains("[b]2[/b]"));
        Assert.Contains(rows, row => row.Contains("Merchant:")
            && row.Contains("Taken:")
            && row.Contains("Potion:")
            && row.Contains("[b]1[/b]"));
        Assert.Contains(rows, row => row.Contains("Activation:")
            && row.Contains("Potion:")
            && row.Contains("[b]3[/b]"));
        Assert.Contains(rows, row => row.Contains("Wasted:")
            && row.Contains("Potion:")
            && row.Contains("[b]1[/b]"));
        Assert.StartsWith("[table=3]", body);
        Assert.EndsWith("[/table]", body);
        Assert.Equal(1, CountOccurrences(body, "[table=3]"));
        Assert.Equal(36, CountOccurrences(body, "[cell "));
        Assert.Equal(12, CountOccurrences(body, "[left]"));
        Assert.DoesNotContain($"{offered} Offered", body);
        Assert.DoesNotContain($"{activation} Activated", body);
        Assert.DoesNotContain("Rejected   [b]", body);
        Assert.DoesNotContain("Purchased   [b]", body);
        Assert.Contains(inScope, body);
        Assert.Contains(all, body);

        Assert.Contains(
            StatConceptGlossary.RenderInformationHint(
                "Potions offered in combat rewards this run."),
            body);
        Assert.Contains(
            StatConceptGlossary.RenderInformationHint(
                "Potions discarded without being used this run."),
            body);
    }

    private static void AssertConceptOrder(string row, params string[] conceptIds)
    {
        var previousIndex = -1;
        foreach (var conceptId in conceptIds)
        {
            var glyph = StatConceptGlossary.RenderHintedGlyph(conceptId);
            var index = row.IndexOf(glyph, StringComparison.Ordinal);
            Assert.True(
                index > previousIndex,
                $"Expected '{conceptId}' after index {previousIndex} in row: {row}");
            previousIndex = index;
        }
    }

    private static int CountOccurrences(string value, string token)
        => value.Split(token, StringSplitOptions.None).Length - 1;

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
