using System;
using System.Collections.Generic;
using Godot;
using SpireLens.Core.Patches;

namespace SpireLens.Core;

/// <summary>
/// Global modal options surface. It lives in a high CanvasLayer under the
/// scene root so it remains available above every game screen and consumes
/// normal mouse/controller input while visible.
/// </summary>
public static class SpireLensOptionsMenu
{
    private const int Layer = 1000;
    private static CanvasLayer? _layer;
    private static readonly List<Button> Checkboxes = new();
    private static readonly List<Button> CheckboxIndicators = new();
    private static readonly List<Panel> SelectionHighlights = new();
    private static int _selectedIndex;
    private static int _leftStickVerticalDirection;
    private static int _leftStickHorizontalDirection;
    private static bool _leftTriggerPressed;

    public static bool IsOpen =>
        _layer != null && GodotObject.IsInstanceValid(_layer) && _layer.Visible;

    public static void Toggle(string source)
    {
        if (IsOpen)
            Close(source);
        else
            Open(source);
    }

    public static void Open(string source)
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null) return;

        if (_layer == null || !GodotObject.IsInstanceValid(_layer))
            Build(tree);

        RefreshCheckboxes();
        _layer!.Visible = true;
        _selectedIndex = Math.Clamp(_selectedIndex, 0, Checkboxes.Count - 1);
        _leftStickVerticalDirection = 0;
        _leftStickHorizontalDirection = 0;
        _leftTriggerPressed = false;
        RefreshSelectionHighlight();
        CoreMain.Logger.Info($"SpireLens options menu opened ({source})");
    }

    public static void Close(string source)
    {
        if (_layer == null || !GodotObject.IsInstanceValid(_layer)) return;
        _layer.Visible = false;
        CoreMain.Logger.Info($"SpireLens options menu closed ({source})");
    }

    public static bool HandleShortcut(InputEvent evt)
    {
        if (!IsOpen) return false;

        if (evt is InputEventKey { Pressed: true, Echo: false } key
            && (key.Keycode == Key.Escape || key.PhysicalKeycode == Key.Escape))
        {
            Close("Escape key");
            return true;
        }

        if (evt is InputEventJoypadMotion motion)
            return HandleLeftStick(motion);

        if (evt is InputEventJoypadButton { Pressed: true } button)
        {
            switch (button.ButtonIndex)
            {
                case JoyButton.DpadUp:
                    MoveFocus(-1);
                    return true;
                case JoyButton.DpadDown:
                    MoveFocus(1);
                    return true;
                case JoyButton.DpadLeft:
                    JumpToEdge(first: true);
                    return true;
                case JoyButton.DpadRight:
                    JumpToEdge(first: false);
                    return true;
                case JoyButton.LeftShoulder:
                    JumpToEdge(first: true);
                    return true;
                case JoyButton.A:
                    ToggleOption(_selectedIndex, "menu confirm");
                    return true;
                default:
                    Close("menu controller button");
                    return true;
            }
        }
        return false;
    }

    public static void Destroy()
    {
        if (_layer != null && GodotObject.IsInstanceValid(_layer))
            _layer.QueueFree();
        _layer = null;
        Checkboxes.Clear();
        CheckboxIndicators.Clear();
        SelectionHighlights.Clear();
        _selectedIndex = 0;
        _leftStickVerticalDirection = 0;
        _leftStickHorizontalDirection = 0;
        _leftTriggerPressed = false;
    }

    private static void Build(SceneTree tree)
    {
        _layer = new CanvasLayer
        {
            Name = "SpireLensOptionsMenu",
            Layer = Layer,
            Visible = false,
        };
        tree.Root.AddChild(_layer);

        var blocker = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.72f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        blocker.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(blocker);

        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        blocker.AddChild(center);

        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(780, 790),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        center.AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 44);
        margin.AddThemeConstantOverride("margin_right", 44);
        margin.AddThemeConstantOverride("margin_top", 34);
        margin.AddThemeConstantOverride("margin_bottom", 34);
        panel.AddChild(margin);

        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 18);
        margin.AddChild(rows);

        var title = NewLabel("SpireLens Options", 32);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        rows.AddChild(title);

        var help = NewLabel("Up/Down selects • A toggles • Left/Right or LB/LT jumps • Esc closes", 18);
        help.HorizontalAlignment = HorizontalAlignment.Center;
        help.Modulate = new Color(0.82f, 0.82f, 0.82f);
        rows.AddChild(help);

        AddOption(rows, "SpireLens: on/off", 0);
        AddOption(rows, "SpireLens: card stats", 1);
        AddOption(rows, "Show monster stats", 2);
        AddOption(rows, "Show cards not in deck", 3);
        AddOption(rows, "Show all meta-cards in \"not in deck\" view", 4);

        var relicFilterHeader = NewLabel("Relic bar filter — choose one", 20);
        relicFilterHeader.Modulate = new Color(0.72f, 0.8f, 0.92f);
        rows.AddChild(relicFilterHeader);
        AddOption(rows, "Show combat-only relics at the combat screen", 5);
        AddOption(rows, "Force show only combat relics on all screens", 6);

        var close = new Button
        {
            Text = "Close  —  RS / Left Shift / Esc",
            CustomMinimumSize = new Vector2(0, 56),
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        close.AddThemeFontSizeOverride("font_size", 20);
        close.Pressed += () => Close("menu button");
        rows.AddChild(close);
    }

    private static Label NewLabel(string text, int fontSize)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }

    private static void AddOption(
        VBoxContainer parent,
        string text,
        int index)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 20);
        parent.AddChild(row);

        var indicator = CreateCheckboxIndicator();
        indicator.MouseEntered += () => SetSelectedIndex(index);
        indicator.Pressed += () =>
        {
            SetSelectedIndex(index);
            ToggleOption(index, "menu checkbox icon");
        };
        CheckboxIndicators.Add(indicator);
        row.AddChild(indicator);

        var optionHost = new Control
        {
            CustomMinimumSize = new Vector2(0, 54),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        row.AddChild(optionHost);

        var selectionHighlight = new Panel
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        selectionHighlight.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        SelectionHighlights.Add(selectionHighlight);
        optionHost.AddChild(selectionHighlight);

        var checkbox = new Button
        {
            Text = text,
            ToggleMode = true,
            Flat = true,
            Alignment = HorizontalAlignment.Left,
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        checkbox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        checkbox.AddThemeFontSizeOverride("font_size", 23);
        checkbox.Toggled += enabled =>
        {
            SetSelectedIndex(index);
            UpdateIndicator(index, enabled);
            SetOption(index, enabled, "menu checkbox");
        };
        checkbox.MouseEntered += () => SetSelectedIndex(index);
        Checkboxes.Add(checkbox);
        optionHost.AddChild(checkbox);

    }

    private static Button CreateCheckboxIndicator()
    {
        var border = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.06f, 0.08f, 0.9f),
            BorderColor = new Color(0.9f, 0.9f, 0.9f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3,
        };

        var indicator = new Button
        {
            Text = "",
            CustomMinimumSize = new Vector2(34, 34),
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        indicator.AddThemeFontSizeOverride("font_size", 24);
        indicator.AddThemeStyleboxOverride("normal", border);
        indicator.AddThemeStyleboxOverride("hover", (StyleBoxFlat)border.Duplicate());
        indicator.AddThemeStyleboxOverride("pressed", (StyleBoxFlat)border.Duplicate());
        indicator.AddThemeStyleboxOverride("focus", (StyleBoxFlat)border.Duplicate());
        return indicator;
    }

    private static void ToggleOption(int index, string source)
    {
        RefreshCheckboxes();
        SetOption(index, !Checkboxes[index].ButtonPressed, source);
        RefreshCheckboxes();
    }

    private static void MoveFocus(int delta)
    {
        if (Checkboxes.Count == 0) return;
        SetSelectedIndex((_selectedIndex + delta + Checkboxes.Count) % Checkboxes.Count);
    }

    private static void JumpToEdge(bool first)
    {
        if (Checkboxes.Count == 0) return;
        SetSelectedIndex(first ? 0 : Checkboxes.Count - 1);
    }

    private static void SetSelectedIndex(int index)
    {
        if (Checkboxes.Count == 0) return;
        _selectedIndex = Math.Clamp(index, 0, Checkboxes.Count - 1);
        RefreshSelectionHighlight();
    }

    private static void RefreshSelectionHighlight()
    {
        if (Checkboxes.Count != SelectionHighlights.Count) return;

        for (var i = 0; i < SelectionHighlights.Count; i++)
        {
            var highlight = SelectionHighlights[i];
            if (!GodotObject.IsInstanceValid(highlight)) continue;

            if (i == _selectedIndex)
            {
                // Reuse the game's Button focus art without giving this modal
                // Godot focus. The gameplay control beneath the overlay keeps
                // its exact focus, highlight, and hover state.
                highlight.AddThemeStyleboxOverride(
                    "panel",
                    Checkboxes[i].GetThemeStylebox("focus"));
                highlight.Visible = true;
            }
            else
            {
                highlight.Visible = false;
            }
        }
    }

    private static bool HandleLeftStick(InputEventJoypadMotion motion)
    {
        const float deadZone = 0.55f;

        if (motion.Axis == JoyAxis.LeftY)
        {
            var direction = motion.AxisValue > deadZone ? 1 : motion.AxisValue < -deadZone ? -1 : 0;
            if (direction == 0)
            {
                _leftStickVerticalDirection = 0;
            }
            else if (direction != _leftStickVerticalDirection)
            {
                _leftStickVerticalDirection = direction;
                MoveFocus(direction);
            }
            return true;
        }

        if (motion.Axis == JoyAxis.LeftX)
        {
            var direction = motion.AxisValue > deadZone ? 1 : motion.AxisValue < -deadZone ? -1 : 0;
            if (direction == 0)
            {
                _leftStickHorizontalDirection = 0;
            }
            else if (direction != _leftStickHorizontalDirection)
            {
                _leftStickHorizontalDirection = direction;
                JumpToEdge(first: direction < 0);
            }
            return true;
        }

        if (motion.Axis == JoyAxis.TriggerLeft)
        {
            var pressed = motion.AxisValue > deadZone;
            if (pressed && !_leftTriggerPressed)
                JumpToEdge(first: false);
            _leftTriggerPressed = pressed;
            return true;
        }

        return false;
    }

    private static void SetOption(int index, bool enabled, string source)
    {
        switch (index)
        {
            case 0:
                ViewStatsInjectorPatch.SetStatsVisibilityEnabled(enabled, source);
                break;
            case 1:
                ViewStatsInjectorPatch.SetCardStatsEnabled(enabled, source);
                break;
            case 2:
                ViewStatsInjectorPatch.SetEnemyStatsEnabled(enabled, source);
                break;
            case 3:
                ViewStatsInjectorPatch.SetShowCardsNotInDeckEnabled(enabled, source);
                break;
            case 4:
                ViewStatsInjectorPatch.SetShowAllMetaCardsInNotInDeckView(
                    enabled,
                    source);
                break;
            case 5:
                ViewStatsInjectorPatch.SetShowCombatOnlyRelicsAtCombatScreen(enabled, source);
                break;
            case 6:
                ViewStatsInjectorPatch.SetHideNonCombatRelicStats(enabled, source);
                break;
        }
    }

    private static void RefreshCheckboxes()
    {
        if (Checkboxes.Count != 7) return;
        SetCheckboxState(0, ViewStatsInjectorPatch.StatsVisibilityEnabled);
        SetCheckboxState(1, ViewStatsInjectorPatch.CardStatsEnabled);
        SetCheckboxState(2, ViewStatsInjectorPatch.EnemyStatsEnabled);
        SetCheckboxState(3, ViewStatsInjectorPatch.ShowCardsNotInDeckEnabled);
        SetCheckboxState(
            4,
            ViewStatsInjectorPatch.ShowAllMetaCardsInNotInDeckView);
        SetCheckboxState(5, ViewStatsInjectorPatch.ShowCombatOnlyRelicsAtCombatScreen);
        SetCheckboxState(6, ViewStatsInjectorPatch.HideNonCombatRelicStats);
    }

    private static void SetCheckboxState(int index, bool enabled)
    {
        Checkboxes[index].SetPressedNoSignal(enabled);
        UpdateIndicator(index, enabled);
    }

    private static void UpdateIndicator(int index, bool enabled)
    {
        if (index < 0 || index >= CheckboxIndicators.Count) return;
        CheckboxIndicators[index].Text = enabled ? "✓" : "";
    }
}
