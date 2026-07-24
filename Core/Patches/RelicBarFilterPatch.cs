using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

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
            var currentTurn = GetCurrentPlayerTurnNumber();
            var holders = 0;
            var classified = 0;
            var hidden = 0;
            var focusedRelic = tree.Root.GetViewport()?.GuiGetFocusOwner()
                as NRelicInventoryHolder;
            RefreshRecursive(
                tree.Root,
                shouldFilter,
                currentTurn,
                ref holders,
                ref classified,
                ref hidden);
            RefreshVisibleNavigation(focusedRelic);

            var capstone = NCapstoneContainer.Instance?.CurrentCapstoneScreen?.GetType().Name ?? "none";
            CoreMain.Logger.Info(
                $"Relic bar filter refresh ({reason}): combat={CombatManager.Instance?.IsInProgress == true}, " +
                $"turn={currentTurn}, capstone={capstone}, filter={shouldFilter}, holders={holders}, " +
                $"not_relevant={classified}, hidden={hidden}.");
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"RelicBarFilter refresh failed: {e.Message}");
        }
    }

    internal static bool IsNonCombatRelic(RelicModel relicModel)
        => RelicClassificationStore.IsNonCombat(relicModel);

    internal static bool IsCombatRelevantNow(RelicModel relicModel, int currentTurn)
    {
        if (IsNonCombatRelic(relicModel)) return false;

        var relevantUntilTurn = RelicClassificationStore.GetCombatRelevantUntilTurn(relicModel);
        return IsBeforeCombatRelevanceCutoff(currentTurn, relevantUntilTurn);
    }

    internal static bool IsBeforeCombatRelevanceCutoff(int currentTurn, int? hiddenStartingTurn)
        => !hiddenStartingTurn.HasValue
           || currentTurn <= 0
           || currentTurn < hiddenStartingTurn.Value;

    internal static int FindNearestVisibleIndex(
        IReadOnlyList<bool> visible,
        int sourceIndex)
    {
        if (visible == null || visible.Count == 0) return -1;
        if (sourceIndex < 0 || sourceIndex >= visible.Count)
            return visible.ToList().FindIndex(value => value);

        for (var distance = 1; distance < visible.Count; distance++)
        {
            var rightIndex = (sourceIndex + distance) % visible.Count;
            if (visible[rightIndex]) return rightIndex;

            var leftIndex = (sourceIndex - distance + visible.Count) % visible.Count;
            if (visible[leftIndex]) return leftIndex;
        }

        return visible[sourceIndex] ? sourceIndex : -1;
    }

    internal static void RefreshVisibleNavigation(
        NRelicInventoryHolder? focusedRelicBeforeRefresh = null)
    {
        try
        {
            var run = NRun.Instance;
            var globalUi = run?.GlobalUi;
            var relicInventory = globalUi?.RelicInventory;
            var topBar = globalUi?.TopBar;
            if (relicInventory == null
                || topBar == null
                || !GodotObject.IsInstanceValid(relicInventory)
                || !GodotObject.IsInstanceValid(topBar))
            {
                return;
            }

            var relicNodes = relicInventory.RelicNodes
                .Where(node => node != null && GodotObject.IsInstanceValid(node))
                .ToArray();
            var visibleRelics = relicNodes
                .Where(node => node.Visible)
                .ToArray();

            var firstVisibleRelic = visibleRelics.FirstOrDefault();
            var activeScreenProxy = topBar.ActiveScreenProxy;
            var downTarget = (Control?)firstVisibleRelic;
            if (downTarget == null
                && activeScreenProxy != null
                && GodotObject.IsInstanceValid(activeScreenProxy))
            {
                downTarget = activeScreenProxy;
            }

            if (downTarget != null)
            {
                var downPath = downTarget.GetPath();
                foreach (var control in new Control?[]
                         {
                             topBar.Gold,
                             topBar.Hp,
                             topBar.FloorIcon,
                             topBar.RoomIcon,
                             topBar.BossIcon,
                         })
                {
                    if (control != null && GodotObject.IsInstanceValid(control))
                        control.FocusNeighborBottom = downPath;
                }

                SetPotionDownNeighbors(topBar.PotionContainer, downPath);
            }

            var firstPotionControl = topBar.PotionContainer?.FirstPotionControl;
            for (var i = 0; i < visibleRelics.Length; i++)
            {
                var relic = visibleRelics[i];
                var left = visibleRelics[(i - 1 + visibleRelics.Length) % visibleRelics.Length];
                var right = visibleRelics[(i + 1) % visibleRelics.Length];
                relic.FocusNeighborLeft = left.GetPath();
                relic.FocusNeighborRight = right.GetPath();
                relic.FocusNeighborTop =
                    firstPotionControl != null && GodotObject.IsInstanceValid(firstPotionControl)
                        ? firstPotionControl.GetPath()
                        : relic.GetPath();
            }

            RepairHiddenRelicFocus(
                relicNodes,
                focusedRelicBeforeRefresh
                ?? relicInventory.GetViewport()?.GuiGetFocusOwner() as NRelicInventoryHolder,
                activeScreenProxy);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"Relic bar navigation refresh failed: {e.Message}");
        }
    }

    private static void ApplyToHolder(NRelicInventoryHolder holder)
    {
        if (holder == null || !GodotObject.IsInstanceValid(holder)) return;
        var relicModel = holder.Relic?.Model;
        if (relicModel == null) return;

        var shouldFilter = holder.GetTree() != null && ShouldFilterNow();
        holder.Visible = !shouldFilter
                         || IsCombatRelevantNow(relicModel, GetCurrentPlayerTurnNumber());
    }

    private static void SetPotionDownNeighbors(Node? node, NodePath downPath)
    {
        if (node == null || !GodotObject.IsInstanceValid(node)) return;

        if (node is NPotionHolder holder)
            holder.FocusNeighborBottom = downPath;

        for (var i = 0; i < node.GetChildCount(); i++)
            SetPotionDownNeighbors(node.GetChild(i), downPath);
    }

    private static void RepairHiddenRelicFocus(
        IReadOnlyList<NRelicInventoryHolder> relicNodes,
        NRelicInventoryHolder? focusedRelic,
        Control? activeScreenProxy)
    {
        if (focusedRelic == null
            || !GodotObject.IsInstanceValid(focusedRelic)
            || focusedRelic.Visible)
        {
            return;
        }

        var sourceIndex = -1;
        var visibility = new bool[relicNodes.Count];
        for (var i = 0; i < relicNodes.Count; i++)
        {
            visibility[i] = relicNodes[i].Visible;
            if (ReferenceEquals(relicNodes[i], focusedRelic))
                sourceIndex = i;
        }

        if (sourceIndex < 0) return;

        var targetIndex = FindNearestVisibleIndex(visibility, sourceIndex);
        if (targetIndex >= 0)
        {
            relicNodes[targetIndex].GrabFocus();
            return;
        }

        if (activeScreenProxy != null && GodotObject.IsInstanceValid(activeScreenProxy))
            activeScreenProxy.GrabFocus();
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
        int currentTurn,
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
                var isRelevant = IsCombatRelevantNow(relicModel, currentTurn);
                if (!isRelevant) classified++;
                var shouldHide = shouldFilter && !isRelevant;
                holder.Visible = !shouldHide;
                if (shouldHide) hidden++;
            }
        }

        for (var i = 0; i < node.GetChildCount(); i++)
        {
            RefreshRecursive(
                node.GetChild(i),
                shouldFilter,
                currentTurn,
                ref holders,
                ref classified,
                ref hidden);
        }
    }

    private static int GetCurrentPlayerTurnNumber()
    {
        if (CombatManager.Instance?.IsInProgress != true) return 0;

        var players = RunManager.Instance?.State?.Players;
        if (players == null || players.Count == 0) return 0;
        return players.Max(player => player.PlayerCombatState?.TurnNumber ?? 0);
    }

    private static void OnCombatBegan(CombatState _) => RefreshAll("combat began");

    private static void OnCombatEnded(CombatRoom _) => RefreshAll("combat ended");
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
public static class RelicBarFilterAfterPlayerTurnStartPatch
{
    [HarmonyPostfix]
    public static void Postfix() => RelicBarFilterPatch.RefreshAll("player turn started");
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

/// <summary>
/// The game's three navigation passes always target the first owned relic and
/// include every owned relic in the horizontal cycle. Reapply SpireLens's
/// visible-only projection after any of those passes overwrite it.
/// </summary>
[HarmonyPatch(typeof(NRelicInventory), "UpdateNavigation")]
public static class RelicInventoryVisibleNavigationPatch
{
    [HarmonyPostfix]
    public static void Postfix() => RelicBarFilterPatch.RefreshVisibleNavigation();
}

[HarmonyPatch(typeof(NPotionContainer), "UpdateNavigation")]
public static class PotionContainerVisibleRelicNavigationPatch
{
    [HarmonyPostfix]
    public static void Postfix() => RelicBarFilterPatch.RefreshVisibleNavigation();
}

[HarmonyPatch(typeof(NTopBar), "UpdateNavigation")]
public static class TopBarVisibleRelicNavigationPatch
{
    [HarmonyPostfix]
    public static void Postfix() => RelicBarFilterPatch.RefreshVisibleNavigation();
}
