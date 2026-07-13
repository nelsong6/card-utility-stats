using System;
using System.Collections.Generic;
using System.Linq;

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

    private static readonly RelicTaxonomyCategory ChargeAcrossTurnsCategory = new(
        ChargeAcrossTurnsCategoryId,
        "Across turns",
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RELIC.METRONOME",
            "RELIC.PAELS_FLESH",
            "RELIC.PAELS_LEGION",
            "RELIC.STONE_CALENDAR",
        },
        []);

    private static readonly RelicTaxonomyCategory ChargeAcrossCombatsCyclingCategory = new(
        ChargeAcrossCombatsCyclingCategoryId,
        "Cycling",
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RELIC.BOOK_OF_FIVE_RINGS",
            "RELIC.FAKE_HAPPY_FLOWER",
            "RELIC.FISHING_ROD",
            "RELIC.GALACTIC_DUST",
            "RELIC.HAPPY_FLOWER",
            "RELIC.IRON_CLUB",
            "RELIC.JOSS_PAPER",
            "RELIC.LASTING_CANDY",
            "RELIC.NUNCHAKU",
            "RELIC.PAELS_WING",
            "RELIC.PENDULUM",
            "RELIC.PEN_NIB",
            "RELIC.POLLINOUS_CORE",
            "RELIC.TOY_BOX",
            "RELIC.TUNING_FORK",
        },
        []);

    private static readonly RelicTaxonomyCategory ChargeAcrossCombatsNonCyclingCategory = new(
        ChargeAcrossCombatsNonCyclingCategoryId,
        "Non-cycling",
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RELIC.GIRYA",
            "RELIC.PAELS_TOOTH",
            "RELIC.PUMPKIN_CANDLE",
            "RELIC.SILVER_CRUCIBLE",
            "RELIC.SWORD_OF_STONE",
            "RELIC.WINGED_BOOTS",
            "RELIC.WONGOS_MYSTERY_TICKET",
        },
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
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RELIC.BRILLIANT_SCARF",
            "RELIC.DIAMOND_DIADEM",
            "RELIC.POCKETWATCH",
            "RELIC.RAINBOW_RING",
            "RELIC.VELVET_CHOKER",
        },
        []);

    private static readonly RelicTaxonomyCategory ChargeResetsEachTurnUnlimitedActivationsCategory = new(
        ChargeResetsEachTurnUnlimitedActivationsCategoryId,
        "Unlimited activations",
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RELIC.KUNAI",
            "RELIC.KUSARIGAMA",
            "RELIC.LETTER_OPENER",
            "RELIC.ORNAMENTAL_FAN",
            "RELIC.SHURIKEN",
        },
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
