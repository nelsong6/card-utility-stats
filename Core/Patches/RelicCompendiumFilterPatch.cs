using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.RelicCollection;

namespace SpireLens.Core.Patches;

internal enum CompendiumRelicFilterMode
{
    Off = 0,
    Compare = 1,
    Filter = 2,
}

internal enum CompendiumRelicEntryVisualAction
{
    Normal,
    Dim,
    Hidden,
}

[HarmonyPatch(typeof(NRelicCollection), "_Ready")]
public static class RelicCompendiumFilterCollectionReadyPatch
{
    [HarmonyPostfix]
    public static void Postfix(NRelicCollection __instance)
    {
        PatchGuard.Run(nameof(RelicCompendiumFilterCollectionReadyPatch), () =>
        {
            RelicCompendiumFilterUi.Inject(__instance);
        });
    }
}

[HarmonyPatch(typeof(NRelicCollection), "OnSubmenuOpened")]
public static class RelicCompendiumFilterCollectionOpenedPatch
{
    [HarmonyPostfix]
    public static void Postfix(NRelicCollection __instance)
    {
        PatchGuard.Run(nameof(RelicCompendiumFilterCollectionOpenedPatch), () =>
        {
            RelicCompendiumFilterUi.Inject(__instance);
            RelicCompendiumFilterUi.ApplyToActiveEntries();
        });
    }
}

internal static class RelicCompendiumFilterContext
{
    public static CompendiumRelicEntryVisualAction GetVisualAction(
        CompendiumRelicFilterMode mode,
        bool canApplyCategoryVisuals,
        bool matchesSelectedCategories)
    {
        if (!canApplyCategoryVisuals || mode == CompendiumRelicFilterMode.Off)
            return CompendiumRelicEntryVisualAction.Normal;

        if (matchesSelectedCategories)
            return CompendiumRelicEntryVisualAction.Normal;

        return mode == CompendiumRelicFilterMode.Filter
            ? CompendiumRelicEntryVisualAction.Hidden
            : CompendiumRelicEntryVisualAction.Dim;
    }
}

internal static class RelicCompendiumFilterUi
{
    private const string PanelName = "SpireLensRelicFilterPanel";
    private const float DimmedAlpha = 0.5f;

    private static readonly List<InjectedPanel> InjectedPanels = new();
    private static readonly List<EntryOriginalVisual> EntryOriginals = new();
    private static readonly HashSet<string> SelectedCategoryIds =
        new(RelicTaxonomy.Categories.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);

    private static CompendiumRelicFilterMode _mode = CompendiumRelicFilterMode.Off;
    private static bool _syncingControls;

    public static void Inject(NRelicCollection? collection)
    {
        if (collection == null || !GodotObject.IsInstanceValid(collection)) return;
        CleanupInvalidPanels();

        foreach (var existing in InjectedPanels)
        {
            if (existing.IsFor(collection))
            {
                SyncPanelControls(existing);
                return;
            }
        }

        RemoveExistingPanel(collection);

        var panel = BuildPanel();
        collection.AddChild(panel.Root);
        var injectedPanel = panel with { Collection = collection };
        InjectedPanels.Add(injectedPanel);
        SyncPanelControls(injectedPanel);
        CoreMain.Logger.Info("RelicCompendiumFilter: injected filter panel");
    }

    public static void ReinjectIntoActiveCollections()
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null) return;

            foreach (var collection in FindRelicCollections(tree.Root))
                Inject(collection);
            ApplyToActiveEntries();
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"RelicCompendiumFilter.ReinjectIntoActiveCollections failed: {e}");
        }
    }

    public static void TeardownInjectedUI()
    {
        RestoreAllEntries();

        foreach (var panel in InjectedPanels.ToArray())
            panel.QueueFree();
        InjectedPanels.Clear();
    }

    public static void ApplyToEntry(NRelicCollectionEntry? entry)
    {
        if (entry == null || !GodotObject.IsInstanceValid(entry)) return;
        RememberOriginal(entry);

        var canApplyCategoryVisuals = entry.ModelVisibility == ModelVisibility.Visible;
        var matches = false;
        if (canApplyCategoryVisuals
            && CompendiumRelicStatsContext.TryGetRelicModel(entry, out var relicModel))
        {
            var relicId = GetRelicId(relicModel);
            matches = RelicTaxonomy.IsRelicInAnySelectedCategory(relicId, SelectedCategoryIds);
        }

        var action = RelicCompendiumFilterContext.GetVisualAction(
            _mode,
            canApplyCategoryVisuals,
            matches);

        ApplyVisualAction(entry, action);
    }

    public static void ApplyToActiveEntries()
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null) return;

            foreach (var entry in FindEntries(tree.Root))
                ApplyToEntry(entry);
            CleanupInvalidEntryOriginals();
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"RelicCompendiumFilter.ApplyToActiveEntries failed: {e}");
        }
    }

    internal static void ResetForTests()
    {
        _mode = CompendiumRelicFilterMode.Off;
        SelectedCategoryIds.Clear();
        foreach (var category in RelicTaxonomy.Categories)
            SelectedCategoryIds.Add(category.Id);
    }

    private static InjectedPanel BuildPanel()
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

        var vbox = new VBoxContainer
        {
            Name = "Contents",
            MouseFilter = Control.MouseFilterEnum.Pass,
            SizeFlagsHorizontal = Control.SizeFlags.Fill | Control.SizeFlags.Expand,
        };
        vbox.AddThemeConstantOverride("separation", 6);
        root.AddChild(vbox);

        var title = NewLabel("SpireLens relic view", 16, new Color(0.918f, 0.745f, 0.318f, 1f));
        vbox.AddChild(title);

        var modeLabel = NewLabel("Mode", 13, new Color(0.78f, 0.73f, 0.64f, 1f));
        vbox.AddChild(modeLabel);

        var modeDropdown = new OptionButton
        {
            Name = "ModeDropdown",
            MouseFilter = Control.MouseFilterEnum.Stop,
            SizeFlagsHorizontal = Control.SizeFlags.Fill | Control.SizeFlags.Expand,
        };
        modeDropdown.AddItem("Off", (int)CompendiumRelicFilterMode.Off);
        modeDropdown.AddItem("Compare", (int)CompendiumRelicFilterMode.Compare);
        modeDropdown.AddItem("Filter", (int)CompendiumRelicFilterMode.Filter);
        modeDropdown.Connect(
            OptionButton.SignalName.ItemSelected,
            Callable.From<long>(index => OnModeSelected(modeDropdown, index)));
        vbox.AddChild(modeDropdown);

        var categoriesLabel = NewLabel("Categories", 13, new Color(0.78f, 0.73f, 0.64f, 1f));
        vbox.AddChild(categoriesLabel);

        var checkboxByCategory = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in RelicTaxonomy.Categories)
        {
            var checkbox = new CheckBox
            {
                Name = $"Category_{category.Id}",
                Text = category.DisplayName,
                MouseFilter = Control.MouseFilterEnum.Stop,
                SizeFlagsHorizontal = Control.SizeFlags.Fill | Control.SizeFlags.Expand,
            };
            checkbox.AddThemeFontSizeOverride("font_size", 14);
            checkbox.Connect(
                BaseButton.SignalName.Toggled,
                Callable.From<bool>(pressed => OnCategoryToggled(category.Id, pressed)));
            checkboxByCategory[category.Id] = checkbox;
            vbox.AddChild(checkbox);
        }

        var buttons = new HBoxContainer
        {
            Name = "BulkButtons",
            MouseFilter = Control.MouseFilterEnum.Pass,
            SizeFlagsHorizontal = Control.SizeFlags.Fill | Control.SizeFlags.Expand,
        };
        buttons.AddThemeConstantOverride("separation", 6);
        vbox.AddChild(buttons);

        var selectAll = NewButton("Select All");
        selectAll.Connect(BaseButton.SignalName.Pressed, Callable.From(SelectAllCategories));
        buttons.AddChild(selectAll);

        var clearAll = NewButton("Clear All");
        clearAll.Connect(BaseButton.SignalName.Pressed, Callable.From(ClearAllCategories));
        buttons.AddChild(clearAll);

        return new InjectedPanel(null, root, modeDropdown, checkboxByCategory);
    }

    private static Label NewLabel(string text, int fontSize, Color color)
    {
        var label = new Label
        {
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.Fill | Control.SizeFlags.Expand,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static Button NewButton(string text)
    {
        var button = new Button
        {
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Stop,
            SizeFlagsHorizontal = Control.SizeFlags.Fill | Control.SizeFlags.Expand,
            CustomMinimumSize = new Vector2(0f, 28f),
        };
        button.AddThemeFontSizeOverride("font_size", 13);
        return button;
    }

    private static void OnModeSelected(OptionButton modeDropdown, long selectedIndex)
    {
        if (_syncingControls) return;

        var selectedId = modeDropdown.GetItemId((int)selectedIndex);
        _mode = Enum.IsDefined(typeof(CompendiumRelicFilterMode), selectedId)
            ? (CompendiumRelicFilterMode)selectedId
            : CompendiumRelicFilterMode.Off;

        ApplyToActiveEntries();
    }

    private static void OnCategoryToggled(string categoryId, bool selected)
    {
        if (_syncingControls) return;

        if (selected)
            SelectedCategoryIds.Add(categoryId);
        else
            SelectedCategoryIds.Remove(categoryId);

        ApplyToActiveEntries();
    }

    private static void SelectAllCategories()
    {
        foreach (var category in RelicTaxonomy.Categories)
            SelectedCategoryIds.Add(category.Id);
        SyncAllControls();
        ApplyToActiveEntries();
    }

    private static void ClearAllCategories()
    {
        SelectedCategoryIds.Clear();
        SyncAllControls();
        ApplyToActiveEntries();
    }

    private static void SyncAllControls()
    {
        _syncingControls = true;
        try
        {
            foreach (var panel in InjectedPanels)
                panel.SyncFromState();
        }
        finally
        {
            _syncingControls = false;
        }
    }

    private static void SyncPanelControls(InjectedPanel panel)
    {
        _syncingControls = true;
        try
        {
            panel.SyncFromState();
        }
        finally
        {
            _syncingControls = false;
        }
    }

    private static void ApplyVisualAction(
        NRelicCollectionEntry entry,
        CompendiumRelicEntryVisualAction action)
    {
        var original = RememberOriginal(entry);
        switch (action)
        {
            case CompendiumRelicEntryVisualAction.Hidden:
                entry.Modulate = original.Modulate with { A = 1f };
                entry.Visible = false;
                StatsTooltip.HideIfAnchoredTo(entry);
                break;
            case CompendiumRelicEntryVisualAction.Dim:
                entry.Visible = original.Visible;
                entry.Modulate = original.Modulate with { A = original.Modulate.A * DimmedAlpha };
                break;
            default:
                entry.Visible = original.Visible;
                entry.Modulate = original.Modulate;
                break;
        }
    }

    private static EntryOriginalVisual RememberOriginal(NRelicCollectionEntry entry)
    {
        CleanupInvalidEntryOriginals();

        foreach (var original in EntryOriginals)
        {
            if (original.IsFor(entry))
                return original;
        }

        var added = new EntryOriginalVisual(entry, entry.Modulate, entry.Visible);
        EntryOriginals.Add(added);
        return added;
    }

    private static void RestoreAllEntries()
    {
        foreach (var original in EntryOriginals.ToArray())
            original.Restore();
        EntryOriginals.Clear();
    }

    private static string GetRelicId(RelicModel relicModel)
    {
        try
        {
            return RelicHoverShowPatch.GetStatsAggregateId(relicModel);
        }
        catch
        {
            return relicModel.Id.ToString();
        }
    }

    private static void RemoveExistingPanel(NRelicCollection collection)
    {
        for (var i = collection.GetChildCount() - 1; i >= 0; i--)
        {
            var child = collection.GetChild(i);
            if (!string.Equals(child.Name.ToString(), PanelName, StringComparison.Ordinal))
                continue;

            collection.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static void CleanupInvalidPanels()
    {
        for (var i = InjectedPanels.Count - 1; i >= 0; i--)
        {
            if (InjectedPanels[i].IsValid) continue;
            InjectedPanels.RemoveAt(i);
        }
    }

    private static void CleanupInvalidEntryOriginals()
    {
        for (var i = EntryOriginals.Count - 1; i >= 0; i--)
        {
            if (EntryOriginals[i].IsValid) continue;
            EntryOriginals.RemoveAt(i);
        }
    }

    private static IEnumerable<NRelicCollection> FindRelicCollections(Node? node)
    {
        if (node == null) yield break;
        if (node is NRelicCollection collection && GodotObject.IsInstanceValid(collection))
            yield return collection;

        var count = node.GetChildCount();
        for (var i = 0; i < count; i++)
        {
            foreach (var child in FindRelicCollections(node.GetChild(i)))
                yield return child;
        }
    }

    private static IEnumerable<NRelicCollectionEntry> FindEntries(Node? node)
    {
        if (node == null) yield break;
        if (node is NRelicCollectionEntry entry && GodotObject.IsInstanceValid(entry))
            yield return entry;

        var count = node.GetChildCount();
        for (var i = 0; i < count; i++)
        {
            foreach (var childEntry in FindEntries(node.GetChild(i)))
                yield return childEntry;
        }
    }

    private sealed record InjectedPanel(
        NRelicCollection? Collection,
        PanelContainer Root,
        OptionButton ModeDropdown,
        IReadOnlyDictionary<string, CheckBox> Checkboxes)
    {
        public bool IsValid =>
            Root != null
            && GodotObject.IsInstanceValid(Root)
            && Collection != null
            && GodotObject.IsInstanceValid(Collection);

        public bool IsFor(NRelicCollection collection) =>
            Collection != null && ReferenceEquals(Collection, collection);

        public void QueueFree()
        {
            if (Root != null && GodotObject.IsInstanceValid(Root))
                Root.QueueFree();
        }

        public void SyncFromState()
        {
            var selectedIndex = 0;
            for (var i = 0; i < ModeDropdown.ItemCount; i++)
            {
                if (ModeDropdown.GetItemId(i) != (int)_mode) continue;
                selectedIndex = i;
                break;
            }
            ModeDropdown.Selected = selectedIndex;

            foreach (var (categoryId, checkbox) in Checkboxes)
            {
                if (checkbox == null || !GodotObject.IsInstanceValid(checkbox)) continue;
                checkbox.SetPressedNoSignal(SelectedCategoryIds.Contains(categoryId));
            }
        }
    }

    private sealed class EntryOriginalVisual
    {
        private readonly NRelicCollectionEntry _entry;

        public EntryOriginalVisual(NRelicCollectionEntry entry, Color modulate, bool visible)
        {
            _entry = entry;
            Modulate = modulate;
            Visible = visible;
        }

        public Color Modulate { get; }

        public bool Visible { get; }

        public bool IsValid => GodotObject.IsInstanceValid(_entry);

        public bool IsFor(NRelicCollectionEntry entry) => ReferenceEquals(_entry, entry);

        public void Restore()
        {
            if (!IsValid) return;
            _entry.Modulate = Modulate;
            _entry.Visible = Visible;
        }
    }
}
