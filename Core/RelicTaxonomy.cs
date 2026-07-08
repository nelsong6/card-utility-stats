using System;
using System.Collections.Generic;
using System.Linq;

namespace SpireLens.Core;

internal sealed record RelicTaxonomyCategory(
    string Id,
    string DisplayName,
    IReadOnlySet<string> RelicIds);

internal static class RelicTaxonomy
{
    public const string EnergyCategoryId = "energy";

    private static readonly RelicTaxonomyCategory EnergyCategory = new(
        EnergyCategoryId,
        "Energy relics",
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RELIC.ART_OF_WAR",
            "RELIC.BLOOD_SOAKED_ROSE",
            "RELIC.BOOMING_CONCH",
            "RELIC.BOOKMARK",
            "RELIC.BRILLIANT_SCARF",
            "RELIC.CANDELABRA",
            "RELIC.CHANDELIER",
            "RELIC.ECTOPLASM",
            "RELIC.FAKE_HAPPY_FLOWER",
            "RELIC.FAKE_VENERABLE_TEA_SET",
            "RELIC.GREMLIN_HORN",
            "RELIC.HAPPY_FLOWER",
            "RELIC.ICE_CREAM",
            "RELIC.LANTERN",
            "RELIC.MUMMIFIED_HAND",
            "RELIC.NUNCHAKU",
            "RELIC.PHILOSOPHERS_STONE",
            "RELIC.PRISMATIC_GEM",
            "RELIC.SOZU",
            "RELIC.VELVET_CHOKER",
            "RELIC.VENERABLE_TEA_SET",
            "RELIC.VERY_HOT_COCOA",
        });

    public static IReadOnlyList<RelicTaxonomyCategory> Categories { get; } =
    [
        EnergyCategory,
    ];

    public static bool IsRelicInAnySelectedCategory(
        string? relicId,
        IEnumerable<string> selectedCategoryIds)
    {
        if (string.IsNullOrWhiteSpace(relicId)) return false;

        var selected = new HashSet<string>(
            selectedCategoryIds ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        if (selected.Count == 0) return false;

        return Categories.Any(category =>
            selected.Contains(category.Id)
            && category.RelicIds.Contains(relicId));
    }
}
