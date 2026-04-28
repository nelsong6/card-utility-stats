using System;
using System.Text;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Shows per-relic SpireLens stats below the game's relic hover tooltip
/// when the player hovers a relic in the inventory bar.
/// </summary>
[HarmonyPatch(typeof(NRelicInventoryHolder), "OnFocus")]
public static class RelicHoverShowPatch
{
    private const string VulnerableIconPath = "res://images/atlases/power_atlas.sprites/vulnerable_power.tres";
    private const int InlineIconSize = 16;

    [HarmonyPostfix]
    public static void Postfix(NRelicInventoryHolder __instance)
    {
        try
        {
            var tickbox = ViewStatsInjectorPatch.LastInjectedTickbox;
            var viewStatsEnabled = tickbox?.IsTicked ?? RuntimeOptionsProvider.Current.ViewStatsToggleEnabled;
            if (!viewStatsEnabled) return;

            var relicNode = __instance.Relic;
            if (relicNode?.Model is not BagOfMarbles) return;

            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null) return;

            const string relicId = "RELIC.BAG_OF_MARBLES";
            var agg = RunTracker.GetRelicAggregate(relicId);
            if (agg == null || (agg.EnemiesAffected == 0 && agg.VulnerableApplied == 0)) return;

            var body = BuildBodyBBCode(agg);
            StatsTooltip.Show(tree, __instance, "Bag of Marbles", "SpireLens", body);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"RelicHoverShowPatch failed: {e.Message}");
        }
    }

    private static string BuildBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, VulnerableLabel("enemies affected"), agg.EnemiesAffected.ToString(), "");
        return sb.ToString();
    }

    private static string VulnerableLabel(string suffix)
    {
        var path = NormalizeResourcePath(VulnerableIconPath);
        return $"[img={InlineIconSize}x{InlineIconSize}]{path}[/img] {suffix}";
    }

    private static string NormalizeResourcePath(string path)
    {
        return path.StartsWith("res://", StringComparison.Ordinal)
            ? path
            : $"res://{path.TrimStart('/')}";
    }

    private static void Row3(StringBuilder sb, string label, string value, string pct)
    {
        sb.Append("[table=3]");
        sb.Append($"[cell expand=4 padding=0,0,12,0][color=#e0e0e0]{label}[/color][/cell]");
        sb.Append($"[cell expand=1 padding=0,0,12,0][right][b]{value}[/b][/right][/cell]");
        sb.Append($"[cell expand=1 padding=0,0,4,0][right][color=#b5b5b5]{pct}[/color][/right][/cell]");
        sb.Append("[/table]\n");
    }
}

[HarmonyPatch(typeof(NRelicInventoryHolder), "OnUnfocus")]
public static class RelicHoverHidePatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        try { StatsTooltip.Hide(); }
        catch (Exception e) { CoreMain.Logger.Error($"RelicHoverHidePatch failed: {e.Message}"); }
    }
}
