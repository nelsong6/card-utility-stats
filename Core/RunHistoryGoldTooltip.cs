using System;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.Core.Runs;
using SpireLens.Core.Patches;

namespace SpireLens.Core;

/// <summary>
/// Adds the shared run-wide gold page to the stock gold value in a past-run
/// summary while leaving the game's label and layout intact.
/// </summary>
internal static class RunHistoryGoldTooltip
{
    private static MegaLabel? _label;
    private static RunGoldStats? _goldStats;
    private static int _floors;
    private static Control.MouseFilterEnum _originalMouseFilter;
    private static Control.CursorShape _originalCursorShape;
    private static Action? _showHandler;
    private static Action? _mouseExitHandler;
    private static Action? _focusExitHandler;

    public static void Refresh(NRunHistory runHistory, RunHistoryPlayer player)
    {
        Remove();

        var run = RunHistoryStatsContext.GetCurrentRunData();
        var label = runHistory?._goldLabel;
        if (!IsLive(runHistory)
            || player == null
            || run == null
            || !IsLive(label)
            || !RunHistoryHpTooltip.IsTrackedPlayer(run, player))
        {
            return;
        }

        _label = label;
        _goldStats = run.GoldStats ?? new RunGoldStats();
        _floors = Math.Max(0, run.FloorReached ?? 0);
        _originalMouseFilter = label!.MouseFilter;
        _originalCursorShape = label.MouseDefaultCursorShape;
        label.MouseFilter = Control.MouseFilterEnum.Stop;
        label.MouseDefaultCursorShape = Control.CursorShape.PointingHand;

        StatsTooltipPinManager.AttachRunHistoryGoldLabel(label);
        _showHandler = () => ShowTooltip(label);
        _mouseExitHandler = () => HideTooltipOnMouseExit(label);
        _focusExitHandler = () => HideTooltip(label);
        label.MouseEntered += _showHandler;
        label.FocusEntered += _showHandler;
        label.MouseExited += _mouseExitHandler;
        label.FocusExited += _focusExitHandler;
    }

    public static void Remove(NRunHistory? runHistory = null)
    {
        if (IsLive(_label))
        {
            NHoverTipSet.Remove(_label!);
            if (_showHandler != null)
            {
                _label!.MouseEntered -= _showHandler;
                _label.FocusEntered -= _showHandler;
            }

            if (_mouseExitHandler != null)
                _label!.MouseExited -= _mouseExitHandler;
            if (_focusExitHandler != null)
                _label!.FocusExited -= _focusExitHandler;

            _label!.MouseFilter = _originalMouseFilter;
            _label.MouseDefaultCursorShape = _originalCursorShape;
        }

        _label = null;
        _goldStats = null;
        _floors = 0;
        _showHandler = null;
        _mouseExitHandler = null;
        _focusExitHandler = null;
    }

    public static void Teardown() => Remove();

    public static void ReinjectIntoActiveRunHistory()
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            var runHistory = tree == null ? null : FindRunHistory(tree.Root);
            var player = runHistory?._selectedPlayerIcon?.Player;
            if (runHistory != null && player != null)
                Refresh(runHistory, player);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error(
                $"RunHistoryGoldTooltip hot-reload reinjection failed: {e}");
        }
    }

    internal static bool IsTarget(Control? target)
        => IsLive(target) && ReferenceEquals(target, _label);

    internal static bool TryBuildStatsTip(Control target, out HoverTip tip)
    {
        tip = default;
        if (!IsTarget(target) || _goldStats == null) return false;

        return GoldStatsTooltip.TryBuildNativeHoverTip(
            _goldStats,
            _floors,
            out tip);
    }

    internal static void ShowTooltip(Control target)
    {
        if (!IsTarget(target)
            || !ViewStatsInjectorPatch.StatsVisibilityEnabled
            || !TryBuildStatsTip(target, out var tip))
        {
            return;
        }

        NHoverTipSet.Remove(target);
        var tipSet = NHoverTipSet.CreateAndShow(
            target,
            tip,
            HoverTip.GetHoverTipAlignment(target));
        if (tipSet != null)
            NativeStatsHoverTipStyler.ApplyToLastTextTip(tipSet);
    }

    private static void HideTooltipOnMouseExit(Control target)
    {
        if (!IsTarget(target)) return;
        if (MegaCrit.Sts2.Core.Nodes.CommonUi.NControllerManager.Instance
                ?.IsUsingDirectionalNavigation == true
            && target.HasFocus())
        {
            return;
        }

        HideTooltip(target);
    }

    private static void HideTooltip(Control target)
    {
        if (IsLive(target))
            NHoverTipSet.Remove(target);
    }

    private static NRunHistory? FindRunHistory(Node node)
    {
        if (node is NRunHistory runHistory && IsLive(runHistory))
            return runHistory;

        foreach (var child in node.GetChildren())
        {
            var found = FindRunHistory(child);
            if (found != null) return found;
        }

        return null;
    }

    private static bool IsLive(GodotObject? value)
        => value != null && GodotObject.IsInstanceValid(value);
}
