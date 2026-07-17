using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Presentation-only filter for the standard in-run relic bar. Relics remain
/// owned and functional, and every non-bar surface continues to show them.
/// </summary>
[HarmonyPatch(typeof(NRelicInventoryHolder), nameof(NRelicInventoryHolder._Ready))]
public static class RelicBarFilterPatch
{
    [HarmonyPostfix]
    public static void Postfix(NRelicInventoryHolder __instance)
    {
        try { ApplyToHolder(__instance); }
        catch (Exception e) { CoreMain.Logger.Error($"RelicBarFilterPatch failed: {e.Message}"); }
    }

    public static void RefreshAll()
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null) return;
            RefreshRecursive(tree.Root);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"RelicBarFilter refresh failed: {e.Message}");
        }
    }

    internal static bool IsNonCombatRelic(RelicModel relicModel)
        => relicModel is LeesWaffle
            or Strawberry
            or Pear
            or NutritiousOyster
            or ChosenCheese
            or DarkstonePeriapt;

    private static void ApplyToHolder(NRelicInventoryHolder holder)
    {
        if (holder == null || !GodotObject.IsInstanceValid(holder)) return;
        var relicModel = holder.Relic?.Model;
        if (relicModel == null) return;

        holder.Visible = !ViewStatsInjectorPatch.HideNonCombatRelicStats
                         || !IsNonCombatRelic(relicModel);
    }

    private static void RefreshRecursive(Node node)
    {
        if (node is NRelicInventoryHolder holder)
            ApplyToHolder(holder);

        for (var i = 0; i < node.GetChildCount(); i++)
            RefreshRecursive(node.GetChild(i));
    }
}
