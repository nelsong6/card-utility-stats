using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.UI;
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
    private const string SelectorName = "SpireLensPotionViewPanel";
    private const string HistoryRootName = "SpireLensPotionRunHistory";
    private static readonly List<InjectedPotionSelector> Selectors = new();
    private static readonly List<PotionHistoryLayout> Layouts = new();
    private static CompendiumPotionViewMode _mode = CompendiumPotionViewMode.Gallery;
    private static bool _syncingControls;

    public static void Inject(NPotionLab? lab)
    {
        if (lab == null || !GodotObject.IsInstanceValid(lab)) return;
        CleanupInvalid();

        var existing = Selectors.FirstOrDefault(selector => selector.IsFor(lab));
        if (existing != null)
        {
            SyncSelector(existing);
            ApplyLayout(lab);
            return;
        }

        RemoveNamedChild(lab, SelectorName);
        var selector = BuildSelector();
        lab.AddChild(selector.Root);
        var injected = selector with { Lab = lab };
        Selectors.Add(injected);
        SyncSelector(injected);
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
        SyncAllSelectors();
        ApplyToActiveLabs();
    }

    public static void TeardownInjectedUi()
    {
        foreach (var layout in Layouts.ToArray())
            layout.Restore();
        Layouts.Clear();

        foreach (var selector in Selectors.ToArray())
            selector.QueueFree();
        Selectors.Clear();
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

    private static InjectedPotionSelector BuildSelector()
    {
        var root = new VBoxContainer
        {
            Name = SelectorName,
            Position = new Vector2(34f, 126f),
            CustomMinimumSize = new Vector2(218f, 0f),
            ZIndex = 200,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };

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
        root.AddChild(dropdown);

        return new InjectedPotionSelector(null, root, dropdown);
    }

    private static void OnModeSelected(OptionButton dropdown, long selectedIndex)
    {
        if (_syncingControls) return;
        var selectedId = dropdown.GetItemId((int)selectedIndex);
        _mode = Enum.IsDefined(typeof(CompendiumPotionViewMode), selectedId)
            ? (CompendiumPotionViewMode)selectedId
            : CompendiumPotionViewMode.Gallery;

        SyncAllSelectors();
        ApplyToActiveLabs();
    }

    private static void SyncAllSelectors()
    {
        CleanupInvalid();
        foreach (var selector in Selectors)
            SyncSelector(selector);
    }

    private static void SyncSelector(InjectedPotionSelector selector)
    {
        if (!selector.IsValid) return;
        _syncingControls = true;
        try
        {
            for (var i = 0; i < selector.Dropdown.ItemCount; i++)
            {
                if (selector.Dropdown.GetItemId(i) != (int)_mode) continue;
                selector.Dropdown.Select(i);
                break;
            }
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

        var root = BuildTimeline();
        host.AddChild(root);
        var firstIndex = categories.Count > 0
            ? categories.Min(category => category.GetIndex())
            : host.GetChildCount() - 1;
        host.MoveChild(root, Math.Max(0, firstIndex));
        Layouts.Add(new PotionHistoryLayout(lab, root, categoryStates));
    }

    private static Control BuildTimeline()
    {
        var entries = RunTracker.GetEffectivePotionHistory(out var outcome)
            .OrderBy(entry => entry.Sequence)
            .ToList();
        var timeline = new VBoxContainer
        {
            Name = HistoryRootName,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        timeline.AddChild(NewLabel("Current run potion timeline"));

        if (outcome == "none")
        {
            timeline.AddChild(NewLabel("No active or just-completed run is available."));
            return timeline;
        }

        if (entries.Count == 0)
        {
            timeline.AddChild(NewLabel("No potions have been seen in this run yet."));
            return timeline;
        }

        for (var i = 0; i < entries.Count; i++)
        {
            timeline.AddChild(BuildTimelineEntry(entries[i], outcome));
            if (i < entries.Count - 1)
                timeline.AddChild(new HSeparator());
        }
        return timeline;
    }

    private static Control BuildTimelineEntry(PotionRunHistoryEntry entry, string outcome)
    {
        var row = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };

        var holder = CreateNativePotionHolder(entry.PotionId);
        if (holder != null)
            row.AddChild(holder);

        var details = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        row.AddChild(details);
        details.AddChild(NewLabel(string.IsNullOrWhiteSpace(entry.DisplayName)
            ? entry.PotionId
            : entry.DisplayName));
        details.AddChild(NewLabel(GetStatus(entry, outcome)));

        if (!entry.Acquired)
        {
            details.AddChild(NewLabel(
                $"Seen: {FormatLocation(entry.SeenFloor, entry.SeenLocationKind, entry.SeenLocationName)}"));
            details.AddChild(NewLabel($"Method: {entry.AcquisitionMethod}"));
            return row;
        }

        details.AddChild(NewLabel(
            $"Acquired: {FormatLocation(entry.AcquiredFloor, entry.AcquiredLocationKind, entry.AcquiredLocationName)}"));
        details.AddChild(NewLabel($"Method: {entry.AcquisitionMethod}"));

        if (entry.Used)
        {
            details.AddChild(NewLabel(
                $"Used: {FormatLocation(entry.UsedFloor, entry.UsedLocationKind, entry.UsedLocationName)}"));
        }
        else if (entry.Discarded)
        {
            details.AddChild(NewLabel(
                $"Discarded: {FormatLocation(entry.DiscardedFloor, entry.DiscardedLocationKind, entry.DiscardedLocationName)}"));
        }
        else if (entry.HeldAtRunEnd)
        {
            details.AddChild(NewLabel(entry.HeldAtRunEndFloor.HasValue
                ? $"Held at run end: Floor {entry.HeldAtRunEndFloor.Value}"
                : "Held at run end"));
        }

        return row;
    }

    private static string GetStatus(PotionRunHistoryEntry entry, string outcome)
    {
        if (!entry.Acquired) return "Seen, not taken";
        if (entry.Used) return "Used";
        if (entry.Discarded) return "Discarded";
        if (entry.HeldAtRunEnd || outcome != "in_progress") return "Held at run end";
        return "Held now";
    }

    private static NLabPotionHolder? CreateNativePotionHolder(string potionId)
    {
        try
        {
            var potion = ModelDb.GetByIdOrNull<PotionModel>(ModelId.Deserialize(potionId));
            return potion == null
                ? null
                : NLabPotionHolder.Create(potion.ToMutable(), ModelVisibility.Visible);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"PotionCompendiumHistory could not create native holder for {potionId}: {e}");
            return null;
        }
    }

    private static string FormatLocation(int? floor, string? kind, string? name)
    {
        var parts = new List<string>();
        if (floor.HasValue) parts.Add($"Floor {floor.Value}");
        if (!string.IsNullOrWhiteSpace(kind)) parts.Add(kind!);
        if (!string.IsNullOrWhiteSpace(name)) parts.Add(name!);
        return parts.Count == 0 ? "Unknown location" : string.Join(" · ", parts);
    }

    private static Label NewLabel(string text)
        => new()
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

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
        for (var i = Selectors.Count - 1; i >= 0; i--)
            if (!Selectors[i].IsValid) Selectors.RemoveAt(i);
        for (var i = Layouts.Count - 1; i >= 0; i--)
            if (!Layouts[i].IsValid) Layouts.RemoveAt(i);
    }

    private sealed record InjectedPotionSelector(
        NPotionLab? Lab,
        VBoxContainer Root,
        OptionButton Dropdown)
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
