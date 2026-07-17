using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens;

namespace SpireLens.Core.Patches;

/// <summary>
/// Presentation-only filter for the standard in-run relic bar. Relics remain
/// owned and functional, and every non-bar surface continues to show them.
/// </summary>
[HarmonyPatch(typeof(NRelicInventoryHolder), nameof(NRelicInventoryHolder._Ready))]
public static class RelicBarFilterPatch
{
    private static RelicBarFilterMonitor? _monitor;
    private static bool? _lastShouldFilter;

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
            var shouldFilter = ShouldFilterNow(tree.Root);
            _lastShouldFilter = shouldFilter;
            RefreshRecursive(tree.Root, shouldFilter);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"RelicBarFilter refresh failed: {e.Message}");
        }
    }

    public static void EnsureMonitor()
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null) return;
        if (_monitor != null && GodotObject.IsInstanceValid(_monitor)) return;

        _monitor = new RelicBarFilterMonitor { Name = "SpireLensRelicBarFilterMonitor" };
        tree.Root.AddChild(_monitor);
    }

    public static void DestroyMonitor()
    {
        if (_monitor != null && GodotObject.IsInstanceValid(_monitor))
            _monitor.QueueFree();
        _monitor = null;
        _lastShouldFilter = null;
    }

    internal static void RefreshIfContextChanged()
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null) return;
        var shouldFilter = ShouldFilterNow(tree.Root);
        if (_lastShouldFilter == shouldFilter) return;
        RefreshAll();
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

        var tree = holder.GetTree();
        var shouldFilter = tree != null && ShouldFilterNow(tree.Root);
        holder.Visible = !shouldFilter || !IsNonCombatRelic(relicModel);
    }

    private static bool ShouldFilterNow(Node root)
    {
        if (ViewStatsInjectorPatch.HideNonCombatRelicStats) return true;
        if (!ViewStatsInjectorPatch.ShowCombatOnlyRelicsAtCombatScreen) return false;
        if (CombatManager.Instance?.IsInProgress != true) return false;
        return !HasVisibleDeckView(root);
    }

    private static bool HasVisibleDeckView(Node node)
    {
        if (node is NDeckViewScreen deckView && deckView.IsVisibleInTree())
            return true;

        for (var i = 0; i < node.GetChildCount(); i++)
        {
            if (HasVisibleDeckView(node.GetChild(i))) return true;
        }
        return false;
    }

    private static void RefreshRecursive(Node node, bool shouldFilter)
    {
        if (node is NRelicInventoryHolder holder)
        {
            var relicModel = holder.Relic?.Model;
            if (relicModel != null)
                holder.Visible = !shouldFilter || !IsNonCombatRelic(relicModel);
        }

        for (var i = 0; i < node.GetChildCount(); i++)
            RefreshRecursive(node.GetChild(i), shouldFilter);
    }
}

public partial class RelicBarFilterMonitor : Node
{
    public override void _Process(double delta)
    {
        RelicBarFilterPatch.RefreshIfContextChanged();
    }
}
