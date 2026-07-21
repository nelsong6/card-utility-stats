using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SpireLens.Core;

internal sealed record RelicTaxonomyCategory(
    string Id,
    string DisplayName,
    IReadOnlySet<string> RelicIds,
    IReadOnlyList<RelicTaxonomyCategory> Children);

internal enum RelicTaxonomyCategorySelectionState
{
    Unselected,
    Partial,
    Selected,
}

internal static class RelicTaxonomy
{
    private const string EmbeddedFileSuffix = "Config.relic-taxonomy.json";

    public const string ChargeCategoryId = "charge";
    public const string ChargeAcrossCombatsCategoryId = "charge_across_combats";
    public const string ChargeAcrossCombatsCyclingCategoryId = "charge_across_combats_cycling";
    public const string ChargeAcrossCombatsNonCyclingCategoryId = "charge_across_combats_non_cycling";
    public const string ChargeAcrossTurnsCategoryId = "charge_across_turns";
    public const string ChargeResetsEachTurnCategoryId = "charge_resets_each_turn";
    public const string ChargeResetsEachTurnLimitedActivationsCategoryId =
        "charge_resets_each_turn_limited_activations";
    public const string ChargeResetsEachTurnUnlimitedActivationsCategoryId =
        "charge_resets_each_turn_unlimited_activations";

    private static readonly IReadOnlySet<string> EmptyRelicIds =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> RelicIdsByCategory =
        LoadRelicIdsByCategory();

    private static readonly RelicTaxonomyCategory ChargeAcrossTurnsCategory = new(
        ChargeAcrossTurnsCategoryId,
        "Across turns",
        RelicIdsFor(ChargeAcrossTurnsCategoryId),
        []);

    private static readonly RelicTaxonomyCategory ChargeAcrossCombatsCyclingCategory = new(
        ChargeAcrossCombatsCyclingCategoryId,
        "Cycling",
        RelicIdsFor(ChargeAcrossCombatsCyclingCategoryId),
        []);

    private static readonly RelicTaxonomyCategory ChargeAcrossCombatsNonCyclingCategory = new(
        ChargeAcrossCombatsNonCyclingCategoryId,
        "Non-cycling",
        RelicIdsFor(ChargeAcrossCombatsNonCyclingCategoryId),
        []);

    private static readonly RelicTaxonomyCategory ChargeAcrossCombatsCategory = new(
        ChargeAcrossCombatsCategoryId,
        "Across combats",
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        [
            ChargeAcrossCombatsCyclingCategory,
            ChargeAcrossCombatsNonCyclingCategory,
        ]);

    private static readonly RelicTaxonomyCategory ChargeResetsEachTurnLimitedActivationsCategory = new(
        ChargeResetsEachTurnLimitedActivationsCategoryId,
        "Limited activations",
        RelicIdsFor(ChargeResetsEachTurnLimitedActivationsCategoryId),
        []);

    private static readonly RelicTaxonomyCategory ChargeResetsEachTurnUnlimitedActivationsCategory = new(
        ChargeResetsEachTurnUnlimitedActivationsCategoryId,
        "Unlimited activations",
        RelicIdsFor(ChargeResetsEachTurnUnlimitedActivationsCategoryId),
        []);

    private static readonly RelicTaxonomyCategory ChargeResetsEachTurnCategory = new(
        ChargeResetsEachTurnCategoryId,
        "Resets each turn",
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        [
            ChargeResetsEachTurnLimitedActivationsCategory,
            ChargeResetsEachTurnUnlimitedActivationsCategory,
        ]);

    private static readonly RelicTaxonomyCategory ChargeCategory = new(
        ChargeCategoryId,
        "Charge",
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        [
            ChargeAcrossCombatsCategory,
            ChargeAcrossTurnsCategory,
            ChargeResetsEachTurnCategory,
        ]);

    public static IReadOnlyList<RelicTaxonomyCategory> RootCategories { get; } =
    [
        ChargeCategory,
    ];

    public static IReadOnlyList<RelicTaxonomyCategory> Categories { get; } =
        Flatten(RootCategories);

    public static IReadOnlyList<RelicTaxonomyCategory> LeafCategories { get; } =
        Categories.Where(category => category.Children.Count == 0).ToArray();

    static RelicTaxonomy()
    {
        foreach (var category in Categories)
        {
            if (category.Children.Count > 0 && category.RelicIds.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Relic taxonomy group '{category.Id}' cannot own relics directly.");
            }
        }
    }

    public static IReadOnlyList<string> GetSelectableCategoryIds(string? categoryId)
    {
        if (string.IsNullOrWhiteSpace(categoryId)) return Array.Empty<string>();

        var category = Categories.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, categoryId, StringComparison.OrdinalIgnoreCase));
        if (category == null) return Array.Empty<string>();

        return EnumerateLeafCategories(category)
            .Select(leaf => leaf.Id)
            .ToArray();
    }

    public static RelicTaxonomyCategorySelectionState GetSelectionState(
        string categoryId,
        IEnumerable<string> selectedCategoryIds)
    {
        var selectableIds = GetSelectableCategoryIds(categoryId);
        if (selectableIds.Count == 0)
            return RelicTaxonomyCategorySelectionState.Unselected;

        var selected = new HashSet<string>(
            selectedCategoryIds ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        var selectedCount = selectableIds.Count(selected.Contains);

        if (selectedCount == 0)
            return RelicTaxonomyCategorySelectionState.Unselected;
        if (selectedCount == selectableIds.Count)
            return RelicTaxonomyCategorySelectionState.Selected;
        return RelicTaxonomyCategorySelectionState.Partial;
    }

    public static void SetCategorySelection(
        ISet<string> selectedCategoryIds,
        string categoryId,
        bool selected)
    {
        ArgumentNullException.ThrowIfNull(selectedCategoryIds);

        foreach (var group in Categories.Where(candidate => candidate.Children.Count > 0))
            selectedCategoryIds.Remove(group.Id);

        foreach (var selectableId in GetSelectableCategoryIds(categoryId))
        {
            if (selected)
                selectedCategoryIds.Add(selectableId);
            else
                selectedCategoryIds.Remove(selectableId);
        }
    }

    public static bool IsRelicInAnySelectedCategory(
        string? relicId,
        IEnumerable<string> selectedLeafCategoryIds)
    {
        if (string.IsNullOrWhiteSpace(relicId)) return false;

        var selected = new HashSet<string>(
            selectedLeafCategoryIds ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        if (selected.Count == 0) return false;

        return LeafCategories.Any(category =>
            selected.Contains(category.Id)
            && category.RelicIds.Contains(relicId));
    }

    private static IReadOnlySet<string> RelicIdsFor(string categoryId)
    {
        return RelicIdsByCategory.TryGetValue(categoryId, out var relicIds)
            ? relicIds
            : EmptyRelicIds;
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> LoadRelicIdsByCategory()
    {
        try
        {
            var assembly = typeof(RelicTaxonomy).Assembly;
            var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(name =>
                name.EndsWith(EmbeddedFileSuffix, StringComparison.Ordinal));
            if (resourceName == null)
                throw new InvalidOperationException("Embedded relic taxonomy was not found.");

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException("Embedded relic taxonomy could not be opened.");
            var document = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(stream)
                ?? new Dictionary<string, List<string>>();

            return document.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlySet<string>)new HashSet<string>(
                    pair.Value
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Select(id => id.Trim()),
                    StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"Could not read embedded relic taxonomy: {e.Message}");
            return new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static IReadOnlyList<RelicTaxonomyCategory> Flatten(
        IEnumerable<RelicTaxonomyCategory> roots)
    {
        var flattened = new List<RelicTaxonomyCategory>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
            AddCategoryAndDescendants(root, flattened, seenIds);

        return flattened;
    }

    private static void AddCategoryAndDescendants(
        RelicTaxonomyCategory category,
        ICollection<RelicTaxonomyCategory> flattened,
        ISet<string> seenIds)
    {
        if (!seenIds.Add(category.Id))
        {
            throw new InvalidOperationException(
                $"Relic taxonomy category ID '{category.Id}' is duplicated or cyclic.");
        }

        flattened.Add(category);
        foreach (var child in category.Children)
            AddCategoryAndDescendants(child, flattened, seenIds);
    }

    private static IEnumerable<RelicTaxonomyCategory> EnumerateLeafCategories(
        RelicTaxonomyCategory category)
    {
        if (category.Children.Count == 0)
        {
            yield return category;
            yield break;
        }

        foreach (var child in category.Children)
        {
            foreach (var leaf in EnumerateLeafCategories(child))
                yield return leaf;
        }
    }
}
