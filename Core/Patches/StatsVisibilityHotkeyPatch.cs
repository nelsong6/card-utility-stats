using System;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Debug;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Platform;

namespace SpireLens.Core.Patches;

/// <summary>
/// Toggles the persisted global stats visibility setting with Left Shift or
/// Right Stick press (R3). The game does not bind either input in its default
/// or current gameplay map, while Left Trigger is intentionally left alone
/// because it opens the draw pile and Left Stick press is Peek.
/// </summary>
[HarmonyPatch]
public static class StatsVisibilityHotkeyPatch
{
    private static readonly LeftShiftTapTracker TapTracker = new();

    private static MethodBase? TargetMethod()
    {
        var inputNodeType = AccessTools.TypeByName("SpireLens.Loader.HotReloadInputNode");
        return inputNodeType == null
            ? null
            : AccessTools.Method(inputNodeType, nameof(Node._Input), [typeof(InputEvent)]);
    }

    [HarmonyPostfix]
    public static void Postfix(InputEvent evt)
    {
        try
        {
            string toggleSource;
            if (evt is InputEventKey keyEvent)
            {
                if (!TapTracker.Process(
                        keyEvent.Keycode,
                        keyEvent.PhysicalKeycode,
                        keyEvent.Location,
                        keyEvent.Pressed,
                        keyEvent.Echo)) return;

                toggleSource = "Left Shift hotkey";
            }
            else if (evt is InputEventJoypadButton joypadEvent
                     && IsRightStickPress(joypadEvent.ButtonIndex, joypadEvent.Pressed))
            {
                toggleSource = "R3 hotkey";
            }
            else
            {
                return;
            }

            var inputManager = NInputManager.Instance;
            if (inputManager == null || !CanToggle(inputManager)) return;

            // Shift is free in the shipped and current mappings. If the player
            // later assigns it to a game action, the game binding wins and the
            // keyboard shortcut quietly disables itself. R3 is not exposed as
            // a remappable game action, so it cannot collide through this map.
            if (evt is InputEventKey
                && NInputManager.remappableKeyboardInputs.Any(
                    action => inputManager.GetShortcutKey(action) == Key.Shift)) return;

            ViewStatsInjectorPatch.ToggleStatsVisibility(toggleSource);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"StatsVisibilityHotkeyPatch failed: {e.Message}");
        }
    }

    private static bool CanToggle(NInputManager inputManager)
    {
        if (!NGame.IsGameFocusedWindow()) return false;
        if (PlatformUtil.IsPlatformOverlayOpen()) return false;
        if (NGame.Instance?.Transition?.InTransition == true) return false;
        if (NDevConsole.Instance?.Visible == true) return false;

        var viewport = inputManager.GetViewport();
        var focusOwner = viewport?.GuiGetFocusOwner();
        if (focusOwner is LineEdit or TextEdit) return false;

        var tree = viewport?.GetTree();
        if (tree != null && HasActiveInputRebind(tree.Root)) return false;

        return true;
    }

    internal static bool IsRightStickPress(JoyButton buttonIndex, bool pressed)
        => pressed && buttonIndex == JoyButton.RightStick;

    private static bool HasActiveInputRebind(Node? node)
    {
        if (node == null) return false;
        if (node is NInputSettingsPanel panel
            && panel.IsVisibleInTree()
            && panel._listeningEntry != null)
            return true;

        var childCount = node.GetChildCount();
        for (var i = 0; i < childCount; i++)
        {
            if (HasActiveInputRebind(node.GetChild(i)))
                return true;
        }

        return false;
    }
}

/// <summary>
/// Recognizes a tap of Left Shift without claiming Shift-based chords such as
/// Steam's Shift+Tab overlay shortcut. The toggle fires on release only when
/// no other key was pressed during the hold.
/// </summary>
internal sealed class LeftShiftTapTracker
{
    private bool _leftShiftHeld;
    private bool _usedAsModifier;

    public bool Process(
        Key keycode,
        Key physicalKeycode,
        KeyLocation location,
        bool pressed,
        bool echo)
    {
        if (echo) return false;

        if (IsLeftShiftKey(keycode, physicalKeycode, location))
        {
            if (pressed)
            {
                _leftShiftHeld = true;
                _usedAsModifier = false;
                return false;
            }

            var isTap = _leftShiftHeld && !_usedAsModifier;
            _leftShiftHeld = false;
            _usedAsModifier = false;
            return isTap;
        }

        if (_leftShiftHeld && pressed)
            _usedAsModifier = true;

        return false;
    }

    internal static bool IsLeftShiftKey(
        Key keycode,
        Key physicalKeycode,
        KeyLocation location)
    {
        if (location == KeyLocation.Right) return false;
        return keycode == Key.Shift || physicalKeycode == Key.Shift;
    }
}
