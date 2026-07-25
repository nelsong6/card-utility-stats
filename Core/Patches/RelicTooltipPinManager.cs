using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Pins one owned-relic tooltip set at a time. The pinned set uses a dedicated
/// native owner so the relic's ordinary OnUnfocus/Remove lifecycle can run
/// unchanged without dismissing it.
/// </summary>
internal static class RelicTooltipPinManager
{
    private const string PinOwnerNodeName = "SpireLensPinnedRelicTooltipOwner";
    private const string LockIconNodeName = "SpireLensRelicTooltipLock";
    private const string LockIconPath =
        "res://images/ui/top_panel/reminder_lock.png";

    private sealed class HolderSubscription
    {
        public HolderSubscription(
            NRelicInventoryHolder holder,
            Control.GuiInputEventHandler guiInputHandler,
            Action treeExitingHandler)
        {
            Holder = holder;
            GuiInputHandler = guiInputHandler;
            TreeExitingHandler = treeExitingHandler;
        }

        public NRelicInventoryHolder Holder { get; }
        public Control.GuiInputEventHandler GuiInputHandler { get; }
        public Action TreeExitingHandler { get; }
    }

    private static readonly Dictionary<ulong, HolderSubscription> Subscriptions = new();

    private static NRelicInventoryHolder? _pinnedHolder;
    private static Control? _pinOwner;
    private static NHoverTipSet? _pinnedTipSet;
    private static Texture2D? _lockTexture;
    private static bool _lockLoadAttempted;
    private static ulong _dismissedInputEventId;

    public static void Attach(NRelicInventoryHolder? holder)
    {
        if (!IsLive(holder)) return;

        var instanceId = holder!.GetInstanceId();
        if (Subscriptions.ContainsKey(instanceId)) return;

        Control.GuiInputEventHandler guiInputHandler = inputEvent =>
            OnGuiInput(holder, inputEvent);
        Action treeExitingHandler = () =>
            OnHolderTreeExiting(holder, instanceId);

        holder.GuiInput += guiInputHandler;
        holder.TreeExiting += treeExitingHandler;
        Subscriptions[instanceId] = new HolderSubscription(
            holder,
            guiInputHandler,
            treeExitingHandler);
    }

    public static void ReconcilePinnedState()
    {
        if (_pinnedHolder == null) return;

        if (!IsLive(_pinnedHolder)
            || !_pinnedHolder!.IsVisibleInTree()
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
        if (ReferenceEquals(_pinnedHolder, holder))
            ClearPin(restoreOrdinaryHover: false);
    }

    public static void Teardown()
    {
        ClearPin(restoreOrdinaryHover: false);

        foreach (var subscription in Subscriptions.Values)
        {
            if (!IsLive(subscription.Holder)) continue;

            subscription.Holder.GuiInput -= subscription.GuiInputHandler;
            subscription.Holder.TreeExiting -= subscription.TreeExitingHandler;
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
        if (_pinnedHolder == null || !IsDismissAction(inputEvent))
            return;

        // _Input runs before _GuiInput. Remember the event so a right click
        // that dismissed a pin cannot reach a relic holder later in the same
        // dispatch and immediately create a new pin.
        _dismissedInputEventId = inputEvent.GetInstanceId();
        ClearPin(restoreOrdinaryHover: false);
    }

    internal static bool ShouldSuppressOrdinaryHoverTip(Control owner)
    {
        // A pinned set is created through a surrogate Control owner. During
        // that CreateAndShow call, _pinnedTipSet cannot be assigned until the
        // call returns, so reconciling the surrogate here would mistake the
        // in-progress pin for a dead one and remove the SpireLens page before
        // NativeStatsHoverTipFactory can append it.
        if (owner is not NRelicInventoryHolder holder)
            return false;

        ReconcilePinnedState();
        return ReferenceEquals(_pinnedHolder, holder);
    }

    internal static bool TryGetPinnedHolder(
        Control owner,
        out NRelicInventoryHolder holder)
    {
        holder = null!;
        if (!ReferenceEquals(owner, _pinOwner) || !IsLive(_pinnedHolder))
            return false;

        holder = _pinnedHolder!;
        return true;
    }

    private static void OnGuiInput(
        NRelicInventoryHolder holder,
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
            holder.GetViewport()?.SetInputAsHandled();
            return;
        }

        try
        {
            ReconcilePinnedState();

            if (ReferenceEquals(_pinnedHolder, holder))
            {
                ClearPin(restoreOrdinaryHover: true);
            }
            else
            {
                if (!CanPin(holder)) return;
                Pin(holder);
            }

            holder.GetViewport()?.SetInputAsHandled();
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"Relic tooltip pin toggle failed: {e}");
        }
    }

    private static bool CanPin(NRelicInventoryHolder holder)
    {
        return IsLive(holder)
               && holder.IsVisibleInTree()
               && ViewStatsInjectorPatch.StatsVisibilityEnabled
               && RelicHoverShowPatch.TryBuildNativeHoverTip(holder, out _);
    }

    private static void Pin(NRelicInventoryHolder holder)
    {
        ClearPin(restoreOrdinaryHover: false);
        if (!CanPin(holder)) return;

        NHoverTipSet.Remove(holder);

        var pinOwner = new Control
        {
            Name = PinOwnerNodeName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None,
        };
        holder.AddChild(pinOwner);

        _pinnedHolder = holder;
        _pinOwner = pinOwner;

        try
        {
            var tipSet = NHoverTipSet.CreateAndShow(
                pinOwner,
                holder.Relic.Model.HoverTips);
            if (tipSet == null)
            {
                ClearPin(restoreOrdinaryHover: false);
                return;
            }

            _pinnedTipSet = tipSet;
            tipSet.SetAlignmentForRelic(holder.Relic);
            NativeStatsHoverTipStyler.MakePinnedTipInteractive(tipSet);
            AddLockIcon(holder);
            CoreMain.LogDebug(
                $"Pinned relic tooltip: {holder.Relic.Model.Id}");
        }
        catch
        {
            ClearPin(restoreOrdinaryHover: false);
            throw;
        }
    }

    private static void ClearPin(bool restoreOrdinaryHover)
    {
        var holder = _pinnedHolder;
        var pinOwner = _pinOwner;

        _pinnedHolder = null;
        _pinOwner = null;
        _pinnedTipSet = null;

        if (IsLive(pinOwner))
        {
            NHoverTipSet.Remove(pinOwner!);
            pinOwner!.GetParent()?.RemoveChild(pinOwner);
            pinOwner.QueueFree();
        }

        if (IsLive(holder))
            RemoveLockIcon(holder!);

        if (!restoreOrdinaryHover
            || !IsLive(holder)
            || !holder!.IsVisibleInTree()
            || !ViewStatsInjectorPatch.StatsVisibilityEnabled)
        {
            return;
        }

        var ordinaryTipSet = NHoverTipSet.CreateAndShow(
            holder,
            holder.Relic.Model.HoverTips);
        ordinaryTipSet?.SetAlignmentForRelic(holder.Relic);
    }

    private static void AddLockIcon(NRelicInventoryHolder holder)
    {
        RemoveLockIcon(holder);
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
        holder.AddChild(lockIcon);
    }

    private static void RemoveLockIcon(NRelicInventoryHolder holder)
    {
        var lockIcon = holder.GetNodeOrNull<TextureRect>(LockIconNodeName);
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

    private static void OnHolderTreeExiting(
        NRelicInventoryHolder holder,
        ulong instanceId)
    {
        if (ReferenceEquals(_pinnedHolder, holder))
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

    private static bool IsLive(GodotObject? instance)
        => instance != null && GodotObject.IsInstanceValid(instance);
}
