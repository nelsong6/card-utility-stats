using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.PotionLab;

namespace SpireLens.Core.Patches;

internal enum CompendiumPotionViewMode
{
    Gallery = 0,
    CurrentRun = 1,
}

[HarmonyPatch(typeof(NPotionLab), "_Ready")]
public static class PotionCompendiumHistoryReadyPatch
{
    [HarmonyPostfix]
    public static void Postfix(NPotionLab __instance)
    {
        PatchGuard.Run(nameof(PotionCompendiumHistoryReadyPatch), () =>
        {
            PotionCompendiumHistoryUi.Inject(__instance);
        });
    }
}

[HarmonyPatch(typeof(NPotionLab), nameof(NPotionLab.OnSubmenuOpened))]
public static class PotionCompendiumHistoryOpenedPatch
{
    [HarmonyPostfix]
    public static void Postfix(NPotionLab __instance)
    {
        PatchGuard.Run(nameof(PotionCompendiumHistoryOpenedPatch), () =>
        {
            PotionCompendiumHistoryUi.Inject(__instance);
            PotionCompendiumHistoryUi.ApplyLayout(__instance);
        });
    }
}

[HarmonyPatch(typeof(NPotionLab), "LoadPotions")]
public static class PotionCompendiumHistoryLoadedPatch
{
    [HarmonyPostfix]
    public static void Postfix(NPotionLab __instance)
    {
        PatchGuard.Run(nameof(PotionCompendiumHistoryLoadedPatch), () =>
        {
            PotionCompendiumHistoryUi.ApplyLayout(__instance);
        });
    }
}

[HarmonyPatch(typeof(NPotionLab), "ClearPotions")]
public static class PotionCompendiumHistoryClearPatch
{
    [HarmonyPrefix]
    public static void Prefix(NPotionLab __instance)
    {
        PatchGuard.Run(nameof(PotionCompendiumHistoryClearPatch), () =>
        {
            PotionCompendiumHistoryUi.RestoreLayout(__instance);
        });
    }
}

internal static class PotionCompendiumHistoryUi
{
    private const string PanelName = "SpireLensPotionViewPanel";
    private const string HistoryRootName = "SpireLensPotionRunHistory";
    private static readonly List<InjectedPotionPanel> Panels = new();
    private static readonly List<PotionHistoryLayout> Layouts = new();
    private static CompendiumPotionViewMode _mode = CompendiumPotionViewMode.Gallery;
    private static bool _syncingControls;

    public static void Inject(NPotionLab? lab)
    {
        if (lab == null || !GodotObject.IsInstanceValid(lab)) return;
        CleanupInvalid();

        var existing = Panels.FirstOrDefault(panel => panel.IsFor(lab));
        if (existing != null)
        {
            SyncPanel(existing);
            ApplyLayout(lab);
            return;
        }

        RemoveNamedChild(lab, PanelName);
        var panel = BuildPanel();
        lab.AddChild(panel.Root);
        var injected = panel with { Lab = lab };
        Panels.Add(injected);
        SyncPanel(injected);
        ApplyLayout(lab);
        CoreMain.Logger.Info("PotionCompendiumHistory: injected view dropdown");
    }

    public static void ReinjectIntoActiveLabs()
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null) return;
            foreach (var lab in FindLabs(tree.Root))
                Inject(lab);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"PotionCompendiumHistory reinjection failed: {e}");
        }
    }

    public static void SelectCurrentRunMode()
    {
        _mode = CompendiumPotionViewMode.CurrentRun;
        SyncAllPanels();
        ApplyToActiveLabs();
    }

    public static void TeardownInjectedUi()
    {
        foreach (var layout in Layouts.ToArray())
            layout.Restore();
        Layouts.Clear();

        foreach (var panel in Panels.ToArray())
            panel.QueueFree();
        Panels.Clear();
    }

    public static void ApplyLayout(NPotionLab? lab)
    {
        if (lab == null || !GodotObject.IsInstanceValid(lab)) return;
        if (_mode == CompendiumPotionViewMode.CurrentRun)
            ShowHistory(lab);
        else
            RestoreLayout(lab);
    }

    public static void RestoreLayout(NPotionLab? lab)
    {
        if (lab == null) return;
        for (var i = Layouts.Count - 1; i >= 0; i--)
        {
            var layout = Layouts[i];
            if (!layout.IsFor(lab)) continue;
            layout.Restore();
            Layouts.RemoveAt(i);
        }
    }

    private static InjectedPotionPanel BuildPanel()
    {
        var root = new PanelContainer
        {
            Name = PanelName,
            Position = new Vector2(34f, 126f),
            CustomMinimumSize = new Vector2(218f, 0f),
            ZIndex = 200,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        root.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.055f, 0.049f, 0.043f, 0.84f),
            BorderColor = new Color(0.56f, 0.46f, 0.25f, 0.72f),
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
        });

        var contents = new VBoxContainer();
        contents.AddThemeConstantOverride("separation", 6);
        root.AddChild(contents);
        contents.AddChild(NewLabel(
            "SpireLens potion view",
            16,
            new Color(0.918f, 0.745f, 0.318f)));
        contents.AddChild(NewLabel("Mode", 13, new Color(0.78f, 0.73f, 0.64f)));

        var dropdown = new OptionButton
        {
            Name = "ModeDropdown",
            MouseFilter = Control.MouseFilterEnum.Stop,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        dropdown.AddItem("Potion gallery", (int)CompendiumPotionViewMode.Gallery);
        dropdown.AddItem("Current run stats", (int)CompendiumPotionViewMode.CurrentRun);
        dropdown.Connect(
            OptionButton.SignalName.ItemSelected,
            Callable.From<long>(index => OnModeSelected(dropdown, index)));
        contents.AddChild(dropdown);

        var hint = NewLabel(
            "See offers left behind and the lifecycle of every potion taken this run.",
            12,
            new Color(0.82f, 0.78f, 0.68f));
        hint.Name = "CurrentRunHint";
        hint.Visible = false;
        contents.AddChild(hint);

        return new InjectedPotionPanel(null, root, dropdown, hint);
    }

    private static void OnModeSelected(OptionButton dropdown, long selectedIndex)
    {
        if (_syncingControls) return;
        var selectedId = dropdown.GetItemId((int)selectedIndex);
        _mode = Enum.IsDefined(typeof(CompendiumPotionViewMode), selectedId)
            ? (CompendiumPotionViewMode)selectedId
            : CompendiumPotionViewMode.Gallery;

        SyncAllPanels();
        ApplyToActiveLabs();
    }

    private static void SyncAllPanels()
    {
        CleanupInvalid();
        foreach (var panel in Panels)
            SyncPanel(panel);
    }

    private static void SyncPanel(InjectedPotionPanel panel)
    {
        if (!panel.IsValid) return;
        _syncingControls = true;
        try
        {
            for (var i = 0; i < panel.Dropdown.ItemCount; i++)
            {
                if (panel.Dropdown.GetItemId(i) == (int)_mode)
                {
                    panel.Dropdown.Select(i);
                    break;
                }
            }
            panel.CurrentRunHint.Visible = _mode == CompendiumPotionViewMode.CurrentRun;
        }
        finally
        {
            _syncingControls = false;
        }
    }

    private static void ApplyToActiveLabs()
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null) return;
        foreach (var lab in FindLabs(tree.Root))
            ApplyLayout(lab);
        CleanupInvalid();
    }

    private static void ShowHistory(NPotionLab lab)
    {
        RestoreLayout(lab);
        var categories = GetCategories(lab).ToList();
        var host = categories.FirstOrDefault()?.GetParent();
        if (host == null) return;

        RemoveNamedChild(host, HistoryRootName);
        var categoryStates = categories
            .Select(category => new PotionCategoryState(category, category.Visible))
            .ToList();
        foreach (var category in categories)
            category.Visible = false;

        var root = BuildHistoryRoot();
        host.AddChild(root);
        var firstIndex = categories.Count > 0
            ? categories.Min(category => category.GetIndex())
            : host.GetChildCount() - 1;
        host.MoveChild(root, Math.Max(0, firstIndex));
        Layouts.Add(new PotionHistoryLayout(lab, root, categoryStates));
    }

    private static Control BuildHistoryRoot()
    {
        var entries = RunTracker.GetEffectivePotionHistory(out var outcome)
            .OrderBy(entry => entry.Sequence)
            .ToList();
        var root = new VBoxContainer
        {
            Name = HistoryRootName,
            CustomMinimumSize = new Vector2(1120f, 0f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        root.AddThemeConstantOverride("separation", 14);

        var title = NewLabel("Current run potion history", 25, new Color(0.94f, 0.82f, 0.5f));
        title.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(title);

        if (outcome == "none")
        {
            var noRun = NewLabel("No active or just-completed run is available.", 18, Colors.LightGray);
            noRun.HorizontalAlignment = HorizontalAlignment.Center;
            root.AddChild(noRun);
            return root;
        }

        if (entries.Count == 0)
        {
            var empty = NewLabel("No potion offers or acquisitions have been recorded in this run yet.", 18, Colors.LightGray);
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            root.AddChild(empty);
            return root;
        }

        var lanes = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        lanes.AddThemeConstantOverride("separation", 24);
        root.AddChild(lanes);

        var notTaken = entries.Where(entry => !entry.Acquired).ToList();
        var taken = entries.Where(entry => entry.Acquired).ToList();
        lanes.AddChild(BuildLane("Seen, not taken", notTaken, outcome, leftLane: true));
        lanes.AddChild(BuildLane("Taken / used", taken, outcome, leftLane: false));
        return root;
    }

    private static Control BuildLane(
        string title,
        IReadOnlyCollection<PotionRunHistoryEntry> entries,
        string outcome,
        bool leftLane)
    {
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(530f, 0f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = leftLane
                ? new Color(0.12f, 0.09f, 0.08f, 0.78f)
                : new Color(0.07f, 0.11f, 0.13f, 0.82f),
            BorderColor = leftLane
                ? new Color(0.45f, 0.31f, 0.24f, 0.8f)
                : new Color(0.24f, 0.5f, 0.58f, 0.82f),
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 12,
            ContentMarginBottom = 12,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5,
        });

        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 10);
        panel.AddChild(list);
        var header = NewLabel($"{title}  ·  {entries.Count}", 20, Colors.White);
        header.HorizontalAlignment = HorizontalAlignment.Center;
        list.AddChild(header);

        if (entries.Count == 0)
        {
            var empty = NewLabel("None", 16, new Color(0.65f, 0.65f, 0.65f));
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            list.AddChild(empty);
            return panel;
        }

        foreach (var entry in entries)
            list.AddChild(BuildPotionRow(entry, outcome, leftLane));
        return panel;
    }

    private static Control BuildPotionRow(
        PotionRunHistoryEntry entry,
        string outcome,
        bool leftLane)
    {
        var row = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0f, 92f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        row.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.035f, 0.035f, 0.04f, 0.66f),
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
        });

        var contents = new HBoxContainer();
        contents.AddThemeConstantOverride("separation", 12);
        row.AddChild(contents);

        var icon = new TextureRect
        {
            Texture = GetPotionTexture(entry.PotionId),
            CustomMinimumSize = new Vector2(64f, 64f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        contents.AddChild(icon);

        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 2);
        contents.AddChild(text);
        text.AddChild(NewLabel(
            string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.PotionId : entry.DisplayName,
            18,
            Colors.White));

        if (leftLane)
        {
            text.AddChild(NewLabel(
                $"Seen {FormatLocation(entry.SeenFloor, entry.SeenLocationKind, entry.SeenLocationName)}",
                14,
                new Color(0.83f, 0.76f, 0.68f)));
            text.AddChild(NewLabel(
                $"Not taken · {entry.AcquisitionMethod}",
                14,
                new Color(0.72f, 0.62f, 0.58f)));
            return row;
        }

        text.AddChild(NewLabel(
            $"Acquired {FormatLocation(entry.AcquiredFloor, entry.AcquiredLocationKind, entry.AcquiredLocationName)} · {entry.AcquisitionMethod}",
            14,
            new Color(0.72f, 0.82f, 0.84f)));

        string status;
        Color statusColor;
        if (entry.Used)
        {
            status = $"Used {FormatLocation(entry.UsedFloor, entry.UsedLocationKind, entry.UsedLocationName)}";
            statusColor = new Color(0.45f, 0.85f, 0.92f);
        }
        else if (entry.Discarded)
        {
            status = $"Discarded {FormatLocation(entry.DiscardedFloor, entry.DiscardedLocationKind, entry.DiscardedLocationName)}";
            statusColor = new Color(0.86f, 0.58f, 0.48f);
        }
        else if (entry.HeldAtRunEnd)
        {
            status = entry.HeldAtRunEndFloor.HasValue
                ? $"HELD AT RUN END · Floor {entry.HeldAtRunEndFloor.Value}"
                : "HELD AT RUN END";
            statusColor = new Color(0.95f, 0.76f, 0.3f);
        }
        else
        {
            status = outcome == "in_progress" ? "HELD NOW" : "HELD AT RUN END";
            statusColor = new Color(0.95f, 0.76f, 0.3f);
        }

        text.AddChild(NewLabel(status, 14, statusColor));
        return row;
    }

    private static string FormatLocation(int? floor, string? kind, string? name)
    {
        var parts = new List<string>();
        if (floor.HasValue) parts.Add($"Floor {floor.Value}");
        if (!string.IsNullOrWhiteSpace(kind)) parts.Add(kind!);
        if (!string.IsNullOrWhiteSpace(name)) parts.Add(name!);
        return parts.Count == 0 ? "at an unknown location" : string.Join(" · ", parts);
    }

    private static Texture2D? GetPotionTexture(string potionId)
    {
        try
        {
            return ModelDb
                .GetByIdOrNull<PotionModel>(ModelId.Deserialize(potionId))
                ?.Image;
        }
        catch
        {
            return null;
        }
    }

    private static Label NewLabel(string text, int fontSize, Color color)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static IEnumerable<NPotionLabCategory> GetCategories(NPotionLab lab)
    {
        if (lab._common != null) yield return lab._common;
        if (lab._uncommon != null) yield return lab._uncommon;
        if (lab._rare != null) yield return lab._rare;
        if (lab._special != null) yield return lab._special;
    }

    private static IEnumerable<NPotionLab> FindLabs(Node? node)
    {
        if (node == null) yield break;
        if (node is NPotionLab lab) yield return lab;
        for (var i = 0; i < node.GetChildCount(); i++)
        {
            foreach (var childLab in FindLabs(node.GetChild(i)))
                yield return childLab;
        }
    }

    private static void RemoveNamedChild(Node parent, string name)
    {
        for (var i = parent.GetChildCount() - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i);
            if (!string.Equals(child.Name.ToString(), name, StringComparison.Ordinal)) continue;
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static void CleanupInvalid()
    {
        for (var i = Panels.Count - 1; i >= 0; i--)
            if (!Panels[i].IsValid) Panels.RemoveAt(i);
        for (var i = Layouts.Count - 1; i >= 0; i--)
            if (!Layouts[i].IsValid) Layouts.RemoveAt(i);
    }

    private sealed record InjectedPotionPanel(
        NPotionLab? Lab,
        PanelContainer Root,
        OptionButton Dropdown,
        Label CurrentRunHint)
    {
        public bool IsValid => Lab != null
            && GodotObject.IsInstanceValid(Lab)
            && GodotObject.IsInstanceValid(Root)
            && GodotObject.IsInstanceValid(Dropdown);

        public bool IsFor(NPotionLab lab) => IsValid && ReferenceEquals(Lab, lab);

        public void QueueFree()
        {
            if (GodotObject.IsInstanceValid(Root)) Root.QueueFree();
        }
    }

    private sealed class PotionHistoryLayout
    {
        private readonly NPotionLab _lab;
        private readonly Control _root;
        private readonly IReadOnlyList<PotionCategoryState> _categories;

        public PotionHistoryLayout(
            NPotionLab lab,
            Control root,
            IReadOnlyList<PotionCategoryState> categories)
        {
            _lab = lab;
            _root = root;
            _categories = categories;
        }

        public bool IsValid => GodotObject.IsInstanceValid(_lab);
        public bool IsFor(NPotionLab lab) => ReferenceEquals(_lab, lab);

        public void Restore()
        {
            if (GodotObject.IsInstanceValid(_root)) _root.QueueFree();
            foreach (var state in _categories)
                state.Restore();
        }
    }

    private sealed record PotionCategoryState(NPotionLabCategory Category, bool Visible)
    {
        public void Restore()
        {
            if (GodotObject.IsInstanceValid(Category)) Category.Visible = Visible;
        }
    }
}
