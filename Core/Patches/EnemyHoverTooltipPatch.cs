using System;
using System.Text;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace SpireLens.Core.Patches;

[HarmonyPatch(typeof(NCreature), nameof(NCreature.OnFocus))]
public static class EnemyHoverShowPatch
{
    [HarmonyPostfix]
    public static void Postfix(NCreature __instance)
    {
        try
        {
            var tickbox = ViewStatsInjectorPatch.LastInjectedTickbox;
            var viewStatsEnabled = tickbox?.IsTicked ?? RuntimeOptionsProvider.Current.ViewStatsToggleEnabled;
            if (!viewStatsEnabled) return;

            var creature = __instance.Entity;
            if (creature?.Monster == null || creature.IsPlayer) return;

            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null) return;

            var agg = RunTracker.GetEnemyAggregate(creature) ?? new EnemyAggregate
            {
                EnemyId = creature.Monster.Id.ToString(),
                DisplayName = GetEnemyDisplayName(creature),
            };

            var body = BuildEnemyDamageBodyBBCode(agg);
            StatsTooltip.Show(tree, __instance, GetEnemyDisplayName(creature), "SpireLens", body);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"EnemyHoverShowPatch failed: {e.Message}");
        }
    }

    private static string BuildEnemyDamageBodyBBCode(EnemyAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Damage attempted", agg.DamageAttempted.ToString(), "");
        Row3(sb, "Damage dealt", agg.DamageDealt.ToString(), "");
        Row3(sb, "Damage blocked", agg.DamageBlocked.ToString(), "");
        return sb.ToString();
    }

    private static string GetEnemyDisplayName(MegaCrit.Sts2.Core.Entities.Creatures.Creature creature)
    {
        return creature.Monster?.Id.ToString() ?? "Enemy";
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

[HarmonyPatch(typeof(NCreature), nameof(NCreature.OnUnfocus))]
public static class EnemyHoverHidePatch
{
    [HarmonyPostfix]
    public static void Postfix(NCreature __instance)
    {
        try
        {
            var creature = __instance.Entity;
            if (creature?.Monster == null || creature.IsPlayer) return;

            StatsTooltip.Hide();
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"EnemyHoverHidePatch failed: {e.Message}");
        }
    }
}
