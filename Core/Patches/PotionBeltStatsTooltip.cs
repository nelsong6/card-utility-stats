using System.Globalization;
using System.Text;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Nodes.Potions;

namespace SpireLens.Core.Patches;

/// <summary>
/// Shared run-wide potion page appended to every top-bar belt holder. The
/// holder may contain a potion or be empty; both use the game's ordinary
/// hover-tip lifecycle.
/// </summary>
internal static class PotionBeltStatsTooltip
{
    internal static bool TryBuildNativeHoverTip(
        NPotionHolder owner,
        out HoverTip tip)
    {
        tip = default;
        if (owner == null
            || !RunTracker.TryGetEffectivePotionStatsSource(
                out var history,
                out var floors))
        {
            return false;
        }

        tip = StatsTooltip.CreateNativeTip(
            "Potion stats",
            BuildBodyBBCode(Summarize(history, floors)),
            stretchHorizontally: true);
        return true;
    }

    internal static PotionBeltStatsSummary Summarize(
        IEnumerable<PotionRunHistoryEntry>? history,
        int floors)
    {
        var summary = new PotionBeltStatsSummary
        {
            Floors = Math.Max(0, floors),
        };

        foreach (var entry in history ?? Enumerable.Empty<PotionRunHistoryEntry>())
        {
            if (entry == null) continue;
            var isPotionReward = IsPotionReward(entry);
            var isShopOffer = string.Equals(
                entry.AcquisitionMethod,
                "Shop",
                StringComparison.OrdinalIgnoreCase);
            if (isPotionReward || isShopOffer)
                summary.TotalPotionsOffered++;

            if (IsCombatRewardOffer(entry))
            {
                summary.CombatRewardPotionsOffered++;
                switch (ResolveRarity(entry))
                {
                    case PotionRarity.Common:
                        summary.CommonCombatRewardPotions++;
                        break;
                    case PotionRarity.Uncommon:
                        summary.UncommonCombatRewardPotions++;
                        break;
                    case PotionRarity.Rare:
                        summary.RareCombatRewardPotions++;
                        break;
                }

                if (IsFruitJuice(entry))
                    summary.FruitJuicesInCombatRewards++;
            }

            if (entry.RejectedAtRewardScreen)
                summary.RejectedPotionsAtRewardScreen++;
            if (isPotionReward
                && string.Equals(
                    entry.SeenLocationKind,
                    "Event",
                    StringComparison.OrdinalIgnoreCase))
            {
                summary.PotionsOfferedInEvents++;
            }

            if (isShopOffer)
            {
                summary.PotionsOfferedInShops++;
                if (entry.Acquired)
                    summary.PotionsPurchasedInShops++;
            }

            if (entry.Used)
                summary.TotalPotionActivations++;
            if (entry.Discarded)
                summary.TotalPotionDiscards++;
        }

        return summary;
    }

    internal static string BuildBodyBBCode(PotionBeltStatsSummary summary)
    {
        summary ??= new PotionBeltStatsSummary();
        var potionIcon = StatConceptGlossary.RenderHintedGlyph("potion");
        var commonIcon = StatConceptGlossary.RenderHintedGlyph("potion_common");
        var uncommonIcon = StatConceptGlossary.RenderHintedGlyph("potion_uncommon");
        var rareIcon = StatConceptGlossary.RenderHintedGlyph("potion_rare");
        var fruitJuiceIcon = StatConceptGlossary.RenderHintedGlyph("fruit_juice");
        var offeredIcon = StatConceptGlossary.RenderHintedGlyph("offered");
        var takenIcon = StatConceptGlossary.RenderHintedGlyph("taken");
        var averageIcon = StatConceptGlossary.RenderHintedGlyph("average");
        var floorIcon = StatConceptGlossary.RenderHintedGlyph("floor");
        var combatIcon = StatConceptGlossary.RenderHintedGlyph("combat");
        var eventIcon = StatConceptGlossary.RenderHintedGlyph("unknown_room");
        var shopIcon = StatConceptGlossary.RenderHintedGlyph("shop");
        var activationIcon = StatConceptGlossary.RenderHintedGlyph("activation");
        var wastedIcon = StatConceptGlossary.RenderHintedGlyph("wasted");
        var body = new StringBuilder();

        AppendRow(
            body,
            $"{combatIcon} {offeredIcon} {potionIcon}",
            "Offered",
            summary.CombatRewardPotionsOffered,
            "Potions offered in combat rewards this run.");
        AppendRow(
            body,
            $"{averageIcon} {floorIcon} {offeredIcon} {potionIcon}",
            "Offered",
            Divide(summary.TotalPotionsOffered, summary.Floors),
            "Average potions offered per floor reached.");
        AppendRow(
            body,
            $"{combatIcon} {offeredIcon} {commonIcon}",
            "Offered",
            summary.CommonCombatRewardPotions,
            "Common potions offered in combat rewards.");
        AppendRow(
            body,
            $"{combatIcon} {offeredIcon} {uncommonIcon}",
            "Offered",
            summary.UncommonCombatRewardPotions,
            "Uncommon potions offered in combat rewards.");
        AppendRow(
            body,
            $"{combatIcon} {offeredIcon} {rareIcon}",
            "Offered",
            summary.RareCombatRewardPotions,
            "Rare potions offered in combat rewards.");
        AppendRow(
            body,
            $"{combatIcon} {offeredIcon} {fruitJuiceIcon}",
            "Offered",
            summary.FruitJuicesInCombatRewards,
            "Fruit Juices offered in combat rewards.");
        AppendRow(
            body,
            $"{offeredIcon} {wastedIcon} {potionIcon}",
            "Rejected",
            summary.RejectedPotionsAtRewardScreen,
            "Potions rejected at reward screens.");
        AppendRow(
            body,
            $"{eventIcon} {offeredIcon} {potionIcon}",
            "Offered",
            summary.PotionsOfferedInEvents,
            "Potions offered in events.");
        AppendRow(
            body,
            $"{shopIcon} {offeredIcon} {potionIcon}",
            "Offered",
            summary.PotionsOfferedInShops,
            "Potions offered in shops.");
        AppendRow(
            body,
            $"{shopIcon} {takenIcon} {potionIcon}",
            "Purchased",
            summary.PotionsPurchasedInShops,
            "Potions purchased in shops.");
        AppendRow(
            body,
            $"{activationIcon} {potionIcon}",
            "Activated",
            summary.TotalPotionActivations,
            "Potions activated this run.");
        AppendRow(
            body,
            $"{wastedIcon} {potionIcon}",
            "Discarded",
            summary.TotalPotionDiscards,
            "Potions discarded without being used this run.");

        return body.ToString();
    }

    private static bool IsCombatRewardOffer(PotionRunHistoryEntry entry)
        => IsPotionReward(entry)
            && entry.SeenLocationKind is "Combat" or "Elite combat" or "Boss combat";

    private static bool IsPotionReward(PotionRunHistoryEntry entry)
        => string.Equals(
            entry.AcquisitionMethod,
            "Potion reward",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsFruitJuice(PotionRunHistoryEntry entry)
        => string.Equals(
            entry.PotionId,
            "POTION.FRUIT_JUICE",
            StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                entry.DisplayName,
                "Fruit Juice",
                StringComparison.OrdinalIgnoreCase);

    private static PotionRarity? ResolveRarity(PotionRunHistoryEntry entry)
    {
        if (Enum.TryParse<PotionRarity>(
                entry.Rarity,
                ignoreCase: true,
                out var rarity))
        {
            return rarity;
        }

        try
        {
            return ModelDb.GetByIdOrNull<PotionModel>(
                ModelId.Deserialize(entry.PotionId))?.Rarity;
        }
        catch
        {
            return null;
        }
    }

    private static void AppendRow(
        StringBuilder body,
        string icon,
        string label,
        int value,
        string fullDescription)
        => AppendRow(
            body,
            icon,
            label,
            value.ToString(CultureInfo.InvariantCulture),
            fullDescription);

    private static void AppendRow(
        StringBuilder body,
        string icon,
        string label,
        decimal value,
        string fullDescription)
        => AppendRow(
            body,
            icon,
            label,
            value.ToString("0.##", CultureInfo.InvariantCulture),
            fullDescription);

    private static void AppendRow(
        StringBuilder body,
        string icon,
        string label,
        string value,
        string fullDescription)
    {
        if (body.Length > 0) body.Append('\n');
        body.Append(StatsTooltip.RenderRowInformationHint(label, fullDescription))
            .Append(' ')
            .Append(icon)
            .Append(' ')
            .Append(label)
            .Append("   [b]")
            .Append(value)
            .Append("[/b]");
    }

    private static decimal Divide(int numerator, int denominator)
        => denominator > 0 ? (decimal)numerator / denominator : 0m;
}

internal sealed class PotionBeltStatsSummary
{
    public int Floors { get; set; }
    public int TotalPotionsOffered { get; set; }
    public int CombatRewardPotionsOffered { get; set; }
    public int CommonCombatRewardPotions { get; set; }
    public int UncommonCombatRewardPotions { get; set; }
    public int RareCombatRewardPotions { get; set; }
    public int FruitJuicesInCombatRewards { get; set; }
    public int RejectedPotionsAtRewardScreen { get; set; }
    public int PotionsOfferedInEvents { get; set; }
    public int PotionsOfferedInShops { get; set; }
    public int PotionsPurchasedInShops { get; set; }
    public int TotalPotionActivations { get; set; }
    public int TotalPotionDiscards { get; set; }
}
