using System;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens;

namespace SpireLens.Core;

/// <summary>
/// The SpireLens entry in the deck view's sort row: a trigger button injected
/// alongside the game's four sorters, and a dropdown listing the SpireLens
/// metrics the deck can be ordered by.
///
/// Why a plain Godot control instead of a cloned <c>NCardViewSortButton</c>:
/// the game's sort buttons resolve %ButtonImage / %Label / %Image through
/// scene-unique names and share a ShaderMaterial with whatever they were
/// duplicated from. A clone that gets either wrong drives the ORIGINAL
/// button's visuals — the same trap documented on the tickbox clone in
/// <see cref="Patches.ViewStatsInjectorPatch"/>. The deck view already carries
/// a plain injected Button (the SpireLens menu shortcut), so this follows the
/// surface that is known to render here rather than the one that is easy to
/// break invisibly.
///
/// The dropdown lives in its own high CanvasLayer, like
/// <see cref="SpireLensOptionsMenu"/>, so it draws above the card grid without
/// depending on where the sort row sits in the screen's child order.
/// </summary>
internal static class DeckViewSortMenu
{
    private const string ButtonName = "SpireLensSortButton";
    private const string LayerName = "SpireLensDeckSortMenu";
    private const int MenuLayer = 999;
    private const float SlotGap = 16f;
    private const float FallbackSlotWidth = 220f;

    private static Button? _button;
    private static CanvasLayer? _menuLayer;

    /// <summary>
    /// Inject the trigger button into the sort row. Safe to call repeatedly —
    /// each call tears down whatever the previous screen or Core load left
    /// behind first.
    /// </summary>
    internal static void Inject(NDeckViewScreen screen)
    {
        try
        {
            // The run-history deck viewer reuses this same screen type, but its
            // pile is a historical reconstruction; ordering it by the LIVE
            // run's aggregates would be quietly wrong.
            if (RunHistoryDeckViewer.IsHistoricalDeckViewer(screen)) return;

            Teardown();

            var anchor = screen._alphabetSorter;
            if (anchor == null || !GodotObject.IsInstanceValid(anchor))
            {
                CoreMain.Logger.Warn(
                    "DeckViewSortMenu: alphabetical sorter not found — scene structure may have changed.");
                return;
            }

            var parent = anchor.GetParent();
            if (parent == null) return;

            RemoveStale(parent);

            var button = new Button
            {
                Name = ButtonName,
                Text = ButtonText(),
                FocusMode = Control.FocusModeEnum.None,
                MouseDefaultCursorShape = Control.CursorShape.PointingHand,
                CustomMinimumSize = new Vector2(SlotWidth(anchor), 56f),
            };
            button.AddThemeFontSizeOverride("font_size", 20);
            button.Pressed += Toggle;
            parent.AddChild(button);
            _button = button;

            // A Container parent lays its own children out; a plain Control
            // parent does not, so step one slot to the right of the last
            // sorter ourselves. Sorter positions come from the loaded scene,
            // not from a layout pass, so they are valid this early.
            if (parent is not Container)
                button.Position = anchor.Position + new Vector2(SlotStep(screen, anchor), 0f);

            CoreMain.Logger.Info(
                $"DeckViewSortMenu: injected sort trigger (parent={parent.GetType().Name})");
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"DeckViewSortMenu.Inject failed: {e}");
        }
    }

    /// <summary>Called by CoreMain.Shutdown, and before each re-injection.</summary>
    internal static void Teardown()
    {
        Close();

        if (_button != null && GodotObject.IsInstanceValid(_button))
            _button.QueueFree();
        _button = null;
    }

    /// <summary>Re-render the trigger label after the sort state changes.</summary>
    internal static void RefreshButtonText()
    {
        if (_button == null || !GodotObject.IsInstanceValid(_button)) return;
        _button.Text = ButtonText();
    }

    private static string ButtonText()
    {
        var metric = DeckViewSpireLensSort.ActiveMetric;
        return metric == null
            ? "SpireLens  v"
            : $"{metric.Label} {DirectionGlyph()}  v";
    }

    private static string DirectionGlyph()
        => DeckViewSpireLensSort.Descending ? "↓" : "↑";

    private static float SlotWidth(Control anchor)
    {
        var width = anchor.Size.X;
        return width > 1f ? width : FallbackSlotWidth;
    }

    /// <summary>
    /// Distance from one sorter slot to the next, measured from the two
    /// rightmost sorters so the injected button lands on the same rhythm as
    /// the row it joins. Falls back to the anchor's own width when the scene
    /// gives us no usable step.
    /// </summary>
    private static float SlotStep(NDeckViewScreen screen, Control anchor)
    {
        var previous = screen._costSorter;
        if (previous != null && GodotObject.IsInstanceValid(previous))
        {
            var step = anchor.Position.X - previous.Position.X;
            if (step > 1f) return step;
        }

        return SlotWidth(anchor) + SlotGap;
    }

    private static void RemoveStale(Node parent)
    {
        for (var i = parent.GetChildCount() - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i);
            if (!string.Equals(child.Name.ToString(), ButtonName, StringComparison.Ordinal))
                continue;

            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    private static bool IsOpen => _menuLayer != null && GodotObject.IsInstanceValid(_menuLayer);

    private static void Open()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        if (_button == null || !GodotObject.IsInstanceValid(_button)) return;

        var layer = new CanvasLayer { Name = LayerName, Layer = MenuLayer };
        tree.Root.AddChild(layer);
        _menuLayer = layer;

        // Invisible full-screen catcher: clicking anywhere outside the panel
        // dismisses, which is what a dropdown is expected to do.
        var blocker = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        blocker.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        blocker.GuiInput += evt =>
        {
            if (evt is InputEventMouseButton { Pressed: true }) Close();
        };
        layer.AddChild(blocker);

        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        blocker.AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 18);
        margin.AddThemeConstantOverride("margin_right", 18);
        margin.AddThemeConstantOverride("margin_top", 14);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        panel.AddChild(margin);

        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 6);
        margin.AddChild(rows);

        rows.AddChild(NewLabel("Sort deck by", 20, new Color(0.72f, 0.8f, 0.92f)));

        AddRow(
            rows,
            "Off — use the game's sort",
            DeckViewSpireLensSort.ActiveMetric == null,
            () =>
            {
                DeckViewSpireLensSort.Clear("sort menu");
                Close();
                Patches.ViewStatsInjectorPatch.RefreshDeckView();
            });

        foreach (var metric in DeckViewSpireLensSort.Metrics)
        {
            var chosen = metric;
            var active = DeckViewSpireLensSort.IsActive(chosen);
            var label = active
                ? $"{chosen.Label}  {DirectionGlyph()} {(DeckViewSpireLensSort.Descending ? "highest first" : "lowest first")}"
                : chosen.Label;

            AddRow(rows, label, active, () =>
            {
                DeckViewSpireLensSort.Select(chosen, "sort menu");
                Close();
            });
        }

        rows.AddChild(NewLabel(
            "Pick the active metric again to flip the direction.",
            16,
            new Color(0.68f, 0.68f, 0.7f)));

        // The blocker is anchored full-rect at the layer origin, so its local
        // coordinates are viewport coordinates — the trigger's global rect
        // drops straight in.
        var triggerRect = _button.GetGlobalRect();
        panel.Position = new Vector2(triggerRect.Position.X, triggerRect.End.Y + 8f);
    }

    private static void Close()
    {
        if (_menuLayer != null && GodotObject.IsInstanceValid(_menuLayer))
            _menuLayer.QueueFree();
        _menuLayer = null;
    }

    private static void AddRow(VBoxContainer parent, string text, bool active, Action onPressed)
    {
        var row = new Button
        {
            // A leading marker rather than a separate indicator node: the row
            // stays one control, so keyboard/mouse hit areas cannot drift
            // apart from the label.
            Text = active ? $"• {text}" : $"   {text}",
            Flat = true,
            Alignment = HorizontalAlignment.Left,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(360f, 46f),
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        row.AddThemeFontSizeOverride("font_size", 21);
        row.Pressed += onPressed;
        parent.AddChild(row);
    }

    private static Label NewLabel(string text, int fontSize, Color modulate)
    {
        var label = new Label { Text = text, Modulate = modulate };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }
}
