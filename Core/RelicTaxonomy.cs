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

internal sealed record LoadedRelicTaxonomy(
    IReadOnlySet<string> UncategorizedRelicIds,
    IReadOnlyDictionary<string, IReadOnlySet<string>> CategorizedRelicIds);

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

    private static readonly LoadedRelicTaxonomy TaxonomyData = LoadTaxonomy();

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> RelicIdsByCategory =
        TaxonomyData.CategorizedRelicIds;

    public static IReadOnlySet<string> UncategorizedRelicIds => TaxonomyData.UncategorizedRelicIds;

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

    private static LoadedRelicTaxonomy LoadTaxonomy()
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
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Relic taxonomy root must be an object.");

            if (!root.TryGetProperty("uncategorized", out var uncategorizedElement))
                throw new InvalidOperationException("Relic taxonomy is missing 'uncategorized'.");
            if (!root.TryGetProperty("charge", out var chargeElement))
                throw new InvalidOperationException("Relic taxonomy is missing 'charge'.");

            var unknownRootKeys = root.EnumerateObject()
                .Where(property => property.Name is not "uncategorized" and not "charge")
                .Select(property => property.Name)
                .ToArray();
            if (unknownRootKeys.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Relic taxonomy has unknown root key(s): {string.Join(", ", unknownRootKeys)}.");
            }

            var uncategorized = ReadRelicIds(uncategorizedElement, "uncategorized");
            var categorized = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);
            CollectCategoryLeaves(chargeElement, ChargeCategoryId, categorized);

            var requiredLeafIds = new[]
            {
                ChargeAcrossCombatsCyclingCategoryId,
                ChargeAcrossCombatsNonCyclingCategoryId,
                ChargeAcrossTurnsCategoryId,
                ChargeResetsEachTurnLimitedActivationsCategoryId,
                ChargeResetsEachTurnUnlimitedActivationsCategoryId,
            };
            var missingLeafIds = requiredLeafIds.Where(id => !categorized.ContainsKey(id)).ToArray();
            if (missingLeafIds.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Relic taxonomy is missing leaf category/categories: {string.Join(", ", missingLeafIds)}.");
            }

            ValidateExclusiveRelicPlacement(uncategorized, categorized);
            return new LoadedRelicTaxonomy(uncategorized, categorized);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"Could not read embedded relic taxonomy: {e.Message}");
            return new LoadedRelicTaxonomy(
                EmptyRelicIds,
                new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private static void CollectCategoryLeaves(
        JsonElement element,
        string categoryId,
        IDictionary<string, IReadOnlySet<string>> destination)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            destination[categoryId] = ReadRelicIds(element, categoryId);
            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Relic taxonomy category '{categoryId}' must be an object or array.");
        }

        foreach (var child in element.EnumerateObject())
            CollectCategoryLeaves(child.Value, $"{categoryId}_{child.Name}", destination);
    }

    private static IReadOnlySet<string> ReadRelicIds(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Relic taxonomy '{path}' must be an array.");

        var relicIds = element.EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null)
            .ToArray();
        if (relicIds.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException($"Relic taxonomy '{path}' contains a blank or non-string relic ID.");

        var normalized = relicIds.Select(id => id!).ToArray();
        var distinct = new HashSet<string>(normalized, StringComparer.OrdinalIgnoreCase);
        if (distinct.Count != normalized.Length)
            throw new InvalidOperationException($"Relic taxonomy '{path}' contains a duplicate relic ID.");

        if (!normalized.SequenceEqual(normalized.OrderBy(id => id, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidOperationException($"Relic taxonomy '{path}' must be alphabetically sorted.");

        return distinct;
    }

    private static void ValidateExclusiveRelicPlacement(
        IReadOnlySet<string> uncategorized,
        IReadOnlyDictionary<string, IReadOnlySet<string>> categorized)
    {
        var placements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Register(string path, IEnumerable<string> relicIds)
        {
            foreach (var relicId in relicIds)
            {
                if (placements.TryGetValue(relicId, out var previousPath))
                {
                    throw new InvalidOperationException(
                        $"Relic taxonomy lists '{relicId}' in both '{previousPath}' and '{path}'.");
                }

                placements[relicId] = path;
            }
        }

        Register("uncategorized", uncategorized);
        foreach (var category in categorized)
            Register(category.Key, category.Value);
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
