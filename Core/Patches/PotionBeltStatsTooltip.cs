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
        var goldIcon = StatConceptGlossary.RenderHintedGlyph("gold");
        var activationIcon = StatConceptGlossary.RenderHintedGlyph("activation");
        var wastedIcon = StatConceptGlossary.RenderHintedGlyph("wasted");
        var body = new StringBuilder();

        AppendRow(
            body,
            $"{combatIcon} {offeredIcon} {potionIcon}",
            "Combat reward potions offered",
            summary.CombatRewardPotionsOffered);
        AppendRow(
            body,
            $"{averageIcon} {floorIcon} {potionIcon}",
            "Avg potions offered per floor",
            Divide(summary.TotalPotionsOffered, summary.Floors));
        AppendRow(
            body,
            commonIcon,
            "Common combat reward potions",
            summary.CommonCombatRewardPotions);
        AppendRow(
            body,
            uncommonIcon,
            "Uncommon combat reward potions",
            summary.UncommonCombatRewardPotions);
        AppendRow(
            body,
            rareIcon,
            "Rare combat reward potions",
            summary.RareCombatRewardPotions);
        AppendRow(
            body,
            fruitJuiceIcon,
            "Fruit Juices in combat rewards",
            summary.FruitJuicesInCombatRewards);
        AppendRow(
            body,
            $"{wastedIcon} {potionIcon}",
            "Rejected potions at reward screen",
            summary.RejectedPotionsAtRewardScreen);
        AppendRow(
            body,
            $"{eventIcon} {offeredIcon} {potionIcon}",
            "Potions offered in events",
            summary.PotionsOfferedInEvents);
        AppendRow(
            body,
            $"{goldIcon} {offeredIcon} {potionIcon}",
            "Potions offered in shops",
            summary.PotionsOfferedInShops);
        AppendRow(
            body,
            $"{goldIcon} {takenIcon} {potionIcon}",
            "Potions purchased in shops",
            summary.PotionsPurchasedInShops);
        AppendRow(
            body,
            $"{activationIcon} {potionIcon}",
            "Total potion activations",
            summary.TotalPotionActivations);
        AppendRow(
            body,
            $"{wastedIcon} {potionIcon}",
            "Total potion discards",
            summary.TotalPotionDiscards);

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
        int value)
        => AppendRow(
            body,
            icon,
            label,
            value.ToString(CultureInfo.InvariantCulture));

    private static void AppendRow(
        StringBuilder body,
        string icon,
        string label,
        decimal value)
        => AppendRow(
            body,
            icon,
            label,
            value.ToString("0.##", CultureInfo.InvariantCulture));

    private static void AppendRow(
        StringBuilder body,
        string icon,
        string label,
        string value)
    {
        if (body.Length > 0) body.Append('\n');
        body.Append(icon)
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
