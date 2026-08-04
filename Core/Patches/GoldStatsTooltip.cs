using System.Globalization;
using System.Collections.Generic;
using System.Text;
using MegaCrit.sts2.Core.Nodes.TopBar;
using MegaCrit.Sts2.Core.HoverTips;

namespace SpireLens.Core.Patches;

internal static class GoldStatsTooltip
{
    internal static bool TryBuildNativeHoverTip(
        NTopBarGold owner,
        out HoverTip tip)
    {
        tip = default;
        if (owner == null
            || !RunTracker.TryGetEffectiveGoldStats(
                out var goldStats,
                out var floors))
        {
            return false;
        }

        return TryBuildNativeHoverTip(goldStats, floors, out tip);
    }

    internal static bool TryBuildNativeHoverTip(
        RunGoldStats? goldStats,
        int floors,
        out HoverTip tip)
    {
        tip = StatsTooltip.CreateNativeTip(
            "Gold stats",
            BuildBodyBBCode(goldStats, floors),
            stretchHorizontally: true);
        return true;
    }

    internal static string BuildBodyBBCode(
        RunGoldStats? goldStats,
        int floors)
    {
        goldStats ??= new RunGoldStats();
        var body = new StringBuilder();

        AppendStatRow(
            body,
            ["gold_gained"],
            [],
            string.Empty,
            goldStats.GoldAcquired,
            "All gold acquired this run.");
        AppendStatRow(
            body,
            ["gold"],
            [],
            "Spent",
            goldStats.GoldSpent,
            "All gold spent this run.");
        AppendStatRow(
            body,
            ["shop", "gold"],
            [],
            "Spent",
            goldStats.GoldSpentInShops,
            "Gold spent in shops this run.");
        AppendStatRow(
            body,
            ["average", "shop", "gold"],
            ["shop"],
            "Spent",
            Divide(goldStats.GoldSpentInShops, goldStats.ShopsVisited),
            "Average gold spent per shop visited.");
        AppendStatRow(
            body,
            ["unknown_room", "gold"],
            [],
            "Spent",
            goldStats.GoldSpentInEvents,
            "Gold spent in events this run.");
        AppendStatRow(
            body,
            ["average", "floor", "gold_gained"],
            ["floor"],
            "Gold gained",
            Divide(goldStats.GoldAcquired, floors),
            "Average gold acquired per floor reached.");
        AppendStatRow(
            body,
            ["average", "combat", "gold_gained"],
            ["combat"],
            "Gold gained",
            Divide(goldStats.GoldGainedInCombats, goldStats.Combats),
            "Average gold gained per combat.");
        AppendStatRow(
            body,
            ["average", "unknown_room", "gold_gained"],
            ["unknown_room"],
            "Gold gained",
            Divide(goldStats.GoldGainedInEvents, goldStats.EventsVisited),
            "Average gold gained per event visited.");

        return body.ToString();
    }

    private static void AppendStatRow(
        StringBuilder body,
        IReadOnlyList<string> conceptIds,
        IReadOnlyList<string> denominatorConceptIds,
        string label,
        int value,
        string fullDescription)
        => AppendStatRow(
            body,
            conceptIds,
            denominatorConceptIds,
            label,
            value.ToString(CultureInfo.InvariantCulture),
            fullDescription);

    private static void AppendStatRow(
        StringBuilder body,
        IReadOnlyList<string> conceptIds,
        IReadOnlyList<string> denominatorConceptIds,
        string label,
        decimal value,
        string fullDescription)
        => AppendStatRow(
            body,
            conceptIds,
            denominatorConceptIds,
            label,
            FormatDecimal(value),
            fullDescription);

    private static void AppendStatRow(
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

    private static string FormatDecimal(decimal value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);
}
