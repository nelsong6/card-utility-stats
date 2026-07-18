using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

[HarmonyPatch(typeof(NRelicCollection), "AddRelics")]
public static class RelicCompendiumFilterCollectionAddRelicsPatch
{
    [HarmonyPostfix]
    public static void Postfix(NRelicCollection __instance)
    {
        PatchGuard.Run(nameof(RelicCompendiumFilterCollectionAddRelicsPatch), () =>
        {
            RelicCompendiumFilterUi.ApplyLayoutToCollection(__instance);
            RelicCompendiumFilterUi.ApplyToActiveEntries();
        });
    }
}

[HarmonyPatch(typeof(NRelicCollection), "ClearRelics")]
public static class RelicCompendiumFilterCollectionClearRelicsPatch
{
    [HarmonyPrefix]
    public static void Prefix(NRelicCollection __instance)
    {
        PatchGuard.Run(nameof(RelicCompendiumFilterCollectionClearRelicsPatch), () =>
        {
            RelicCompendiumFilterUi.RestoreCollectionLayout(__instance);
        });
    }
}

internal static class RelicCompendiumFilterContext
{
    public static CompendiumRelicEntryVisualAction GetVisualAction(
        CompendiumRelicFilterMode mode,
        bool isVisibleRelic,
        bool showUndiscoveredRelics,
        bool matchesSelectedCategories)
    {
        if (mode == CompendiumRelicFilterMode.Off)
            return CompendiumRelicEntryVisualAction.Normal;

        if (!isVisibleRelic)
            return showUndiscoveredRelics
                ? CompendiumRelicEntryVisualAction.Normal
                : CompendiumRelicEntryVisualAction.Hidden;

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
    private const string FlatGridName = "SpireLensFlatRelicGrid";
    private const int CategoryTreeColumn = 0;
    private const int MaxVisibleCategoryTreeRows = 8;
    private const float CategoryTreeRowHeight = 24f;
    private const float CategoryTreeVerticalPadding = 8f;
    private const float DimmedAlpha = 0.5f;
    private const int FallbackFlatGridColumns = 8;
    private static readonly string[] CategoryFieldNames =
    [
        "_starter",
        "_common",
        "_uncommon",
        "_rare",
        "_shop",
        "_ancient",
        "_event",
    ];

    private static readonly FieldInfo[] CategoryFields = CategoryFieldNames
        .Select(name => AccessTools.Field(typeof(NRelicCollection), name))
        .Where(field => field != null)
        .Cast<FieldInfo>()
        .ToArray();

    private static readonly FieldInfo? CategoryRelicsContainerField =
        AccessTools.Field(typeof(NRelicCollectionCategory), "_relicsContainer");

    private static readonly FieldInfo? CategorySubCategoriesField =
        AccessTools.Field(typeof(NRelicCollectionCategory), "_subCategories");

    private static readonly List<InjectedPanel> InjectedPanels = new();
    private static readonly List<EntryOriginalVisual> EntryOriginals = new();
    private static readonly List<FlatCollectionLayout> FlatLayouts = new();
    private static readonly HashSet<string> SelectedCategoryIds =
        new(RelicTaxonomy.LeafCategories.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);

    private const CompendiumRelicFilterMode DefaultMode = CompendiumRelicFilterMode.Filter;
    private const bool DefaultShowUndiscoveredRelics = false;
    private const bool DefaultUseSingleRelicGrid = true;
    private const bool DefaultEditCombatRelevance = false;

    private static CompendiumRelicFilterMode _mode = DefaultMode;
    private static bool _showUndiscoveredRelics = DefaultShowUndiscoveredRelics;
    private static bool _useSingleRelicGrid = DefaultUseSingleRelicGrid;
    private static bool _editCombatRelevance = DefaultEditCombatRelevance;
    private static bool _syncingControls;

    internal static bool IsEditingCombatRelevance => _editCombatRelevance;

    public static void Inject(NRelicCollection? collection)
    {
        if (collection == null || !GodotObject.IsInstanceValid(collection)) return;
        CleanupInvalidPanels();

        foreach (var existing in InjectedPanels)
        {
            if (existing.IsFor(collection))
            {
                SyncPanelControls(existing);
                ApplyLayoutToCollection(collection);
                return;
            }
        }

        RemoveExistingPanel(collection);

        var panel = BuildPanel();
        collection.AddChild(panel.Root);
        var injectedPanel = panel with { Collection = collection };
        InjectedPanels.Add(injectedPanel);
        SyncPanelControls(injectedPanel);
        ApplyLayoutToCollection(collection);
        CoreMain.Logger.Info("RelicCompendiumFilter: injected filter panel");
    }

    public static void ReinjectIntoActiveCollections()
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null) return;

            foreach (var collection in FindRelicCollections(tree.Root))
            {
                Inject(collection);
                ApplyLayoutToCollection(collection);
            }
            ApplyToActiveEntries();
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"RelicCompendiumFilter.ReinjectIntoActiveCollections failed: {e}");
        }
    }

    public static void TeardownInjectedUI()
    {
        RestoreAllCollectionLayouts();
        RestoreAllEntries();
        RelicCompendiumClassificationUi.TeardownBadges();

        foreach (var panel in InjectedPanels.ToArray())
            panel.QueueFree();
        InjectedPanels.Clear();
    }

    public static void ApplyToEntry(NRelicCollectionEntry? entry)
    {
        if (entry == null || !GodotObject.IsInstanceValid(entry)) return;
        RememberOriginal(entry);

        var isVisibleRelic = entry.ModelVisibility == ModelVisibility.Visible;
        var matches = false;
        if (isVisibleRelic
            && CompendiumRelicStatsContext.TryGetRelicModel(entry, out var relicModel))
        {
            var relicId = GetRelicId(relicModel);
            matches = RelicTaxonomy.IsRelicInAnySelectedCategory(relicId, SelectedCategoryIds);
        }

        var action = _editCombatRelevance
            ? !isVisibleRelic && !_showUndiscoveredRelics
                ? CompendiumRelicEntryVisualAction.Hidden
                : CompendiumRelicEntryVisualAction.Normal
            : RelicCompendiumFilterContext.GetVisualAction(
                _mode,
                isVisibleRelic,
                _showUndiscoveredRelics,
                matches);

        ApplyVisualAction(entry, action);
        RelicCompendiumClassificationUi.ApplyToEntry(entry, _editCombatRelevance);
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
        _mode = DefaultMode;
        _showUndiscoveredRelics = DefaultShowUndiscoveredRelics;
        _useSingleRelicGrid = DefaultUseSingleRelicGrid;
        _editCombatRelevance = DefaultEditCombatRelevance;
        SelectedCategoryIds.Clear();
        foreach (var category in RelicTaxonomy.LeafCategories)
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

        var editCombatRelevance = new CheckBox
        {
            Name = "EditCombatRelevance",
            Text = "Edit combat relevance",
            MouseFilter = Control.MouseFilterEnum.Stop,
            SizeFlagsHorizontal = Control.SizeFlags.Fill | Control.SizeFlags.Expand,
        };
        editCombatRelevance.AddThemeFontSizeOverride("font_size", 14);
        editCombatRelevance.Connect(
            BaseButton.SignalName.Toggled,
            Callable.From<bool>(OnEditCombatRelevanceToggled));
        vbox.AddChild(editCombatRelevance);

        var editHint = NewLabel(
            "Click a relic or press A to switch Combat / Non-combat.",
            12,
            new Color(0.82f, 0.78f, 0.68f, 1f));
        editHint.Name = "EditCombatRelevanceHint";
        editHint.Visible = false;
        vbox.AddChild(editHint);

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

        var showUndiscovered = new CheckBox
        {
            Name = "ShowUndiscovered",
            Text = "Show undiscovered",
            MouseFilter = Control.MouseFilterEnum.Stop,
            SizeFlagsHorizontal = Control.SizeFlags.Fill | Control.SizeFlags.Expand,
        };
        showUndiscovered.AddThemeFontSizeOverride("font_size", 14);
        showUndiscovered.Connect(
            BaseButton.SignalName.Toggled,
            Callable.From<bool>(OnShowUndiscoveredToggled));
        vbox.AddChild(showUndiscovered);

        var singleRelicGrid = new CheckBox
        {
            Name = "SingleRelicGrid",
            Text = "Single relic grid",
            MouseFilter = Control.MouseFilterEnum.Stop,
            SizeFlagsHorizontal = Control.SizeFlags.Fill | Control.SizeFlags.Expand,
        };
        singleRelicGrid.AddThemeFontSizeOverride("font_size", 14);
        singleRelicGrid.Connect(
            BaseButton.SignalName.Toggled,
            Callable.From<bool>(OnSingleRelicGridToggled));
        vbox.AddChild(singleRelicGrid);

        var categoriesLabel = NewLabel("SpireLens categories", 13, new Color(0.78f, 0.73f, 0.64f, 1f));
        vbox.AddChild(categoriesLabel);

        var categoryTree = new Tree
        {
            Name = "CategoryTree",
            Columns = 1,
            HideRoot = true,
            HideFolding = false,
            ScrollHorizontalEnabled = false,
            ScrollVerticalEnabled = true,
            MouseFilter = Control.MouseFilterEnum.Stop,
            SizeFlagsHorizontal = Control.SizeFlags.Fill | Control.SizeFlags.Expand,
            CustomMinimumSize = new Vector2(
                0f,
                Math.Min(RelicTaxonomy.Categories.Count, MaxVisibleCategoryTreeRows)
                * CategoryTreeRowHeight
                + CategoryTreeVerticalPadding),
        };
        categoryTree.AddThemeFontSizeOverride("font_size", 14);

        var categoryItems = new Dictionary<string, TreeItem>(StringComparer.OrdinalIgnoreCase);
        var hiddenRoot = categoryTree.CreateItem();
        foreach (var category in RelicTaxonomy.RootCategories)
            AddCategoryTreeItem(categoryTree, hiddenRoot, category, categoryItems);

        categoryTree.Connect(
            Tree.SignalName.ItemEdited,
            Callable.From(() => OnCategoryTreeItemEdited(categoryTree, categoryItems)));
        vbox.AddChild(categoryTree);

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

        return new InjectedPanel(
            null,
            root,
            editCombatRelevance,
            editHint,
            modeDropdown,
            showUndiscovered,
            singleRelicGrid,
            categoryItems);
    }

    private static void AddCategoryTreeItem(
        Tree categoryTree,
        TreeItem parentItem,
        RelicTaxonomyCategory category,
        IDictionary<string, TreeItem> categoryItems)
    {
        var item = categoryTree.CreateItem(parentItem);
        item.SetCellMode(CategoryTreeColumn, TreeItem.TreeCellMode.Check);
        item.SetText(CategoryTreeColumn, category.DisplayName);
        item.SetEditable(CategoryTreeColumn, true);
        item.SetSelectable(CategoryTreeColumn, false);
        categoryItems[category.Id] = item;

        foreach (var child in category.Children)
            AddCategoryTreeItem(categoryTree, item, child, categoryItems);

        if (category.Children.Count > 0)
            item.Collapsed = false;
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

    private static void OnEditCombatRelevanceToggled(bool selected)
    {
        if (_syncingControls) return;

        _editCombatRelevance = selected;
        SyncAllControls();
        ApplyToActiveEntries();
    }

    private static void OnCategoryTreeItemEdited(
        Tree categoryTree,
        IReadOnlyDictionary<string, TreeItem> categoryItems)
    {
        if (_syncingControls || categoryTree.GetEditedColumn() != CategoryTreeColumn) return;

        var editedItem = categoryTree.GetEdited();
        if (editedItem == null) return;

        var editedItemId = editedItem.GetInstanceId();
        var categoryId = categoryItems
            .FirstOrDefault(pair => pair.Value.GetInstanceId() == editedItemId)
            .Key;
        if (string.IsNullOrWhiteSpace(categoryId)) return;

        var currentState = RelicTaxonomy.GetSelectionState(categoryId, SelectedCategoryIds);
        RelicTaxonomy.SetCategorySelection(
            SelectedCategoryIds,
            categoryId,
            selected: currentState != RelicTaxonomyCategorySelectionState.Selected);

        SyncAllControls();
        ApplyToActiveEntries();
    }

    private static void OnShowUndiscoveredToggled(bool selected)
    {
        if (_syncingControls) return;

        _showUndiscoveredRelics = selected;
        ApplyToActiveEntries();
    }

    private static void OnSingleRelicGridToggled(bool selected)
    {
        if (_syncingControls) return;

        _useSingleRelicGrid = selected;
        ApplyLayoutToActiveCollections();
        ApplyToActiveEntries();
    }

    private static void SelectAllCategories()
    {
        foreach (var category in RelicTaxonomy.LeafCategories)
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

    public static void ApplyLayoutToCollection(NRelicCollection? collection)
    {
        if (collection == null || !GodotObject.IsInstanceValid(collection)) return;

        try
        {
            if (_useSingleRelicGrid)
                FlattenCollectionLayout(collection);
            else
                RestoreCollectionLayout(collection);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"RelicCompendiumFilter.ApplyLayoutToCollection failed: {e}");
        }
    }

    public static void RestoreCollectionLayout(NRelicCollection? collection)
    {
        if (collection == null) return;

        for (var i = FlatLayouts.Count - 1; i >= 0; i--)
        {
            var layout = FlatLayouts[i];
            if (!layout.IsFor(collection)) continue;

            layout.Restore();
            FlatLayouts.RemoveAt(i);
        }
    }

    private static void ApplyLayoutToActiveCollections()
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null) return;

            foreach (var collection in FindRelicCollections(tree.Root))
                ApplyLayoutToCollection(collection);
            CleanupInvalidFlatLayouts();
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"RelicCompendiumFilter.ApplyLayoutToActiveCollections failed: {e}");
        }
    }

    private static void FlattenCollectionLayout(NRelicCollection collection)
    {
        CleanupInvalidFlatLayouts();

        foreach (var existing in FlatLayouts)
        {
            if (existing.IsFor(collection) && existing.IsValid)
                return;
        }

        RestoreCollectionLayout(collection);

        var categories = GetBuiltInCategories(collection).ToList();
        if (categories.Count == 0) return;

        var firstCategory = categories.FirstOrDefault(category => category.GetParent() != null);
        var hostParent = firstCategory?.GetParent();
        if (hostParent == null || !GodotObject.IsInstanceValid(hostParent)) return;

        RemoveExistingFlatGrid(hostParent);

        var entries = GetEntriesForCategories(categories);
        if (entries.Count == 0) return;

        var firstIndex = firstCategory != null
            ? GetChildIndex(hostParent, firstCategory)
            : hostParent.GetChildCount();

        var flatGrid = new GridContainer
        {
            Name = FlatGridName,
            Columns = GetFlatGridColumns(categories),
            MouseFilter = Control.MouseFilterEnum.Pass,
            SizeFlagsHorizontal = Control.SizeFlags.Fill | Control.SizeFlags.Expand,
            SizeFlagsVertical = Control.SizeFlags.Fill,
        };
        hostParent.AddChild(flatGrid);
        hostParent.MoveChild(flatGrid, Math.Clamp(firstIndex, 0, hostParent.GetChildCount() - 1));

        var categoryStates = categories
            .Select(category => new OriginalCategoryState(category, category.Visible))
            .ToList();
        var entryStates = new List<OriginalEntryState>();

        foreach (var entry in entries)
        {
            var parent = entry.GetParent();
            if (parent == null || !GodotObject.IsInstanceValid(parent)) continue;

            entryStates.Add(new OriginalEntryState(entry, parent, GetChildIndex(parent, entry)));
            parent.RemoveChild(entry);
            flatGrid.AddChild(entry);
        }

        if (entryStates.Count == 0)
        {
            flatGrid.QueueFree();
            return;
        }

        foreach (var categoryState in categoryStates)
            categoryState.Category.Visible = false;

        FlatLayouts.Add(new FlatCollectionLayout(collection, flatGrid, categoryStates, entryStates));
        CoreMain.Logger.Info($"RelicCompendiumFilter: flattened {entryStates.Count} relic entries");
    }

    private static void RestoreAllCollectionLayouts()
    {
        foreach (var layout in FlatLayouts.ToArray())
            layout.Restore();
        FlatLayouts.Clear();
    }

    private static IEnumerable<NRelicCollectionCategory> GetBuiltInCategories(NRelicCollection collection)
    {
        var seen = new HashSet<NRelicCollectionCategory>(ReferenceEqualityComparer.Instance);
        foreach (var field in CategoryFields)
        {
            if (field.GetValue(collection) is not NRelicCollectionCategory category) continue;

            foreach (var found in GetCategoryAndSubcategories(category))
            {
                if (seen.Add(found))
                    yield return found;
            }
        }
    }

    private static IEnumerable<NRelicCollectionCategory> GetCategoryAndSubcategories(
        NRelicCollectionCategory category)
    {
        if (!GodotObject.IsInstanceValid(category)) yield break;

        yield return category;

        if (CategorySubCategoriesField?.GetValue(category) is not IEnumerable<NRelicCollectionCategory> subCategories)
            yield break;

        foreach (var subCategory in subCategories)
        {
            foreach (var found in GetCategoryAndSubcategories(subCategory))
                yield return found;
        }
    }

    private static List<NRelicCollectionEntry> GetEntriesForCategories(
        IEnumerable<NRelicCollectionCategory> categories)
    {
        var result = new List<NRelicCollectionEntry>();
        var seen = new HashSet<NRelicCollectionEntry>(ReferenceEqualityComparer.Instance);

        foreach (var category in categories)
        {
            if (CategoryRelicsContainerField?.GetValue(category) is not GridContainer relicsContainer)
                continue;

            foreach (var entry in FindEntries(relicsContainer))
            {
                if (!GodotObject.IsInstanceValid(entry) || !seen.Add(entry)) continue;
                result.Add(entry);
            }
        }

        return result;
    }

    private static int GetFlatGridColumns(IEnumerable<NRelicCollectionCategory> categories)
    {
        foreach (var category in categories)
        {
            if (CategoryRelicsContainerField?.GetValue(category) is GridContainer relicsContainer
                && relicsContainer.Columns > 0)
            {
                return relicsContainer.Columns;
            }
        }

        return FallbackFlatGridColumns;
    }

    private static void RemoveExistingFlatGrid(Node hostParent)
    {
        for (var i = hostParent.GetChildCount() - 1; i >= 0; i--)
        {
            var child = hostParent.GetChild(i);
            if (!string.Equals(child.Name.ToString(), FlatGridName, StringComparison.Ordinal))
                continue;

            hostParent.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static int GetChildIndex(Node parent, Node child)
    {
        var count = parent.GetChildCount();
        for (var i = 0; i < count; i++)
        {
            if (ReferenceEquals(parent.GetChild(i), child))
                return i;
        }

        return count;
    }

    private static void CleanupInvalidFlatLayouts()
    {
        for (var i = FlatLayouts.Count - 1; i >= 0; i--)
        {
            if (FlatLayouts[i].IsValid) continue;
            FlatLayouts.RemoveAt(i);
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
        CheckBox EditCombatRelevanceCheckbox,
        Label EditCombatRelevanceHint,
        OptionButton ModeDropdown,
        CheckBox ShowUndiscoveredCheckbox,
        CheckBox SingleRelicGridCheckbox,
        IReadOnlyDictionary<string, TreeItem> CategoryItems)
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
            if (EditCombatRelevanceCheckbox != null
                && GodotObject.IsInstanceValid(EditCombatRelevanceCheckbox))
            {
                EditCombatRelevanceCheckbox.SetPressedNoSignal(_editCombatRelevance);
            }

            if (EditCombatRelevanceHint != null
                && GodotObject.IsInstanceValid(EditCombatRelevanceHint))
            {
                EditCombatRelevanceHint.Visible = _editCombatRelevance;
            }

            var selectedIndex = 0;
            for (var i = 0; i < ModeDropdown.ItemCount; i++)
            {
                if (ModeDropdown.GetItemId(i) != (int)_mode) continue;
                selectedIndex = i;
                break;
            }
            ModeDropdown.Selected = selectedIndex;

            if (ShowUndiscoveredCheckbox != null && GodotObject.IsInstanceValid(ShowUndiscoveredCheckbox))
                ShowUndiscoveredCheckbox.SetPressedNoSignal(_showUndiscoveredRelics);

            if (SingleRelicGridCheckbox != null && GodotObject.IsInstanceValid(SingleRelicGridCheckbox))
                SingleRelicGridCheckbox.SetPressedNoSignal(_useSingleRelicGrid);

            foreach (var (categoryId, item) in CategoryItems)
            {
                if (item == null || !GodotObject.IsInstanceValid(item)) continue;

                var state = RelicTaxonomy.GetSelectionState(categoryId, SelectedCategoryIds);

                // Godot can retain a stale mixed state when SetChecked(false)
                // is a no-op, so always clear it before setting the final state.
                item.SetIndeterminate(CategoryTreeColumn, false);
                item.SetChecked(
                    CategoryTreeColumn,
                    state == RelicTaxonomyCategorySelectionState.Selected);
                item.SetIndeterminate(
                    CategoryTreeColumn,
                    state == RelicTaxonomyCategorySelectionState.Partial);
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

    private sealed class FlatCollectionLayout
    {
        private readonly NRelicCollection _collection;
        private readonly GridContainer _flatGrid;
        private readonly IReadOnlyList<OriginalCategoryState> _categories;
        private readonly IReadOnlyList<OriginalEntryState> _entries;

        public FlatCollectionLayout(
            NRelicCollection collection,
            GridContainer flatGrid,
            IReadOnlyList<OriginalCategoryState> categories,
            IReadOnlyList<OriginalEntryState> entries)
        {
            _collection = collection;
            _flatGrid = flatGrid;
            _categories = categories;
            _entries = entries;
        }

        public bool IsValid => GodotObject.IsInstanceValid(_collection);

        public bool IsFor(NRelicCollection collection) => ReferenceEquals(_collection, collection);

        public void Restore()
        {
            foreach (var category in _categories)
                category.Restore();

            foreach (var entry in _entries)
                entry.Restore();

            if (GodotObject.IsInstanceValid(_flatGrid))
                _flatGrid.QueueFree();
        }
    }

    private sealed class OriginalCategoryState
    {
        public OriginalCategoryState(NRelicCollectionCategory category, bool visible)
        {
            Category = category;
            Visible = visible;
        }

        public NRelicCollectionCategory Category { get; }

        private bool Visible { get; }

        public void Restore()
        {
            if (!GodotObject.IsInstanceValid(Category)) return;
            Category.Visible = Visible;
        }
    }

    private sealed class OriginalEntryState
    {
        private readonly NRelicCollectionEntry _entry;
        private readonly Node _parent;
        private readonly int _index;

        public OriginalEntryState(NRelicCollectionEntry entry, Node parent, int index)
        {
            _entry = entry;
            _parent = parent;
            _index = index;
        }

        public void Restore()
        {
            if (!GodotObject.IsInstanceValid(_entry) || !GodotObject.IsInstanceValid(_parent))
                return;

            var currentParent = _entry.GetParent();
            if (!ReferenceEquals(currentParent, _parent))
            {
                currentParent?.RemoveChild(_entry);
                _parent.AddChild(_entry);
            }

            var targetIndex = Math.Clamp(_index, 0, Math.Max(0, _parent.GetChildCount() - 1));
            _parent.MoveChild(_entry, targetIndex);
        }
    }
}
