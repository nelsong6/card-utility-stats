using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MegaCrit.sts2.Core.Nodes.TopBar;
using MegaCrit.Sts2.Core.HoverTips;

namespace SpireLens.Core.Patches;

internal static class MaxHpHistoryTooltip
{
    internal static bool TryBuildNativeHoverTip(
        NTopBarHp owner,
        out HoverTip tip)
    {
        tip = default;
        if (owner == null) return false;

        if (!RunTracker.TryGetEffectiveHealthStats(
                out var healthStats,
                out var floors))
        {
            return false;
        }

        var history = RunTracker.GetEffectiveMaxHpHistory();

        return TryBuildNativeHoverTip(
            healthStats,
            floors,
            history,
            out tip);
    }

    internal static bool TryBuildNativeHoverTip(
        RunHealthStats? healthStats,
        int floors,
        IEnumerable<MaxHpRunHistoryEntry>? history,
        out HoverTip tip)
    {
        tip = StatsTooltip.CreateNativeTip(
            "HP stats",
            BuildBodyBBCode(healthStats, floors, history),
            stretchHorizontally: true);
        return true;
    }

    internal static string BuildBodyBBCode(
        RunHealthStats? healthStats,
        int floors,
        IEnumerable<MaxHpRunHistoryEntry>? history)
    {
        healthStats ??= new RunHealthStats();
        var orderedHistory = (history ?? Array.Empty<MaxHpRunHistoryEntry>())
            .Where(entry => entry != null && entry.PreviousMaxHp != entry.NewMaxHp)
            .OrderBy(entry => entry.Sequence)
            .ToArray();
        var totalHpLost = healthStats.HpLostInCombats + healthStats.HpLostInEvents;
        var avgPerFloor = floors > 0 ? totalHpLost / floors : 0m;
        var avgPerCombat = healthStats.Combats > 0
            ? healthStats.HpLostInCombats / healthStats.Combats
            : 0m;
        var maxHpGained = orderedHistory.Sum(entry =>
            Math.Max(0, entry.NewMaxHp - entry.PreviousMaxHp));
        var maxHpLost = orderedHistory.Sum(entry =>
            Math.Max(0, entry.PreviousMaxHp - entry.NewMaxHp));
        var body = new StringBuilder();

        AppendStatRow(
            body,
            ["damage", "in", "all", "combat"],
            [],
            "HP lost in combats",
            FormatDecimal(healthStats.HpLostInCombats),
            "HP lost to combat damage this run.");
        AppendStatRow(
            body,
            ["damage", "in", "all", "unknown_room"],
            [],
            "HP lost in events",
            FormatDecimal(healthStats.HpLostInEvents),
            "HP lost from events this run.");
        AppendStatRow(
            body,
            ["average", "damage", "floor"],
            ["floor"],
            "Avg HP lost per floor",
            FormatDecimal(avgPerFloor),
            "Average total HP lost per floor reached.");
        AppendStatRow(
            body,
            ["average", "damage", "combat"],
            ["combat"],
            "Avg HP lost per combat",
            FormatDecimal(avgPerCombat),
            "Average HP lost to combat damage per combat.");
        AppendStatRow(
            body,
            ["max_hp_gained"],
            [],
            "Max HP gained",
            maxHpGained.ToString(CultureInfo.InvariantCulture),
            "Total maximum HP gained this run.");
        AppendStatRow(
            body,
            ["max_hp"],
            [],
            "Max HP lost",
            maxHpLost.ToString(CultureInfo.InvariantCulture),
            "Total maximum HP lost this run.");

        if (orderedHistory.Length > 0)
        {
            body.Append("\n[b]Max HP changes[/b]\n")
                .Append(BuildBodyBBCode(orderedHistory));
        }

        return body.ToString();
    }

    internal static string BuildBodyBBCode(
        IEnumerable<MaxHpRunHistoryEntry>? history)
    {
        var floorIcon = StatConceptGlossary.RenderHintedGlyph("floor");
        var turnIcon = StatConceptGlossary.RenderHintedGlyph("turn");
        var body = new StringBuilder();

        foreach (var entry in (history ?? Array.Empty<MaxHpRunHistoryEntry>())
                     .OrderBy(item => item.Sequence))
        {
            if (entry == null || entry.PreviousMaxHp == entry.NewMaxHp) continue;
            if (body.Length > 0) body.Append('\n');

            var source = !string.IsNullOrWhiteSpace(entry.SourceName)
                ? entry.SourceName
                : !string.IsNullOrWhiteSpace(entry.LocationName)
                    ? entry.LocationName
                    : !string.IsNullOrWhiteSpace(entry.LocationKind)
                        ? entry.LocationKind
                        : "Unknown";
            var delta = entry.NewMaxHp - entry.PreviousMaxHp;

            body.Append(floorIcon)
                .Append(' ')
                .Append(Math.Max(0, entry.Floor));
            if (entry.Turn is > 0)
                body.Append(" · ").Append(turnIcon).Append(' ').Append(entry.Turn.Value);
            body.Append("   ")
                .Append(StatsTooltip.EscapeBbcode(source))
                .Append("   [b]")
                .Append(delta > 0 ? "+" : "")
                .Append(delta)
                .Append("[/b]   ")
                .Append(entry.PreviousMaxHp)
                .Append(" → ")
                .Append(entry.NewMaxHp);
        }

        return body.ToString();
    }

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

    private static string FormatDecimal(decimal value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);
}
