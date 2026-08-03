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
        var body = new StringBuilder();

        AppendStatRow(body, goldGainedIcon, "Gold acquired", goldStats.GoldAcquired);
        AppendStatRow(body, goldIcon, "Gold spent", goldStats.GoldSpent);
        AppendStatRow(body, goldIcon, "Gold spent in shops", goldStats.GoldSpentInShops);
        AppendStatRow(
            body,
            $"{averageIcon} {goldIcon}",
            "Avg gold spent per shop",
            Divide(goldStats.GoldSpentInShops, goldStats.ShopsVisited));
        AppendStatRow(body, goldIcon, "Gold spent in events", goldStats.GoldSpentInEvents);
        AppendStatRow(
            body,
            $"{averageIcon} {floorIcon}",
            "Avg gold gained per floor",
            Divide(goldStats.GoldAcquired, floors));
        AppendStatRow(
            body,
            $"{averageIcon} {combatIcon}",
            "Avg gold gained per combat",
            Divide(goldStats.GoldGainedInCombats, goldStats.Combats));
        AppendStatRow(
            body,
            $"{averageIcon} {eventIcon}",
            "Avg gold gained per event",
            Divide(goldStats.GoldGainedInEvents, goldStats.EventsVisited));

        return body.ToString();
    }

    private static void AppendStatRow(
        StringBuilder body,
        string icon,
        string label,
        int value)
        => AppendStatRow(
            body,
            icon,
            label,
            value.ToString(CultureInfo.InvariantCulture));

    private static void AppendStatRow(
        StringBuilder body,
        string icon,
        string label,
        decimal value)
        => AppendStatRow(body, icon, label, FormatDecimal(value));

    private static void AppendStatRow(
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

    private static string FormatDecimal(decimal value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);
}
