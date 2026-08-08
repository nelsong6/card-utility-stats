using System.Globalization;
using System.Collections.Generic;
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
        var body = new StringBuilder();

        AppendRow(
            body,
            ["potion", "offered", "in", "all", "combat"],
            [],
            "Potions offered in all combats",
            summary.CombatRewardPotionsOffered,
            "Potions offered in combat rewards this run.");
        AppendRow(
            body,
            ["average", "potion", "offered", "floor"],
            ["floor"],
            "Average potions offered per floor",
            Divide(summary.TotalPotionsOffered, summary.Floors),
            "Average potions offered per floor reached.");
        AppendRow(
            body,
            ["potion_common", "offered", "in", "all", "combat"],
            [],
            "Common potions offered in all combats",
            summary.CommonCombatRewardPotions,
            "Common potions offered in combat rewards.");
        AppendRow(
            body,
            ["potion_uncommon", "offered", "in", "all", "combat"],
            [],
            "Uncommon potions offered in all combats",
            summary.UncommonCombatRewardPotions,
            "Uncommon potions offered in combat rewards.");
        AppendRow(
            body,
            ["potion_rare", "offered", "in", "all", "combat"],
            [],
            "Rare potions offered in all combats",
            summary.RareCombatRewardPotions,
            "Rare potions offered in combat rewards.");
        AppendRow(
            body,
            ["fruit_juice", "offered", "in", "all", "combat"],
            [],
            "Fruit Juices offered in all combats",
            summary.FruitJuicesInCombatRewards,
            "Fruit Juices offered in combat rewards.");
        AppendRow(
            body,
            ["potion", "offered", "wasted"],
            [],
            string.Empty,
            summary.RejectedPotionsAtRewardScreen,
            "Potions rejected at reward screens.");
        AppendRow(
            body,
            ["potion", "offered", "in", "all", "unknown_room"],
            [],
            "Potions offered in all events",
            summary.PotionsOfferedInEvents,
            "Potions offered in events.");
        AppendRow(
            body,
            ["potion", "offered", "in", "all", "shop"],
            [],
            "Potions offered in all shops",
            summary.PotionsOfferedInShops,
            "Potions offered in shops.");
        AppendRow(
            body,
            ["potion", "taken", "in", "all", "shop"],
            [],
            string.Empty,
            summary.PotionsPurchasedInShops,
            "Potions purchased in shops.");
        AppendRow(
            body,
            ["potion", "activation"],
            [],
            "Activated",
            summary.TotalPotionActivations,
            "Potions activated this run.");
        AppendRow(
            body,
            ["potion", "wasted"],
            [],
            string.Empty,
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
        IReadOnlyList<string> conceptIds,
        IReadOnlyList<string> denominatorConceptIds,
        string label,
        int value,
        string fullDescription)
        => AppendRow(
            body,
            conceptIds,
            denominatorConceptIds,
            label,
            value.ToString(CultureInfo.InvariantCulture),
            fullDescription);

    private static void AppendRow(
        StringBuilder body,
        IReadOnlyList<string> conceptIds,
        IReadOnlyList<string> denominatorConceptIds,
        string label,
        decimal value,
        string fullDescription)
        => AppendRow(
            body,
            conceptIds,
            denominatorConceptIds,
            label,
            value.ToString("0.##", CultureInfo.InvariantCulture),
            fullDescription);

    private static void AppendRow(
        StringBuilder body,
        IReadOnlyList<string> conceptIds,
        IReadOnlyList<string> denominatorConceptIds,
        string label,
        string value,
        string fullDescription)
    {
        StatsTooltip.AppendInlineStatRow(
            body,
            conceptIds,
            denominatorConceptIds,
            label,
            value,
            fullDescription);
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
