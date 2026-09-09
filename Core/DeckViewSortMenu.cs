using System;
using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens;

namespace SpireLens.Core;

/// <summary>
/// The SpireLens entry in the deck view's sort row: a fifth sort button, and a
/// dropdown listing the SpireLens metrics the deck can be ordered by.
///
/// The trigger is a duplicate of the game's own <c>NCardViewSortButton</c>, so
/// it inherits the row's font, plate art, text placement, hover tweens and
/// direction arrow rather than approximating them. Duplicating that node has
/// two known traps, both handled below:
///
///   - <c>_Ready</c> resolves %ButtonImage / %Label / %Image through
///     scene-unique names, which resolve against a node's <c>Owner</c>.
///     <c>Duplicate()</c> does not reliably carry that relationship, so the
///     clone would otherwise resolve to the ORIGINAL sorter's nodes and drive
///     its visuals. Owner and unique-name flags are restored before insertion.
///   - Materials are Resources and are shared by reference across a
///     duplicate, so the clone's hover tweens would mutate the original
///     button's shader. The clone gets its own material instance.
///
/// Both are the same traps documented on the tickbox clone in
/// <see cref="Patches.ViewStatsInjectorPatch"/>.
///
/// The dropdown itself lives in its own high CanvasLayer, like
/// <see cref="SpireLensOptionsMenu"/>, so it draws above the card grid without
/// depending on where the sort row sits in the screen's child order.
/// </summary>
internal static class DeckViewSortMenu
{
    private const string TriggerName = "SpireLensSorter";
    private const string LayerName = "SpireLensDeckSortMenu";
    private const int MenuLayer = 999;
    private const string IdleLabel = "SpireLens";
    private const string LabelFontName = "font";

    private static NCardViewSortButton? _trigger;
    private static NCardViewSortButton? _anchor;
    private static NDeckViewScreen? _screen;
    private static CanvasLayer? _menuLayer;

    // The sort row's own font, lifted off the anchor sorter's MegaLabel so the
    // dropdown reads as part of the same surface rather than as stock Godot UI.
    private static Font? _rowFont;

    /// <summary>
    /// Add the trigger to the sort row. Safe to call repeatedly — each call
    /// clears whatever the previous screen or Core load left behind. Injected
    /// on both the in-run deck view and the run-history deck viewer.
    /// </summary>
    internal static void Inject(NDeckViewScreen screen)
    {
        try
        {
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

            // Default flags copy structure and scripts; runtime .Connect()
            // handlers are per-instance and are not carried over, so the clone
            // starts with none of the game's sort wiring.
            var clone = (NCardViewSortButton)anchor.Duplicate();
            clone.Name = TriggerName;

            GiveCloneItsOwnMaterial(clone);

            // Owner first, then the unique-name flags: setting the flag is
            // what registers a child in its owner's unique-node table, and
            // _Ready reads that table during AddChild below.
            SetOwnerRecursive(clone, clone);
            CopyUniqueNameFlags(anchor, clone);

            parent.AddChild(clone);
            _trigger = clone;
            _anchor = anchor;
            _screen = screen;

            clone.Connect(
                NClickableControl.SignalName.Released,
                Callable.From<NButton>(OnTriggerReleased));

            // SetLabel/SetHue both touch fields resolved in _Ready, so they
            // have to come after AddChild.
            clone.SetLabel(LabelText());
            SyncArrow();
            if (screen._bg?.Material is ShaderMaterial hue)
                clone.SetHue(hue);

            WireFocusNeighbours(screen, anchor, clone);
            _rowFont = anchor._label?.GetThemeFont(LabelFontName);

            CoreMain.Logger.Info(
                $"DeckViewSortMenu: injected sort trigger (parent={parent.GetType().Name}, "
                + $"historical={RunHistoryDeckViewer.IsHistoricalDeckViewer(screen)})");
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

        // The game wired the alphabetical sorter's right neighbour to itself
        // as the end of the row; put that back before our node disappears.
        if (_anchor != null && GodotObject.IsInstanceValid(_anchor))
            _anchor.FocusNeighborRight = _anchor.GetPath();

        if (_trigger != null && GodotObject.IsInstanceValid(_trigger))
            _trigger.QueueFree();

        _trigger = null;
        _anchor = null;
        _screen = null;
        _rowFont = null;
    }

    /// <summary>Re-render the trigger's label and arrow after a state change.</summary>
    internal static void RefreshButtonText()
    {
        if (_trigger == null || !GodotObject.IsInstanceValid(_trigger)) return;

        _trigger.SetLabel(LabelText());
        SyncArrow();
    }

    /// <summary>
    /// Re-render the deck on the screen this trigger belongs to. The in-run
    /// deck view and the run-history viewer are different instances, so this
    /// cannot go through the injector's live-deck reference.
    /// </summary>
    internal static void RefreshDeckView()
    {
        try
        {
            if (_screen != null && GodotObject.IsInstanceValid(_screen))
                _screen.DisplayCards();
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"DeckViewSortMenu re-render failed: {e.Message}");
        }
    }

    private static string LabelText()
        => DeckViewSpireLensSort.ActiveMetric?.Label ?? IdleLabel;

    private static void OnTriggerReleased(NButton button)
    {
        // NCardViewSortButton.OnRelease flips IsDescending on every click and
        // the arrow follows it. Ours is a menu trigger, not a direction
        // toggle, so put the arrow back on the real sort direction.
        SyncArrow();
        Toggle();
    }

    private static void SyncArrow()
    {
        if (_trigger == null || !GodotObject.IsInstanceValid(_trigger)) return;
        _trigger.IsDescending = DeckViewSpireLensSort.Descending;
    }

    private static void GiveCloneItsOwnMaterial(Node clone)
    {
        if (FindChildByName(clone, "ButtonImage") is CanvasItem { Material: not null } image)
            image.Material = (Material)image.Material.Duplicate();
    }

    private static void WireFocusNeighbours(
        NDeckViewScreen screen,
        Control anchor,
        Control clone)
    {
        // NDeckViewScreen._Ready wires the four stock sorters into a row and
        // never saw ours, so the trigger would be unreachable by controller.
        var below = screen._grid?.DefaultFocusedControl;
        clone.FocusNeighborLeft = anchor.GetPath();
        clone.FocusNeighborRight = clone.GetPath();
        clone.FocusNeighborTop = clone.GetPath();
        clone.FocusNeighborBottom = below != null ? below.GetPath() : clone.GetPath();
        anchor.FocusNeighborRight = clone.GetPath();
    }

    private static void SetOwnerRecursive(Node node, Node owner)
    {
        for (var i = 0; i < node.GetChildCount(); i++)
        {
            var child = node.GetChild(i);
            child.Owner = owner;
            SetOwnerRecursive(child, owner);
        }
    }

    private static void CopyUniqueNameFlags(Node source, Node clone)
    {
        var count = Math.Min(source.GetChildCount(), clone.GetChildCount());
        for (var i = 0; i < count; i++)
        {
            var sourceChild = source.GetChild(i);
            var cloneChild = clone.GetChild(i);
            cloneChild.UniqueNameInOwner = sourceChild.UniqueNameInOwner;
            CopyUniqueNameFlags(sourceChild, cloneChild);
        }
    }

    private static Node? FindChildByName(Node parent, string name)
    {
        for (var i = 0; i < parent.GetChildCount(); i++)
        {
            var child = parent.GetChild(i);
            if (string.Equals(child.Name.ToString(), name, StringComparison.Ordinal))
                return child;

            var found = FindChildByName(child, name);
            if (found != null) return found;
        }

        return null;
    }

    private static void RemoveStale(Node parent)
    {
        for (var i = parent.GetChildCount() - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i);
            if (!string.Equals(child.Name.ToString(), TriggerName, StringComparison.Ordinal))
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
        if (_trigger == null || !GodotObject.IsInstanceValid(_trigger)) return;

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
        // The default panel stylebox is translucent, which put the card grid
        // straight through the menu text. Override it with a solid plate.
        panel.AddThemeStyleboxOverride("panel", NewOpaquePanelStyle());
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
                RefreshDeckView();
            });

        foreach (var metric in DeckViewSpireLensSort.Metrics)
        {
            var chosen = metric;
            var active = DeckViewSpireLensSort.IsActive(chosen);
            var label = active
                ? $"{chosen.Label}  {(DeckViewSpireLensSort.Descending ? "highest first" : "lowest first")}"
                : chosen.Label;

            AddRow(rows, label, active, () =>
            {
                DeckViewSpireLensSort.Select(chosen, "sort menu");
                Close();
            });
        }

        // The blocker is anchored full-rect at the layer origin, so its local
        // coordinates are viewport coordinates — the trigger's global rect
        // drops straight in.
        var triggerRect = _trigger.GetGlobalRect();
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
            // stays one control, so hit area and label cannot drift apart.
            Text = active ? $"• {text}" : $"   {text}",
            Flat = true,
            Alignment = HorizontalAlignment.Left,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(360f, 46f),
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        row.AddThemeFontSizeOverride("font_size", 21);
        if (_rowFont != null) row.AddThemeFontOverride(LabelFontName, _rowFont);
        row.Pressed += onPressed;
        parent.AddChild(row);
    }

    private static Label NewLabel(string text, int fontSize, Color modulate)
    {
        var label = new Label { Text = text, Modulate = modulate };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        if (_rowFont != null) label.AddThemeFontOverride(LabelFontName, _rowFont);
        return label;
    }

    private static StyleBoxFlat NewOpaquePanelStyle()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.07f, 0.09f, 1f),
            BorderColor = new Color(0.46f, 0.42f, 0.34f, 1f),
        };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(6);
        return style;
    }
}
