using System;
using System.Collections.Generic;
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

        var history = RunTracker.GetEffectiveMaxHpHistory();
        if (history.Count == 0) return false;

        tip = StatsTooltip.CreateNativeTip(
            "Max HP history",
            BuildBodyBBCode(history),
            stretchHorizontally: true);
        return true;
    }

    internal static string BuildBodyBBCode(
        IEnumerable<MaxHpRunHistoryEntry>? history)
    {
        var floorIcon = StatConceptGlossary.RenderHintedGlyph("floor");
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
                body.Append(" · T").Append(entry.Turn.Value);
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
}
