using System.Globalization;
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
        var goldIcon = StatConceptGlossary.RenderHintedGlyph("gold");
        var goldGainedIcon = StatConceptGlossary.RenderHintedGlyph("gold_gained");
        var averageIcon = StatConceptGlossary.RenderHintedGlyph("average");
        var floorIcon = StatConceptGlossary.RenderHintedGlyph("floor");
        var combatIcon = StatConceptGlossary.RenderHintedGlyph("combat");
        var eventIcon = StatConceptGlossary.RenderHintedGlyph("unknown_room");
        var shopIcon = StatConceptGlossary.RenderHintedGlyph("shop");
        var body = new StringBuilder();

        AppendStatRow(
            body,
            goldGainedIcon,
            "Acquired",
            goldStats.GoldAcquired,
            "All gold acquired this run.");
        AppendStatRow(
            body,
            goldIcon,
            "Spent",
            goldStats.GoldSpent,
            "All gold spent this run.");
        AppendStatRow(
            body,
            $"{shopIcon} {goldIcon}",
            "Spent",
            goldStats.GoldSpentInShops,
            "Gold spent in shops this run.");
        AppendStatRow(
            body,
            $"{averageIcon} {shopIcon} {goldIcon}",
            "Spent",
            Divide(goldStats.GoldSpentInShops, goldStats.ShopsVisited),
            "Average gold spent per shop visited.");
        AppendStatRow(
            body,
            $"{eventIcon} {goldIcon}",
            "Spent",
            goldStats.GoldSpentInEvents,
            "Gold spent in events this run.");
        AppendStatRow(
            body,
            $"{averageIcon} {floorIcon} {goldGainedIcon}",
            "Gained",
            Divide(goldStats.GoldAcquired, floors),
            "Average gold acquired per floor reached.");
        AppendStatRow(
            body,
            $"{averageIcon} {combatIcon} {goldGainedIcon}",
            "Gained",
            Divide(goldStats.GoldGainedInCombats, goldStats.Combats),
            "Average gold gained per combat.");
        AppendStatRow(
            body,
            $"{averageIcon} {eventIcon} {goldGainedIcon}",
            "Gained",
            Divide(goldStats.GoldGainedInEvents, goldStats.EventsVisited),
            "Average gold gained per event visited.");

        return body.ToString();
    }

    private static void AppendStatRow(
        StringBuilder body,
        string icon,
        string label,
        int value,
        string fullDescription)
        => AppendStatRow(
            body,
            icon,
            label,
            value.ToString(CultureInfo.InvariantCulture),
            fullDescription);

    private static void AppendStatRow(
        StringBuilder body,
        string icon,
        string label,
        decimal value,
        string fullDescription)
        => AppendStatRow(body, icon, label, FormatDecimal(value), fullDescription);

    private static void AppendStatRow(
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

    private static string FormatDecimal(decimal value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);
}
