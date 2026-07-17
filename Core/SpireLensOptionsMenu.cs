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
    private static readonly List<CheckBox> Checkboxes = new();

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
        Checkboxes[0].GrabFocus();
        StatsTooltip.Hide();
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

        int optionIndex = evt switch
        {
            InputEventKey { Pressed: true, Echo: false, Keycode: Key.Key1 } => 0,
            InputEventKey { Pressed: true, Echo: false, Keycode: Key.Key2 } => 1,
            InputEventKey { Pressed: true, Echo: false, Keycode: Key.Key3 } => 2,
            InputEventKey { Pressed: true, Echo: false, Keycode: Key.Key4 } => 3,
            InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.A } => 0,
            InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.X } => 1,
            InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.Y } => 2,
            InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.B } => 3,
            _ => -1,
        };

        if (optionIndex < 0) return false;
        ToggleOption(optionIndex, "menu shortcut");
        return true;
    }

    public static void Destroy()
    {
        if (_layer != null && GodotObject.IsInstanceValid(_layer))
            _layer.QueueFree();
        _layer = null;
        Checkboxes.Clear();
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
            CustomMinimumSize = new Vector2(720, 520),
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

        var help = NewLabel("Choose what SpireLens shows. Press RS or Left Shift to close.", 18);
        help.HorizontalAlignment = HorizontalAlignment.Center;
        help.Modulate = new Color(0.82f, 0.82f, 0.82f);
        rows.AddChild(help);

        AddOption(rows, "SpireLens: on/off", "1", "A", 0);
        AddOption(rows, "SpireLens: card stats", "2", "X", 1);
        AddOption(rows, "Show monster stats", "3", "Y", 2);
        AddOption(rows, "Show removed cards", "4", "B", 3);

        var close = new Button
        {
            Text = "Close  —  RS / Left Shift",
            CustomMinimumSize = new Vector2(0, 56),
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
        string keyboardShortcut,
        string gamepadShortcut,
        int index)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 20);
        parent.AddChild(row);

        var checkbox = new CheckBox
        {
            Text = text,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 54),
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        checkbox.AddThemeFontSizeOverride("font_size", 23);
        checkbox.Toggled += enabled => SetOption(index, enabled, "menu checkbox");
        Checkboxes.Add(checkbox);
        row.AddChild(checkbox);

        var shortcut = NewLabel($"[{keyboardShortcut}]   [{gamepadShortcut}]", 21);
        shortcut.VerticalAlignment = VerticalAlignment.Center;
        shortcut.CustomMinimumSize = new Vector2(120, 0);
        shortcut.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(shortcut);
    }

    private static void ToggleOption(int index, string source)
    {
        RefreshCheckboxes();
        SetOption(index, !Checkboxes[index].ButtonPressed, source);
        RefreshCheckboxes();
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
                ViewStatsInjectorPatch.SetShowRemovedCardsEnabled(enabled, source);
                break;
        }
    }

    private static void RefreshCheckboxes()
    {
        if (Checkboxes.Count != 4) return;
        Checkboxes[0].SetPressedNoSignal(ViewStatsInjectorPatch.StatsVisibilityEnabled);
        Checkboxes[1].SetPressedNoSignal(ViewStatsInjectorPatch.CardStatsEnabled);
        Checkboxes[2].SetPressedNoSignal(ViewStatsInjectorPatch.EnemyStatsEnabled);
        Checkboxes[3].SetPressedNoSignal(ViewStatsInjectorPatch.ShowRemovedCardsEnabled);
    }
}
