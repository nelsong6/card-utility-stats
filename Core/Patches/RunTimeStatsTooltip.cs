using System.Text;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;

namespace SpireLens.Core.Patches;

internal static class RunTimeStatsTooltip
{
    internal static bool TryBuildLiveNativeHoverTip(out HoverTip tip)
    {
        RunTimeStatsTracker.SampleNow();
        if (!RunTracker.TryGetEffectiveRunTimeStats(out var stats))
        {
            tip = default;
            return false;
        }

        return TryBuildNativeHoverTip(stats, out tip);
    }

    internal static bool TryBuildNativeHoverTip(
        RunTimeStats? stats,
        out HoverTip tip)
    {
        tip = StatsTooltip.CreateNativeTip(
            "Run timer stats",
            BuildBodyBBCode(stats),
            stretchHorizontally: true);
        return true;
    }

    internal static string BuildBodyBBCode(RunTimeStats? stats)
    {
        stats ??= new RunTimeStats();
        var body = new StringBuilder();
        var average = Icon("average");
        var combat = Icon("combat");
        var turn = Icon("turn");

        AppendRow(
            body,
            combat,
            "Time spent in combats",
            Format(stats.CombatSeconds));
        AppendRow(
            body,
            $"{average} {turn}",
            "Avg time per turn in combat",
            FormatAverage(stats.CombatSeconds, stats.CombatTurns));
        AppendRow(
            body,
            $"{average} {combat}",
            "Avg time per combat",
            FormatAverage(stats.CombatSeconds, stats.Combats));
        AppendRow(
            body,
            Icon("offered"),
            "Time spent at reward screens",
            Format(stats.RewardScreenSeconds));
        AppendRow(
            body,
            Icon("unknown_room"),
            "Time spent in events",
            Format(stats.EventSeconds));
        AppendRow(
            body,
            Icon("floor"),
            "Time spent on the map",
            Format(stats.MapSeconds));

        return body.ToString();
    }

    private static string Icon(string concept)
        => StatConceptGlossary.RenderHintedGlyph(concept);

    private static string Format(long seconds)
        => TimeFormatting.Format((float)System.Math.Max(0L, seconds));

    private static string FormatAverage(long seconds, int samples)
        => TimeFormatting.Format(
            samples > 0
                ? (float)System.Math.Max(0L, seconds) / samples
                : 0f);

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
}
