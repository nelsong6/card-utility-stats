using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Rooms;

namespace SpireLens.Core.Patches;

/// <summary>
/// Presentation-only filter for the standard in-run relic bar. Relics remain
/// owned and functional, and every non-bar surface continues to show them.
/// </summary>
[HarmonyPatch(typeof(NRelicInventoryHolder), nameof(NRelicInventoryHolder._Ready))]
public static class RelicBarFilterPatch
{
    private static bool _hooksInitialized;

    [HarmonyPostfix]
    public static void Postfix(NRelicInventoryHolder __instance)
    {
        try { ApplyToHolder(__instance); }
        catch (Exception e) { CoreMain.Logger.Error($"RelicBarFilterPatch failed: {e.Message}"); }
    }

    public static void InitializeHooks()
    {
        if (_hooksInitialized) return;

        CombatManager.Instance.CombatBegan += OnCombatBegan;
        CombatManager.Instance.CombatEnded += OnCombatEnded;
        _hooksInitialized = true;
        CoreMain.Logger.Info("Relic bar filter hooks wired (CombatBegan, CombatEnded).");
    }

    public static void TeardownHooks()
    {
        if (!_hooksInitialized) return;

        CombatManager.Instance.CombatBegan -= OnCombatBegan;
        CombatManager.Instance.CombatEnded -= OnCombatEnded;
        _hooksInitialized = false;
        CoreMain.Logger.Info("Relic bar filter hooks unwired.");
    }

    public static void RefreshAll(string reason = "requested")
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null) return;
            var shouldFilter = ShouldFilterNow();
            var holders = 0;
            var classified = 0;
            var hidden = 0;
            RefreshRecursive(tree.Root, shouldFilter, ref holders, ref classified, ref hidden);

            var capstone = NCapstoneContainer.Instance?.CurrentCapstoneScreen?.GetType().Name ?? "none";
            CoreMain.Logger.Info(
                $"Relic bar filter refresh ({reason}): combat={CombatManager.Instance?.IsInProgress == true}, " +
                $"capstone={capstone}, filter={shouldFilter}, holders={holders}, " +
                $"non_combat={classified}, hidden={hidden}.");
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
            or DarkstonePeriapt
            or LeadPaperweight
            or LargeCapsule
            or NeowsBones
            or RegalPillow
            or PotionBelt
            or MoltenEgg
            or ToxicEgg
            or FrozenEgg
            or GlassEye
            or PetrifiedToad;

    private static void ApplyToHolder(NRelicInventoryHolder holder)
    {
        if (holder == null || !GodotObject.IsInstanceValid(holder)) return;
        var relicModel = holder.Relic?.Model;
        if (relicModel == null) return;

        var shouldFilter = holder.GetTree() != null && ShouldFilterNow();
        holder.Visible = !shouldFilter || !IsNonCombatRelic(relicModel);
    }

    private static bool ShouldFilterNow()
    {
        if (ViewStatsInjectorPatch.HideNonCombatRelicStats) return true;
        if (!ViewStatsInjectorPatch.ShowCombatOnlyRelicsAtCombatScreen) return false;
        if (CombatManager.Instance?.IsInProgress != true) return false;
        return NCapstoneContainer.Instance?.CurrentCapstoneScreen is not NDeckViewScreen;
    }

    private static void RefreshRecursive(
        Node node,
        bool shouldFilter,
        ref int holders,
        ref int classified,
        ref int hidden)
    {
        if (node is NRelicInventoryHolder holder)
        {
            holders++;
            var relicModel = holder.Relic?.Model;
            if (relicModel != null)
            {
                var isNonCombat = IsNonCombatRelic(relicModel);
                if (isNonCombat) classified++;
                var shouldHide = shouldFilter && isNonCombat;
                holder.Visible = !shouldHide;
                if (shouldHide) hidden++;
            }
        }

        for (var i = 0; i < node.GetChildCount(); i++)
            RefreshRecursive(node.GetChild(i), shouldFilter, ref holders, ref classified, ref hidden);
    }

    private static void OnCombatBegan(CombatState _) => RefreshAll("combat began");

    private static void OnCombatEnded(CombatRoom _) => RefreshAll("combat ended");
}

[HarmonyPatch(typeof(NCapstoneContainer), nameof(NCapstoneContainer.Open))]
public static class RelicBarFilterCapstoneOpenPatch
{
    [HarmonyPostfix]
    public static void Postfix() => RelicBarFilterPatch.RefreshAll("capstone opened");
}

[HarmonyPatch(typeof(NCapstoneContainer), nameof(NCapstoneContainer.Close))]
public static class RelicBarFilterCapstoneClosePatch
{
    [HarmonyPostfix]
    public static void Postfix() => RelicBarFilterPatch.RefreshAll("capstone closed");
}
