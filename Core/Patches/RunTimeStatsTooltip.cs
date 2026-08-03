using System.Text;
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
            Format(stats.CombatSeconds),
            "Total time spent in combats this run.");
        AppendRow(
            body,
            $"{average} {turn}",
            "Avg time per turn in combat",
            FormatAverage(stats.CombatSeconds, stats.CombatTurns),
            "Average combat time per player turn.");
        AppendRow(
            body,
            $"{average} {combat}",
            "Avg time per combat",
            FormatAverage(stats.CombatSeconds, stats.Combats),
            "Average time spent per combat.");
        AppendRow(
            body,
            Icon("offered"),
            "Time spent at reward screens",
            Format(stats.RewardScreenSeconds),
            "Total time spent viewing reward screens this run.");
        AppendRow(
            body,
            Icon("unknown_room"),
            "Time spent in events",
            Format(stats.EventSeconds),
            "Total time spent in events this run.");
        AppendRow(
            body,
            Icon("floor"),
            "Time spent on the map",
            Format(stats.MapSeconds),
            "Total time spent viewing the map this run.");

        return body.ToString();
    }

    private static string Icon(string concept)
        => StatConceptGlossary.RenderHintedGlyph(concept);

    private static string Format(long seconds)
    {
        var nonNegativeSeconds = System.Math.Max(0L, seconds);
        var hours = nonNegativeSeconds / 3600L;
        var minutes = nonNegativeSeconds % 3600L / 60L;
        var remainingSeconds = nonNegativeSeconds % 60L;
        return hours > 0L
            ? $"{hours:00}:{minutes:00}:{remainingSeconds:00}"
            : $"{minutes:00}:{remainingSeconds:00}";
    }

    private static string FormatAverage(long seconds, int samples)
        => Format(samples > 0
            ? (long)System.Math.Round(
                (double)System.Math.Max(0L, seconds) / samples,
                MidpointRounding.AwayFromZero)
            : 0L);

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
}
