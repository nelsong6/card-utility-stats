using System;
using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.Core.Nodes.TopBar;
using SpireLens.Core.Patches;

namespace SpireLens.Core;

/// <summary>
/// Adds the same timer-stat page to the live top-bar timer and the stock run
/// time label in run history. Both targets keep native hover/focus behavior
/// and the shared right-click pin/image-copy affordance.
/// </summary>
internal static class RunTimerStatsTooltip
{
    private sealed class Binding
    {
        public required Control Target { get; init; }
        public RunTimeStats? HistoricalStats { get; init; }
        public required Control.MouseFilterEnum OriginalMouseFilter { get; init; }
        public required Control.CursorShape OriginalCursorShape { get; init; }
        public required Action ShowHandler { get; init; }
        public required Action MouseExitHandler { get; init; }
        public required Action FocusExitHandler { get; init; }
    }

    private static Binding? _liveBinding;
    private static Binding? _historyBinding;

    public static void Initialize() => EnsureLiveTarget();

    public static void Shutdown()
    {
        RemoveBinding(ref _historyBinding);
        RemoveBinding(ref _liveBinding);
    }

    internal static void EnsureLiveTarget()
    {
        if (IsLive(_liveBinding?.Target)) return;

        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree) return;
            var timer = FindDescendant<NRunTimer>(tree.Root);
            if (timer != null)
                Bind(ref _liveBinding, timer, null);
        }
        catch (Exception exception)
        {
            CoreMain.LogDebug(
                $"RunTimerStatsTooltip live injection failed: {exception.Message}");
        }
    }

    public static void RefreshRunHistory(NRunHistory runHistory)
    {
        RemoveRunHistory(runHistory);

        var run = RunHistoryStatsContext.GetCurrentRunData();
        var label = runHistory?._timeLabel;
        if (!IsLive(runHistory) || run == null || !IsLive(label)) return;

        Bind(
            ref _historyBinding,
            label!,
            Clone(run.TimeStats));
    }

    public static void RemoveRunHistory(NRunHistory? runHistory = null)
        => RemoveBinding(ref _historyBinding);

    public static void ReinjectIntoActiveRunHistory()
    {
        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree) return;
            var runHistory = FindDescendant<NRunHistory>(tree.Root);
            if (runHistory != null)
                RefreshRunHistory(runHistory);
        }
        catch (Exception exception)
        {
            CoreMain.Logger.Error(
                $"RunTimerStatsTooltip run-history reinjection failed: {exception}");
        }
    }

    internal static bool IsTarget(Control? target)
        => IsLive(target)
            && (ReferenceEquals(target, _liveBinding?.Target)
                || ReferenceEquals(target, _historyBinding?.Target));

    internal static bool TryBuildStatsTip(Control target, out HoverTip tip)
    {
        if (ReferenceEquals(target, _liveBinding?.Target))
            return RunTimeStatsTooltip.TryBuildLiveNativeHoverTip(out tip);

        if (ReferenceEquals(target, _historyBinding?.Target))
        {
            return RunTimeStatsTooltip.TryBuildNativeHoverTip(
                _historyBinding!.HistoricalStats,
                out tip);
        }

        tip = default;
        return false;
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

    private static void Bind(
        ref Binding? destination,
        Control target,
        RunTimeStats? historicalStats)
    {
        RemoveBinding(ref destination);

        var originalMouseFilter = target.MouseFilter;
        var originalCursorShape = target.MouseDefaultCursorShape;
        Action showHandler = () => ShowTooltip(target);
        Action mouseExitHandler = () => HideTooltipOnMouseExit(target);
        Action focusExitHandler = () => HideTooltip(target);
        destination = new Binding
        {
            Target = target,
            HistoricalStats = historicalStats,
            OriginalMouseFilter = originalMouseFilter,
            OriginalCursorShape = originalCursorShape,
            ShowHandler = showHandler,
            MouseExitHandler = mouseExitHandler,
            FocusExitHandler = focusExitHandler,
        };

        target.MouseFilter = Control.MouseFilterEnum.Stop;
        target.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        StatsTooltipPinManager.AttachRunTimerStatsTarget(target);
        target.MouseEntered += showHandler;
        target.FocusEntered += showHandler;
        target.MouseExited += mouseExitHandler;
        target.FocusExited += focusExitHandler;
    }

    private static void RemoveBinding(ref Binding? binding)
    {
        var current = binding;
        binding = null;
        if (current == null || !IsLive(current.Target)) return;

        NHoverTipSet.Remove(current.Target);
        current.Target.MouseEntered -= current.ShowHandler;
        current.Target.FocusEntered -= current.ShowHandler;
        current.Target.MouseExited -= current.MouseExitHandler;
        current.Target.FocusExited -= current.FocusExitHandler;
        current.Target.MouseFilter = current.OriginalMouseFilter;
        current.Target.MouseDefaultCursorShape = current.OriginalCursorShape;
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

    private static RunTimeStats Clone(RunTimeStats? source)
    {
        source ??= new RunTimeStats();
        return new RunTimeStats
        {
            CombatSeconds = source.CombatSeconds,
            RewardScreenSeconds = source.RewardScreenSeconds,
            EventSeconds = source.EventSeconds,
            MapSeconds = source.MapSeconds,
            Combats = source.Combats,
            CombatTurns = source.CombatTurns,
        };
    }

    private static T? FindDescendant<T>(Node node) where T : Node
    {
        if (node is T match && IsLive(match)) return match;

        foreach (var child in node.GetChildren())
        {
            var found = FindDescendant<T>(child);
            if (found != null) return found;
        }

        return null;
    }

    private static bool IsLive(GodotObject? value)
        => value != null && GodotObject.IsInstanceValid(value);
}
