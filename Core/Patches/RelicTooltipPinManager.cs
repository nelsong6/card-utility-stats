using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.RelicCollection;

namespace SpireLens.Core.Patches;

/// <summary>
/// Pins one relic tooltip set at a time. The pinned set uses a dedicated native
/// owner so the relic's ordinary OnUnfocus/Remove lifecycle can run unchanged
/// without dismissing it.
/// </summary>
internal static class RelicTooltipPinManager
{
    private const string PinOwnerNodeName = "SpireLensPinnedRelicTooltipOwner";
    private const string LockIconNodeName = "SpireLensRelicTooltipLock";
    private const string LockIconPath =
        "res://images/ui/top_panel/reminder_lock.png";
    private const string HintOwnerNodeName = "SpireLensPinnedRelicHintOwner";

    private sealed class TargetSubscription
    {
        public TargetSubscription(
            Control target,
            Control.GuiInputEventHandler guiInputHandler,
            Action treeExitingHandler)
        {
            Target = target;
            GuiInputHandler = guiInputHandler;
            TreeExitingHandler = treeExitingHandler;
        }

        public Control Target { get; }
        public Control.GuiInputEventHandler GuiInputHandler { get; }
        public Action TreeExitingHandler { get; }
    }

    private static readonly Dictionary<ulong, TargetSubscription> Subscriptions = new();

    private static Control? _pinnedTarget;
    private static Control? _pinOwner;
    private static NHoverTipSet? _pinnedTipSet;
    private static RichTextLabel? _pinnedStatsDescription;
    private static Control? _hintOwner;
    private static string? _visibleHintText;
    private static Texture2D? _lockTexture;
    private static bool _lockLoadAttempted;
    private static ulong _dismissedInputEventId;

    public static void Attach(NRelicInventoryHolder? holder)
    {
        if (holder != null)
            AttachTarget(holder);
    }

    public static void Attach(NRelicCollectionEntry? entry)
    {
        if (entry != null)
            AttachTarget(entry);
    }

    private static void AttachTarget(Control target)
    {
        if (!IsLive(target)) return;

        var instanceId = target.GetInstanceId();
        if (Subscriptions.ContainsKey(instanceId)) return;

        Control.GuiInputEventHandler guiInputHandler = inputEvent =>
            OnGuiInput(target, inputEvent);
        Action treeExitingHandler = () =>
            OnTargetTreeExiting(target, instanceId);

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

            subscription.Target.GuiInput -= subscription.GuiInputHandler;
            subscription.Target.TreeExiting -= subscription.TreeExitingHandler;
        }

        Subscriptions.Clear();
        _lockTexture = null;
        _lockLoadAttempted = false;
        _dismissedInputEventId = 0;
    }

    /// <summary>
    /// A pin exists only to let the pointer travel from the relic into its
    /// stats page. Pointer motion is therefore allowed, but the next actual
    /// mouse, keyboard, or controller action dismisses the pin without
    /// consuming the action that the game is about to handle.
    /// </summary>
    internal static void DismissOnGlobalAction(InputEvent inputEvent)
    {
        if (_pinnedTarget == null || !IsDismissAction(inputEvent))
            return;

        // A right press inside the pinned relic belongs to that target's
        // _GuiInput toggle. Do not clear it here first: Godot dispatches
        // _Input before _GuiInput, and clearing globally would make the
        // target see an unpinned relic and pin it again.
        if (IsRightClickInsidePinnedTarget(inputEvent))
            return;

        // _Input runs before _GuiInput. Remember the event so a right click
        // that dismissed a pin cannot reach another relic later in the same
        // dispatch and immediately create a new pin.
        _dismissedInputEventId = inputEvent.GetInstanceId();
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
            && owner is not NRelicCollectionEntry)
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

    private static void OnGuiInput(
        Control target,
        InputEvent inputEvent)
    {
        if (inputEvent is not InputEventMouseButton
            {
                ButtonIndex: MouseButton.Right,
                Pressed: true,
            })
        {
            return;
        }

        if (_dismissedInputEventId == inputEvent.GetInstanceId())
        {
            _dismissedInputEventId = 0;
            target.GetViewport()?.SetInputAsHandled();
            return;
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
                if (!CanPin(target)) return;
                Pin(target);
            }

            target.GetViewport()?.SetInputAsHandled();
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"Relic tooltip pin toggle failed: {e}");
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
            _pinnedStatsDescription =
                NativeStatsHoverTipStyler.GetLastStatsDescription(tipSet);
            AddLockIcon(target);
            CoreMain.LogDebug(
                $"Pinned relic tooltip: {GetRelicDebugId(target)}");
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

        _pinnedTarget = null;
        _pinOwner = null;
        _pinnedTipSet = null;
        _pinnedStatsDescription = null;
        ClearHintPopup();

        if (IsLive(pinOwner))
        {
            NHoverTipSet.Remove(pinOwner!);
            pinOwner!.GetParent()?.RemoveChild(pinOwner);
            pinOwner.QueueFree();
        }

        if (IsLive(target))
            RemoveLockIcon(target!);

        if (!restoreOrdinaryHover
            || !IsLive(target)
            || !target!.IsVisibleInTree()
            || !ViewStatsInjectorPatch.StatsVisibilityEnabled)
        {
            return;
        }

        RestoreOrdinaryHover(target);
    }

    private static bool TryBuildStatsTip(Control target, out HoverTip tip)
    {
        switch (target)
        {
            case NRelicInventoryHolder holder:
                return RelicHoverShowPatch.TryBuildNativeHoverTip(holder, out tip);

            case NRelicCollectionEntry entry:
                return CompendiumRelicStatsContext.TryBuildNativeHoverTip(entry, out tip);

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
            case NRelicInventoryHolder holder:
                nativeHoverTips = holder.Relic.Model.HoverTips;
                return true;

            case NRelicCollectionEntry entry
                when CompendiumRelicStatsContext.TryGetRelicModel(entry, out var relicModel):
                nativeHoverTips = relicModel.HoverTips;
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
            case NRelicInventoryHolder holder:
                tipSet.SetAlignmentForRelic(holder.Relic);
                break;

            case NRelicCollectionEntry entry:
                tipSet.SetAlignment(entry, HoverTip.GetHoverTipAlignment(entry));
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
        }
    }

    private static string GetRelicDebugId(Control target)
    {
        return target switch
        {
            NRelicInventoryHolder holder => holder.Relic.Model.Id.ToString(),
            NRelicCollectionEntry entry
                when CompendiumRelicStatsContext.TryGetRelicModel(entry, out var relicModel)
                => relicModel.Id.ToString(),
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
        if (texture == null) return;

        var lockIcon = new TextureRect
        {
            Name = LockIconNodeName,
            Texture = texture,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            AnchorLeft = 1f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 0f,
            OffsetLeft = -27f,
            OffsetRight = -3f,
            OffsetTop = 2f,
            OffsetBottom = 30f,
        };
        target.AddChild(lockIcon);
    }

    private static void RemoveLockIcon(Control target)
    {
        var lockIcon = target.GetNodeOrNull<TextureRect>(LockIconNodeName);
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
                $"Could not load relic tooltip lock icon: {LockIconPath}");
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

    private static bool IsRightClickInsidePinnedTarget(InputEvent inputEvent)
    {
        return inputEvent is InputEventMouseButton
               {
                   ButtonIndex: MouseButton.Right,
                   Pressed: true,
               } mouseButton
               && IsLive(_pinnedTarget)
               && _pinnedTarget!.IsVisibleInTree()
               && _pinnedTarget.GetGlobalRect().HasPoint(mouseButton.Position);
    }

    private static bool IsLive(GodotObject? instance)
        => instance != null && GodotObject.IsInstanceValid(instance);
}
