using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.RelicCollection;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;

namespace SpireLens.Core.Patches;

/// <summary>
/// Pins one card, relic, or run-history tooltip set at a time. The
/// pinned set uses a dedicated native owner so the game's ordinary
/// OnUnfocus/Remove lifecycle
/// can run unchanged without dismissing it.
/// </summary>
internal static class StatsTooltipPinManager
{
    private const string PinOwnerNodeName = "SpireLensPinnedStatsTooltipOwner";
    private const string LockIconNodeName = "SpireLensStatsTooltipLock";
    private const string LockIconPath =
        "res://images/ui/top_panel/reminder_lock.png";
    private const string HintOwnerNodeName = "SpireLensPinnedStatsHintOwner";
    private const string CopyImageButtonNodeName = "SpireLensCopyStatsImageButton";
    private const string CopyImageButtonText = "Copy";
    private const float CopyFeedbackDurationSeconds = 1.25f;
    private const float LockIconWidth = 24f;
    private const float LockIconHeight = 28f;
    private const float LockIconRightInset = 3f;
    private const float LockIconTopInset = 2f;
    private const float CardLockIconWidth = 48f;
    private const float CardLockIconHeight = 56f;
    private const float CardLockIconRightInset = 6f;
    private const float CardLockIconTopInset = 4f;

    private sealed class TargetSubscription
    {
        public TargetSubscription(
            Control target,
            Control.GuiInputEventHandler? guiInputHandler,
            Action treeExitingHandler)
        {
            Target = target;
            GuiInputHandler = guiInputHandler;
            TreeExitingHandler = treeExitingHandler;
        }

        public Control Target { get; }
        public Control.GuiInputEventHandler? GuiInputHandler { get; }
        public Action TreeExitingHandler { get; }
    }

    private sealed class RunHistoryContainerSubscription
    {
        public RunHistoryContainerSubscription(
            Control container,
            Node.ChildEnteredTreeEventHandler childEnteredTreeHandler,
            Action treeExitingHandler)
        {
            Container = container;
            ChildEnteredTreeHandler = childEnteredTreeHandler;
            TreeExitingHandler = treeExitingHandler;
        }

        public Control Container { get; }
        public Node.ChildEnteredTreeEventHandler ChildEnteredTreeHandler { get; }
        public Action TreeExitingHandler { get; }
    }

    private static readonly Dictionary<ulong, TargetSubscription> Subscriptions = new();
    private static readonly Dictionary<ulong, RunHistoryContainerSubscription>
        RunHistoryContainerSubscriptions = new();

    private static Control? _pinnedTarget;
    private static Control? _pinOwner;
    private static NHoverTipSet? _pinnedTipSet;
    private static Control? _pinnedStatsControl;
    private static RichTextLabel? _pinnedStatsDescription;
    private static Button? _copyImageButton;
    private static Action? _copyImageButtonHandler;
    private static int _copyImageGeneration;
    private static bool _copyImageInProgress;
    private static Control? _hintOwner;
    private static Control? _lockIconHost;
    private static string? _visibleHintText;
    private static object? _pinnedCardModel;
    private static Texture2D? _lockTexture;
    private static bool _lockLoadAttempted;
    private static bool _suppressRightPressUntilRelease;

    public static void Attach(NRelicInventoryHolder? holder)
    {
        if (holder != null)
            AttachTarget(holder, subscribeToGuiInput: true);
    }

    public static void Attach(NRelicCollectionEntry? entry)
    {
        if (entry != null)
            AttachTarget(entry, subscribeToGuiInput: true);
    }

    public static void Attach(RunHistoryCampfireButton? button)
    {
        if (button != null)
            AttachTarget(button, subscribeToGuiInput: true);
    }

    public static void AttachRunHistoryHpLabel(Control? label)
    {
        if (label != null)
            AttachTarget(label, subscribeToGuiInput: true);
    }

    public static void AttachRunHistoryGoldLabel(Control? label)
    {
        if (label != null)
            AttachTarget(label, subscribeToGuiInput: true);
    }

    public static void AttachRunTimerStatsTarget(Control? target)
    {
        if (target != null)
            AttachTarget(target, subscribeToGuiInput: true);
    }

    public static void AttachRunHistoryTargets(NRunHistory? runHistory)
    {
        if (!IsLive(runHistory)) return;

        AttachRunHistoryDescendants(runHistory!);

        var deckHistory = FindDescendant<NDeckHistory>(runHistory!);
        var relicHistory = FindDescendant<NRelicHistory>(runHistory!);
        WatchRunHistoryContainer(
            deckHistory?.GetNodeOrNull<Control>("%CardContainer"));
        WatchRunHistoryContainer(
            relicHistory?.GetNodeOrNull<Control>("%RelicsContainer"));
    }

    private static void AttachTarget(
        Control target,
        bool subscribeToGuiInput)
    {
        if (!IsLive(target)) return;

        var instanceId = target.GetInstanceId();
        if (Subscriptions.ContainsKey(instanceId)) return;

        Control.GuiInputEventHandler? guiInputHandler = subscribeToGuiInput
            ? inputEvent => OnGuiInput(target, inputEvent)
            : null;
        Action treeExitingHandler = () =>
            OnTargetTreeExiting(target, instanceId);

        if (guiInputHandler != null)
            target.GuiInput += guiInputHandler;
        target.TreeExiting += treeExitingHandler;
        Subscriptions[instanceId] = new TargetSubscription(
            target,
            guiInputHandler,
            treeExitingHandler);
    }

    public static void ReconcilePinnedState()
    {
        if (_pinnedTarget == null) return;

        if (!IsLive(_pinnedTarget)
            || !_pinnedTarget!.IsVisibleInTree()
            || _pinnedTarget is NCardHolder cardHolder
                && !ReferenceEquals(_pinnedCardModel, cardHolder.CardModel)
            || _pinnedTarget is NDeckHistoryEntry historyEntry
                && !ReferenceEquals(_pinnedCardModel, historyEntry.Card)
            || !IsLive(_pinOwner)
            || !IsLive(_pinnedTipSet)
            || _pinnedTipSet!.IsQueuedForDeletion())
        {
            ClearPin(restoreOrdinaryHover: false);
        }
    }

    public static void ClearPin()
        => ClearPin(restoreOrdinaryHover: false);

    public static void UnpinIfHolder(NRelicInventoryHolder holder)
    {
        if (ReferenceEquals(_pinnedTarget, holder))
            ClearPin(restoreOrdinaryHover: false);
    }

    public static void Teardown()
    {
        ClearPin(restoreOrdinaryHover: false);

        foreach (var subscription in Subscriptions.Values)
        {
            if (!IsLive(subscription.Target)) continue;

            if (subscription.GuiInputHandler != null)
                subscription.Target.GuiInput -= subscription.GuiInputHandler;
            subscription.Target.TreeExiting -= subscription.TreeExitingHandler;
        }

        Subscriptions.Clear();

        foreach (var subscription in RunHistoryContainerSubscriptions.Values)
        {
            if (!IsLive(subscription.Container)) continue;

            subscription.Container.ChildEnteredTree -=
                subscription.ChildEnteredTreeHandler;
            subscription.Container.TreeExiting -=
                subscription.TreeExitingHandler;
        }

        RunHistoryContainerSubscriptions.Clear();
        _lockTexture = null;
        _lockLoadAttempted = false;
        _suppressRightPressUntilRelease = false;
    }

    /// <summary>
    /// A pin exists only to let the pointer travel from the card/relic into its
    /// stats page. Pointer motion is therefore allowed, but the next actual
    /// mouse, keyboard, or controller action dismisses the pin without
    /// consuming the action that the game is about to handle.
    /// </summary>
    internal static void DismissOnGlobalAction(InputEvent inputEvent)
    {
        // Godot may expose different managed InputEvent wrappers to _Input and
        // the later _GuiInput/OnMousePressed dispatch for one native click.
        // Keep the dismissal guard alive for the physical button lifecycle
        // instead of comparing wrapper instance ids.
        if (IsRightRelease(inputEvent))
        {
            _suppressRightPressUntilRelease = false;
            return;
        }

        // If a release was lost while the window changed focus, a later
        // globally-observed right press is necessarily a new physical press.
        // Do not let a stale latch consume it.
        if (_suppressRightPressUntilRelease
            && IsRightPress(inputEvent)
            && _pinnedTarget == null)
        {
            _suppressRightPressUntilRelease = false;
        }

        // The copy control is the only actionable child of a pinned tooltip.
        // Preserve the pin through its press so Godot can deliver the later
        // release and Pressed signal to the button. Every other click keeps
        // the ordinary dismiss-and-continue behavior below.
        if (IsCopyImageButtonPress(inputEvent))
            return;

        if (_pinnedTarget == null || !IsDismissAction(inputEvent))
            return;

        if (IsRightPress(inputEvent))
            _suppressRightPressUntilRelease = true;

        // _Input runs before every lockable surface's control-specific input
        // handler. Remove the pin once here. If this was a right press, the
        // latch above makes the later handler consume the same physical press
        // instead of immediately pinning again.
        ClearPin(restoreOrdinaryHover: false);
    }

    internal static void HandlePinnedHintHover(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventMouseMotion mouseMotion
            || !IsLive(_pinnedStatsDescription)
            || !_pinnedStatsDescription!.IsVisibleInTree())
        {
            return;
        }

        var description = _pinnedStatsDescription;
        if (!description.GetGlobalRect().HasPoint(mouseMotion.Position))
        {
            ClearHintPopup();
            return;
        }

        var localPosition = description.GetLocalMousePosition();
        var tooltip = description.GetTooltip(localPosition) ?? string.Empty;
        if (string.IsNullOrEmpty(tooltip))
        {
            ClearHintPopup();
            return;
        }

        ShowHintPopup(tooltip, mouseMotion.Position);
    }

    internal static bool ShouldSuppressOrdinaryHoverTip(Control owner)
    {
        // A pinned set is created through a surrogate Control owner. During
        // that CreateAndShow call, _pinnedTipSet cannot be assigned until the
        // call returns, so reconciling the surrogate here would mistake the
        // in-progress pin for a dead one and remove the SpireLens page before
        // NativeStatsHoverTipFactory can append it.
        if (owner is not NRelicInventoryHolder
            && owner is not NRelicCollectionEntry
            && owner is not NCardHolder
            && owner is not NDeckHistoryEntry
            && owner is not NRelicBasicHolder
            && owner is not RunHistoryCampfireButton
            && !RunHistoryHpTooltip.IsTarget(owner)
            && !RunHistoryGoldTooltip.IsTarget(owner)
            && !RunTimerStatsTooltip.IsTarget(owner))
        {
            return false;
        }

        ReconcilePinnedState();
        return ReferenceEquals(_pinnedTarget, owner);
    }

    internal static bool TryBuildPinnedStatsTip(
        Control owner,
        out HoverTip tip)
    {
        tip = default;
        if (!ReferenceEquals(owner, _pinOwner) || !IsLive(_pinnedTarget))
            return false;

        return TryBuildStatsTip(_pinnedTarget!, out tip);
    }

    /// <summary>
    /// Handles a card right press before NCardHolder records it as the
    /// pending alternate-click action. Returning true tells the Harmony
    /// prefix to skip the game's handler, which also prevents its matching
    /// release from emitting AltPressed.
    /// </summary>
    internal static bool TryHandleCardRightClick(
        NCardHolder holder,
        InputEvent inputEvent)
    {
        if (!IsRightPress(inputEvent)
            || !IsPassiveCardPileView(holder))
        {
            return false;
        }

        AttachTarget(holder, subscribeToGuiInput: false);
        return TryTogglePin(holder, inputEvent);
    }

    private static bool IsPassiveCardPileView(Node node)
    {
        for (var current = node.GetParent();
             current != null;
             current = current.GetParent())
        {
            if (current is NCardPileScreen or NCardsViewScreen)
                return true;
        }

        return false;
    }

    private static void OnGuiInput(
        Control target,
        InputEvent inputEvent)
    {
        if (!IsRightPress(inputEvent)) return;

        if (TryTogglePin(target, inputEvent))
            target.GetViewport()?.SetInputAsHandled();
    }

    private static bool TryTogglePin(
        Control target,
        InputEvent inputEvent)
    {
        if (_suppressRightPressUntilRelease)
        {
            // Global input has already removed the pin for this physical
            // press. Restore only the target's ordinary hover set and claim
            // the press so neither SpireLens nor the game treats it as a new
            // right-click action.
            RestoreOrdinaryHover(target);
            target.GetViewport()?.SetInputAsHandled();
            return true;
        }

        try
        {
            ReconcilePinnedState();

            if (ReferenceEquals(_pinnedTarget, target))
            {
                ClearPin(restoreOrdinaryHover: true);
            }
            else
            {
                if (!CanPin(target)) return false;
                Pin(target);
            }

            target.GetViewport()?.SetInputAsHandled();
            return true;
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"Stats tooltip pin toggle failed: {e}");
            return false;
        }
    }

    private static bool CanPin(Control target)
    {
        return IsLive(target)
               && target.IsVisibleInTree()
               && ViewStatsInjectorPatch.StatsVisibilityEnabled
               && TryBuildStatsTip(target, out _)
               && TryGetNativeHoverTips(target, out _);
    }

    private static void Pin(Control target)
    {
        ClearPin(restoreOrdinaryHover: false);
        if (!CanPin(target)
            || !TryGetNativeHoverTips(target, out var nativeHoverTips))
        {
            return;
        }

        NHoverTipSet.Remove(target);

        var pinOwner = new Control
        {
            Name = PinOwnerNodeName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None,
        };
        target.AddChild(pinOwner);

        _pinnedTarget = target;
        _pinOwner = pinOwner;
        _pinnedCardModel = target switch
        {
            NCardHolder cardHolder => cardHolder.CardModel,
            NDeckHistoryEntry historyEntry => historyEntry.Card,
            _ => null,
        };

        try
        {
            var tipSet = NHoverTipSet.CreateAndShow(
                pinOwner,
                nativeHoverTips);
            if (tipSet == null)
            {
                ClearPin(restoreOrdinaryHover: false);
                return;
            }

            _pinnedTipSet = tipSet;
            AlignPinnedTipSet(target, tipSet);
            _pinnedStatsControl =
                NativeStatsHoverTipStyler.GetLastStatsControl(tipSet);
            _pinnedStatsDescription =
                NativeStatsHoverTipStyler.GetLastStatsDescription(tipSet);
            AttachCopyImageButton();
            AddLockIcon(target);
            CoreMain.LogDebug(
                $"Pinned stats tooltip: {GetTargetDebugId(target)}");
        }
        catch
        {
            ClearPin(restoreOrdinaryHover: false);
            throw;
        }
    }

    private static void ClearPin(bool restoreOrdinaryHover)
    {
        var target = _pinnedTarget;
        var pinOwner = _pinOwner;
        DetachCopyImageButton();

        _pinnedTarget = null;
        _pinOwner = null;
        _pinnedTipSet = null;
        _pinnedStatsControl = null;
        _pinnedStatsDescription = null;
        _pinnedCardModel = null;
        ClearHintPopup();

        if (IsLive(pinOwner))
        {
            NHoverTipSet.Remove(pinOwner!);
            pinOwner!.GetParent()?.RemoveChild(pinOwner);
            pinOwner.QueueFree();
        }

        RemoveLockIcon(target);

        if (!restoreOrdinaryHover
            || !IsLive(target)
            || !target!.IsVisibleInTree()
            || !ViewStatsInjectorPatch.StatsVisibilityEnabled)
        {
            return;
        }

        RestoreOrdinaryHover(target);
    }

    private static void AttachCopyImageButton()
    {
        DetachCopyImageButton();
        if (!IsLive(_pinnedStatsControl)) return;

        var title = _pinnedStatsControl!
            .GetNodeOrNull<Control>("%Title");
        if (title?.GetParent() is not HBoxContainer header)
        {
            CoreMain.LogDebug(
                "Stats image copy button skipped: tooltip title header was not found.");
            return;
        }

        var button = new Button
        {
            Name = CopyImageButtonNodeName,
            Text = CopyImageButtonText,
            Flat = true,
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(68f, 28f),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        button.AddThemeFontSizeOverride("font_size", 14);
        button.AddThemeColorOverride("font_color", Color.FromHtml("#94A0AE"));
        button.AddThemeColorOverride("font_hover_color", Color.FromHtml("#E8EDF4"));
        button.AddThemeColorOverride("font_pressed_color", Color.FromHtml("#A9BCEB"));
        button.AddThemeColorOverride("font_disabled_color", Color.FromHtml("#94A0AE"));

        Action handler = OnCopyImageButtonPressed;
        button.Pressed += handler;
        header.AddChild(button);

        _copyImageButton = button;
        _copyImageButtonHandler = handler;
        _copyImageGeneration++;
        _copyImageInProgress = false;
    }

    private static void DetachCopyImageButton()
    {
        _copyImageGeneration++;
        _copyImageInProgress = false;

        var button = _copyImageButton;
        var handler = _copyImageButtonHandler;
        _copyImageButton = null;
        _copyImageButtonHandler = null;

        if (IsLive(button) && handler != null)
            button!.Pressed -= handler;
    }

    private static void OnCopyImageButtonPressed()
    {
        if (_copyImageInProgress) return;
        _ = CopyPinnedStatsImageAsync();
    }

    private static async Task CopyPinnedStatsImageAsync()
    {
        if (_copyImageInProgress
            || !IsLive(_copyImageButton)
            || !IsLive(_pinnedStatsControl)
            || !IsLive(_pinnedTarget)
            || !IsLive(_pinnedTipSet))
        {
            return;
        }

        var generation = _copyImageGeneration;
        var button = _copyImageButton!;
        var statsControl = _pinnedStatsControl!;
        var pinnedTarget = _pinnedTarget!;
        var pinnedTipSet = _pinnedTipSet!;
        var lockIcon = _lockIconHost
            ?.GetNodeOrNull<CanvasItem>(LockIconNodeName);
        var lockIconWasVisible = IsLive(lockIcon) && lockIcon!.Visible;
        var copied = false;
        var feedback = "Copy failed";
        _copyImageInProgress = true;
        button.Disabled = true;
        button.Visible = false;
        if (lockIconWasVisible)
            lockIcon!.Visible = false;

        try
        {
            // Wait until the next completed draw so the hidden button is not
            // present in the viewport texture being captured.
            await statsControl.ToSignal(
                RenderingServer.Singleton,
                RenderingServer.SignalName.FramePostDraw);
            if (generation != _copyImageGeneration
                || !IsLive(statsControl)
                || !IsLive(pinnedTarget)
                || !IsLive(pinnedTipSet))
            {
                return;
            }

            if (!StatsImageCapture.TryCaptureShareImage(
                    statsControl,
                    GetRenderedSubjectRect(pinnedTarget),
                    GetIsolatedSubjectTexture(pinnedTarget),
                    GetTooltipCaptureGroups(pinnedTipSet),
                    out var image,
                    out var captureError))
            {
                CoreMain.Logger.Error(
                    $"Stats image capture failed: {captureError}");
                feedback = "Capture failed";
                return;
            }

            using (image)
            {
                if (!WindowsImageClipboard.TrySetImage(
                        image,
                        out var clipboardError))
                {
                    CoreMain.Logger.Error(
                        $"Stats image clipboard write failed: {clipboardError}");
                    return;
                }
            }

            copied = true;
            feedback = "Copied";
        }
        catch (Exception exception)
        {
            CoreMain.Logger.Error(
                $"Stats image copy failed: {exception}");
        }
        finally
        {
            if (lockIconWasVisible && IsLive(lockIcon))
                lockIcon!.Visible = true;

            if (generation == _copyImageGeneration && IsLive(button))
            {
                button.Text = feedback;
                button.Visible = true;
                button.Disabled = false;
            }

            if (generation == _copyImageGeneration)
                _copyImageInProgress = false;
        }

        if (!copied
            || generation != _copyImageGeneration
            || !IsLive(button))
        {
            return;
        }

        try
        {
            var tree = button.GetTree();
            if (tree == null) return;

            var timer = tree.CreateTimer(CopyFeedbackDurationSeconds);
            await button.ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
            if (generation == _copyImageGeneration && IsLive(button))
                button.Text = CopyImageButtonText;
        }
        catch (Exception exception)
        {
            CoreMain.LogDebug(
                $"Stats image copy feedback reset skipped: {exception.Message}");
        }
    }

    private static IReadOnlyList<Control> GetTooltipCaptureGroups(
        NHoverTipSet tipSet)
    {
        var groups = new List<Control>(2);
        if (IsLive(tipSet._textHoverTipContainer)
            && tipSet._textHoverTipContainer.GetChildCount() > 0)
        {
            groups.Add(tipSet._textHoverTipContainer);
        }

        if (IsLive(tipSet._cardHoverTipContainer)
            && tipSet._cardHoverTipContainer.GetChildCount() > 0)
        {
            groups.Add(tipSet._cardHoverTipContainer);
        }

        return groups;
    }

    private static Texture2D? GetIsolatedSubjectTexture(Control target)
    {
        var relicModel = target switch
        {
            NRelicInventoryHolder holder => holder.Relic.Model,
            NRelicCollectionEntry entry
                when CompendiumRelicStatsContext.TryGetRelicModel(entry, out var model)
                => model,
            NRelicBasicHolder holder when IsLive(holder.Relic)
                => holder.Relic.Model,
            _ => null,
        };

        return relicModel?.BigIcon ?? relicModel?.Icon;
    }

    private static Rect2 GetRenderedSubjectRect(Control target)
    {
        Control visual = target switch
        {
            NCardHolder holder when IsLive(holder.CardNode)
                => holder.CardNode!,
            NRelicInventoryHolder holder when IsLive(holder.Relic)
                => holder.Relic!,
            NDeckHistoryEntry entry
                when IsLive(entry.GetNodeOrNull<Control>("%Card"))
                => entry.GetNode<Control>("%Card"),
            NRelicBasicHolder holder when IsLive(holder.Relic)
                => holder.Relic!,
            _ => target,
        };

        if (visual is not NCard card)
            return visual.GetGlobalRect();

        // NCard draws around a zero-sized Control origin, so its ordinary
        // global rect is empty. Transform the known visual bounds instead.
        var halfSize = NCard.defaultSize / 2f;
        var transform = card.GetGlobalTransform();
        var topLeft = transform * -halfSize;
        var topRight = transform * new Vector2(halfSize.X, -halfSize.Y);
        var bottomLeft = transform * new Vector2(-halfSize.X, halfSize.Y);
        var bottomRight = transform * halfSize;
        var left = Math.Min(
            Math.Min(topLeft.X, topRight.X),
            Math.Min(bottomLeft.X, bottomRight.X));
        var top = Math.Min(
            Math.Min(topLeft.Y, topRight.Y),
            Math.Min(bottomLeft.Y, bottomRight.Y));
        var right = Math.Max(
            Math.Max(topLeft.X, topRight.X),
            Math.Max(bottomLeft.X, bottomRight.X));
        var bottom = Math.Max(
            Math.Max(topLeft.Y, topRight.Y),
            Math.Max(bottomLeft.Y, bottomRight.Y));
        return new Rect2(left, top, right - left, bottom - top);
    }

    private static bool TryBuildStatsTip(Control target, out HoverTip tip)
    {
        switch (target)
        {
            case NCardHolder holder:
                return CardHoverShowPatch.TryBuildNativeHoverTip(holder, out tip);

            case NRelicInventoryHolder holder:
                return RelicHoverShowPatch.TryBuildNativeHoverTip(holder, out tip);

            case NRelicCollectionEntry entry:
                return CompendiumRelicStatsContext.TryBuildNativeHoverTip(entry, out tip);

            case NDeckHistoryEntry entry:
                return RunHistoryStatsContext.TryBuildNativeCardHoverTip(entry, out tip);

            case NRelicBasicHolder holder:
                return RunHistoryStatsContext.TryBuildNativeRelicHoverTip(holder, out tip);

            case RunHistoryCampfireButton button:
                return button.TryBuildStatsTip(out tip);

            case Control label when RunHistoryHpTooltip.IsTarget(label):
                return RunHistoryHpTooltip.TryBuildStatsTip(label, out tip);

            case Control label when RunHistoryGoldTooltip.IsTarget(label):
                return RunHistoryGoldTooltip.TryBuildStatsTip(label, out tip);

            case Control timer when RunTimerStatsTooltip.IsTarget(timer):
                return RunTimerStatsTooltip.TryBuildStatsTip(timer, out tip);

            default:
                tip = default;
                return false;
        }
    }

    private static bool TryGetNativeHoverTips(
        Control target,
        out IEnumerable<IHoverTip> nativeHoverTips)
    {
        switch (target)
        {
            case NCardHolder holder when holder.CardModel != null:
                nativeHoverTips = holder.CardModel.HoverTips;
                return true;

            case NRelicInventoryHolder holder:
                nativeHoverTips = holder.Relic.Model.HoverTips;
                return true;

            case NRelicCollectionEntry entry
                when CompendiumRelicStatsContext.TryGetRelicModel(entry, out var relicModel):
                nativeHoverTips = relicModel.HoverTips;
                return true;

            case NDeckHistoryEntry entry when entry.Card != null:
                nativeHoverTips = entry.Card.HoverTips;
                return true;

            case NRelicBasicHolder holder when IsLive(holder.Relic):
                nativeHoverTips = holder.Relic.Model.HoverTips;
                return true;

            case RunHistoryCampfireButton:
                // The campfire summary has no stock tooltip page. The pin
                // surrogate receives its SpireLens page through
                // NativeStatsHoverTipFactory, just like appended card/relic
                // stats, so the native sequence intentionally starts empty.
                nativeHoverTips = Array.Empty<IHoverTip>();
                return true;

            case Control label when RunHistoryHpTooltip.IsTarget(label):
                nativeHoverTips = Array.Empty<IHoverTip>();
                return true;

            case Control label when RunHistoryGoldTooltip.IsTarget(label):
                nativeHoverTips = Array.Empty<IHoverTip>();
                return true;

            case Control timer when RunTimerStatsTooltip.IsTarget(timer):
                nativeHoverTips = Array.Empty<IHoverTip>();
                return true;

            default:
                nativeHoverTips = null!;
                return false;
        }
    }

    private static void AlignPinnedTipSet(Control target, NHoverTipSet tipSet)
    {
        switch (target)
        {
            case NCardHolder holder:
                tipSet.SetAlignmentForCardHolder(holder);
                break;

            case NRelicInventoryHolder holder:
                tipSet.SetAlignmentForRelic(holder.Relic);
                break;

            case NRelicCollectionEntry entry:
                tipSet.SetAlignment(entry, HoverTip.GetHoverTipAlignment(entry));
                break;

            case NDeckHistoryEntry entry:
                tipSet.SetAlignment(entry, HoverTip.GetHoverTipAlignment(entry));
                break;

            case NRelicBasicHolder holder:
                tipSet.SetAlignmentForRelic(holder.Relic);
                break;

            case RunHistoryCampfireButton button:
                tipSet.SetAlignment(
                    button,
                    HoverTip.GetHoverTipAlignment(button));
                break;

            case Control label when RunHistoryHpTooltip.IsTarget(label):
                tipSet.SetAlignment(
                    label,
                    HoverTip.GetHoverTipAlignment(label));
                break;

            case Control label when RunHistoryGoldTooltip.IsTarget(label):
                tipSet.SetAlignment(
                    label,
                    HoverTip.GetHoverTipAlignment(label));
                break;

            case Control timer when RunTimerStatsTooltip.IsTarget(timer):
                tipSet.SetAlignment(
                    timer,
                    RunTimerStatsTooltip.GetNonObscuringAlignment(timer));
                break;
        }
    }

    private static void RestoreOrdinaryHover(Control target)
    {
        if (!CanPin(target)
            || !TryGetNativeHoverTips(target, out var nativeHoverTips))
        {
            return;
        }

        switch (target)
        {
            case NCardHolder holder:
                NHoverTipSet.CreateAndShow(target, nativeHoverTips)
                    ?.SetAlignmentForCardHolder(holder);
                break;

            case NRelicInventoryHolder holder:
                NHoverTipSet.CreateAndShow(target, nativeHoverTips)
                    ?.SetAlignmentForRelic(holder.Relic);
                break;

            case NRelicCollectionEntry entry:
                NHoverTipSet.CreateAndShow(
                        target,
                        nativeHoverTips,
                        HoverTip.GetHoverTipAlignment(entry))
                    ?.SetFollowOwner();
                break;

            // Run-history card rows do not create an ordinary hover-tip set.
            // Their pinned set is therefore removed without manufacturing a
            // new transient tooltip when the pin is released.

            case NRelicBasicHolder holder:
                NHoverTipSet.CreateAndShow(target, nativeHoverTips)
                    ?.SetAlignmentForRelic(holder.Relic);
                break;

            case RunHistoryCampfireButton button:
                RunHistoryCampfireSummary.ShowTooltip(button);
                break;

            case Control label when RunHistoryHpTooltip.IsTarget(label):
                RunHistoryHpTooltip.ShowTooltip(label);
                break;

            case Control label when RunHistoryGoldTooltip.IsTarget(label):
                RunHistoryGoldTooltip.ShowTooltip(label);
                break;

            case Control timer when RunTimerStatsTooltip.IsTarget(timer):
                RunTimerStatsTooltip.ShowTooltip(timer);
                break;
        }
    }

    private static string GetTargetDebugId(Control target)
    {
        return target switch
        {
            NCardHolder holder when holder.CardModel != null
                => holder.CardModel.Id.ToString(),
            NRelicInventoryHolder holder => holder.Relic.Model.Id.ToString(),
            NRelicCollectionEntry entry
                when CompendiumRelicStatsContext.TryGetRelicModel(entry, out var relicModel)
                => relicModel.Id.ToString(),
            NDeckHistoryEntry entry when entry.Card != null
                => entry.Card.Id.ToString(),
            NRelicBasicHolder holder when IsLive(holder.Relic)
                => holder.Relic.Model.Id.ToString(),
            RunHistoryCampfireButton => "run-history-campfires",
            Control label when RunHistoryHpTooltip.IsTarget(label)
                => "run-history-hp",
            Control label when RunHistoryGoldTooltip.IsTarget(label)
                => "run-history-gold",
            Control timer when RunTimerStatsTooltip.IsTarget(timer)
                => "run-timer-stats",
            _ => target.Name,
        };
    }

    private static void ShowHintPopup(string tooltip, Vector2 pointerPosition)
    {
        if (string.Equals(tooltip, _visibleHintText, StringComparison.Ordinal)
            && IsLive(_hintOwner))
        {
            return;
        }

        ClearHintPopup();
        if (!IsLive(_pinOwner)) return;

        var hintOwner = new Control
        {
            Name = HintOwnerNodeName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None,
            Size = Vector2.One,
        };
        _pinOwner!.AddChild(hintOwner);
        hintOwner.GlobalPosition = pointerPosition + new Vector2(18f, 18f);

        _hintOwner = hintOwner;
        _visibleHintText = tooltip;
        try
        {
            var hintSet = NHoverTipSet.CreateAndShow(
                hintOwner,
                StatsTooltip.CreateNativeHint(tooltip),
                HoverTipAlignment.Right);
            if (hintSet == null)
                ClearHintPopup();
        }
        catch
        {
            ClearHintPopup();
            throw;
        }
    }

    private static void ClearHintPopup()
    {
        var hintOwner = _hintOwner;
        _hintOwner = null;
        _visibleHintText = null;
        if (!IsLive(hintOwner)) return;

        NHoverTipSet.Remove(hintOwner!);
        hintOwner!.GetParent()?.RemoveChild(hintOwner);
        hintOwner.QueueFree();
    }

    private static void AddLockIcon(Control target)
    {
        RemoveLockIcon(target);
        var texture = GetLockTexture();
        var host = GetLockIconHost(target);
        if (texture == null || !IsLive(host)) return;

        var isFullCard = target is NCardHolder;
        var width = isFullCard ? CardLockIconWidth : LockIconWidth;
        var height = isFullCard ? CardLockIconHeight : LockIconHeight;
        var rightInset = isFullCard
            ? CardLockIconRightInset
            : LockIconRightInset;
        var topInset = isFullCard
            ? CardLockIconTopInset
            : LockIconTopInset;

        var lockIcon = new TextureRect
        {
            Name = LockIconNodeName,
            Texture = texture,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        if (isFullCard)
        {
            // NCard draws its 300x422 card centered around a zero-sized
            // Control origin. Anchoring to NCard's Control rect therefore
            // targets the center of the artwork, not its top-right corner.
            // Position against the visual bounds reported by the game.
            lockIcon.Position = new Vector2(
                (NCard.defaultSize.X / 2f) - rightInset - width,
                (-NCard.defaultSize.Y / 2f) + topInset);
            lockIcon.Size = new Vector2(width, height);
        }
        else
        {
            lockIcon.AnchorLeft = 1f;
            lockIcon.AnchorRight = 1f;
            lockIcon.AnchorTop = 0f;
            lockIcon.AnchorBottom = 0f;
            lockIcon.OffsetLeft = -(rightInset + width);
            lockIcon.OffsetRight = -rightInset;
            lockIcon.OffsetTop = topInset;
            lockIcon.OffsetBottom = topInset + height;
        }

        host!.AddChild(lockIcon);
        _lockIconHost = host;
    }

    private static Control GetLockIconHost(Control target)
    {
        // Card holders are interaction/layout slots whose bounds can be much
        // larger than the rendered card. NCard owns the visual transform; its
        // centered visual bounds are handled explicitly in AddLockIcon.
        if (target is NCardHolder holder
            && IsLive(holder.CardNode))
        {
            return holder.CardNode!;
        }

        if (target is NDeckHistoryEntry historyEntry
            && IsLive(historyEntry.GetNodeOrNull<Control>("%Card")))
        {
            return historyEntry.GetNode<Control>("%Card");
        }

        if (target is NRelicBasicHolder basicHolder
            && IsLive(basicHolder.Relic))
        {
            return basicHolder.Relic;
        }

        return target;
    }

    private static void AttachRunHistoryDescendants(Node node)
    {
        switch (node)
        {
            case NDeckHistoryEntry entry:
                AttachTarget(entry, subscribeToGuiInput: true);
                break;

            case NRelicBasicHolder holder
                when RunHistoryStatsContext.HasAncestor<NRelicHistory>(holder):
                AttachTarget(holder, subscribeToGuiInput: true);
                break;
        }

        foreach (var child in node.GetChildren())
            AttachRunHistoryDescendants(child);
    }

    private static void WatchRunHistoryContainer(Control? container)
    {
        if (!IsLive(container)) return;

        var instanceId = container!.GetInstanceId();
        if (RunHistoryContainerSubscriptions.ContainsKey(instanceId)) return;

        Node.ChildEnteredTreeEventHandler childEnteredTreeHandler =
            AttachRunHistoryDescendants;
        Action treeExitingHandler = () =>
            OnRunHistoryContainerTreeExiting(container, instanceId);

        container.ChildEnteredTree += childEnteredTreeHandler;
        container.TreeExiting += treeExitingHandler;
        RunHistoryContainerSubscriptions[instanceId] =
            new RunHistoryContainerSubscription(
                container,
                childEnteredTreeHandler,
                treeExitingHandler);
    }

    private static void OnRunHistoryContainerTreeExiting(
        Control container,
        ulong instanceId)
    {
        if (RunHistoryContainerSubscriptions.Remove(
                instanceId,
                out var subscription)
            && IsLive(container))
        {
            container.ChildEnteredTree -= subscription.ChildEnteredTreeHandler;
            container.TreeExiting -= subscription.TreeExitingHandler;
        }
    }

    private static T? FindDescendant<T>(Node node) where T : Node
    {
        if (node is T match) return match;

        foreach (var child in node.GetChildren())
        {
            var descendant = FindDescendant<T>(child);
            if (descendant != null) return descendant;
        }

        return null;
    }

    private static void RemoveLockIcon(Control? target)
    {
        var recordedHost = _lockIconHost;
        _lockIconHost = null;
        RemoveLockIconFromHost(recordedHost);

        if (!IsLive(target)) return;

        var currentHost = GetLockIconHost(target!);
        if (!ReferenceEquals(currentHost, recordedHost))
            RemoveLockIconFromHost(currentHost);
    }

    private static void RemoveLockIconFromHost(Control? host)
    {
        if (!IsLive(host)) return;

        var lockIcon = host!.GetNodeOrNull<TextureRect>(LockIconNodeName);
        if (!IsLive(lockIcon)) return;

        lockIcon!.GetParent()?.RemoveChild(lockIcon);
        lockIcon.QueueFree();
    }

    private static Texture2D? GetLockTexture()
    {
        if (_lockLoadAttempted) return _lockTexture;
        _lockLoadAttempted = true;

        _lockTexture = ResourceLoader.Load<Texture2D>(
            LockIconPath,
            null,
            ResourceLoader.CacheMode.Reuse);
        if (_lockTexture == null)
        {
            CoreMain.Logger.Error(
                $"Could not load stats tooltip lock icon: {LockIconPath}");
        }

        return _lockTexture;
    }

    private static void OnTargetTreeExiting(
        Control target,
        ulong instanceId)
    {
        if (ReferenceEquals(_pinnedTarget, target))
            ClearPin(restoreOrdinaryHover: false);

        Subscriptions.Remove(instanceId);
    }

    private static bool IsDismissAction(InputEvent inputEvent)
    {
        return inputEvent switch
        {
            InputEventMouseButton mouseButton => mouseButton.Pressed,
            InputEventKey key => key.Pressed && !key.Echo,
            InputEventJoypadButton button => button.Pressed,
            InputEventJoypadMotion motion => Math.Abs(motion.AxisValue) >= 0.5f,
            InputEventScreenTouch touch => touch.Pressed,
            InputEventScreenDrag => true,
            InputEventGesture => true,
            InputEventAction action => action.Pressed,
            _ => false,
        };
    }

    private static bool IsRightPress(InputEvent inputEvent)
        => inputEvent is InputEventMouseButton
        {
            ButtonIndex: MouseButton.Right,
            Pressed: true,
        };

    private static bool IsRightRelease(InputEvent inputEvent)
        => inputEvent is InputEventMouseButton
        {
            ButtonIndex: MouseButton.Right,
            Pressed: false,
        };

    private static bool IsCopyImageButtonPress(InputEvent inputEvent)
    {
        return inputEvent is InputEventMouseButton
            {
                ButtonIndex: MouseButton.Left,
                Pressed: true,
            } mouseButton
            && IsLive(_copyImageButton)
            && _copyImageButton!.Visible
            && !_copyImageButton.Disabled
            && _copyImageButton.GetGlobalRect().HasPoint(mouseButton.Position);
    }

    private static bool IsLive(GodotObject? instance)
        => instance != null && GodotObject.IsInstanceValid(instance);
}
