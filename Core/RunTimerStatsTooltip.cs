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
    private const float HorizontalClearance = 20f;
    private const float ViewportMargin = 8f;

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
    private static NHoverTipSet? _visibleLiveTipSet;
    private static RichTextLabel? _visibleLiveDescription;

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

        if (ReferenceEquals(target, _liveBinding?.Target))
            ClearVisibleLiveTooltip();

        NHoverTipSet.Remove(target);
        var tipSet = NHoverTipSet.CreateAndShow(
            target,
            tip,
            GetNonObscuringAlignment(target));
        if (tipSet != null)
        {
            NativeStatsHoverTipStyler.ApplyToLastTextTip(tipSet);
            AlignClearOfTarget(target, tipSet);

            if (ReferenceEquals(target, _liveBinding?.Target))
            {
                _visibleLiveTipSet = tipSet;
                _visibleLiveDescription =
                    NativeStatsHoverTipStyler.GetLastStatsDescription(tipSet);
            }
        }
    }

    /// <summary>
    /// Updates an already-visible live timer page in place. Historical timer
    /// pages remain snapshots. Replacing the label text lets Godot handle the
    /// redraw and preserves the native hover/pin lifecycle.
    /// </summary>
    internal static void RefreshVisibleLiveTooltip()
    {
        try
        {
            var target = _liveBinding?.Target;
            if (!IsLive(target)
                || !RunTracker.TryGetEffectiveRunTimeStats(out var stats))
            {
                return;
            }

            var body = RunTimeStatsTooltip.BuildBodyBBCode(stats);
            if (IsVisible(_visibleLiveTipSet, _visibleLiveDescription))
            {
                if (!string.Equals(
                        _visibleLiveDescription!.Text,
                        body,
                        StringComparison.Ordinal))
                {
                    _visibleLiveDescription.Text = body;
                    AlignClearOfTarget(target!, _visibleLiveTipSet!);
                }
            }
            else
            {
                ClearVisibleLiveTooltip();
            }

            StatsTooltipPinManager.RefreshPinnedRunTimerStats(target!, body);
        }
        catch (Exception exception)
        {
            CoreMain.LogDebug(
                $"RunTimerStatsTooltip live refresh failed: {exception.Message}");
        }
    }

    internal static HoverTipAlignment GetNonObscuringAlignment(Control target)
    {
        var targetRect = target.GetGlobalRect();
        var viewportRect = target.GetViewport()?.GetVisibleRect() ?? default;
        if (viewportRect.Size.X <= 0f)
            return HoverTipAlignment.Left;

        var targetCenterX = targetRect.Position.X + targetRect.Size.X / 2f;
        var viewportCenterX = viewportRect.Position.X + viewportRect.Size.X / 2f;
        return targetCenterX >= viewportCenterX
            ? HoverTipAlignment.Left
            : HoverTipAlignment.Right;
    }

    /// <summary>
    /// Native left/right alignment touches the tooltip edge directly to the
    /// owner's anchor. Timer glyph outlines extend to that edge, so enforce a
    /// real gap after the tooltip has measured its rendered width. Repeat on
    /// the deferred layout pass because pinning adds the camera control after
    /// the native hover-tip set is first constructed.
    /// </summary>
    internal static void AlignClearOfTarget(
        Control target,
        NHoverTipSet tipSet)
    {
        ApplyClearance(target, tipSet);
        Callable.From(() =>
        {
            if (IsTarget(target) && IsLive(tipSet))
                ApplyClearance(target, tipSet);
        }).CallDeferred();
    }

    private static void ApplyClearance(
        Control target,
        NHoverTipSet tipSet)
    {
        if (!IsLive(target)
            || !IsLive(tipSet)
            || !IsLive(tipSet._textHoverTipContainer))
        {
            return;
        }

        var alignment = GetNonObscuringAlignment(target);
        tipSet.SetAlignment(target, alignment);

        var container = tipSet._textHoverTipContainer;
        var width = container.Size.X;
        if (width <= 0f) return;

        var viewportRect = target.GetViewport()?.GetVisibleRect() ?? default;
        if (viewportRect.Size.X <= 0f) return;

        var x = GetClearTooltipX(
            target.GetGlobalRect(),
            width,
            alignment,
            viewportRect);
        container.GlobalPosition = new Vector2(x, container.GlobalPosition.Y);
    }

    internal static float GetClearTooltipX(
        Rect2 targetRect,
        float tooltipWidth,
        HoverTipAlignment alignment,
        Rect2 viewportRect)
    {
        var desiredX = alignment == HoverTipAlignment.Right
            ? targetRect.Position.X + targetRect.Size.X + HorizontalClearance
            : targetRect.Position.X - HorizontalClearance - tooltipWidth;
        var minimumX = viewportRect.Position.X + ViewportMargin;
        var maximumX = Math.Max(
            minimumX,
            viewportRect.Position.X
                + viewportRect.Size.X
                - ViewportMargin
                - tooltipWidth);
        return Math.Clamp(desiredX, minimumX, maximumX);
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
        var removesLiveBinding = ReferenceEquals(current, _liveBinding);
        binding = null;
        if (removesLiveBinding)
            ClearVisibleLiveTooltip();
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
        if (ReferenceEquals(target, _liveBinding?.Target))
            ClearVisibleLiveTooltip();

        if (IsLive(target))
            NHoverTipSet.Remove(target);
    }

    private static bool IsVisible(
        NHoverTipSet? tipSet,
        RichTextLabel? description)
    {
        return IsLive(tipSet)
            && !tipSet!.IsQueuedForDeletion()
            && IsLive(description)
            && !description!.IsQueuedForDeletion()
            && description.IsVisibleInTree();
    }

    private static void ClearVisibleLiveTooltip()
    {
        _visibleLiveTipSet = null;
        _visibleLiveDescription = null;
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
