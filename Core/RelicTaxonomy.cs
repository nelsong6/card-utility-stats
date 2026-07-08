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
    public const string ChargeCategoryId = "charge";

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

    private static readonly RelicTaxonomyCategory ChargeCategory = new(
        ChargeCategoryId,
        "Charge relics",
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RELIC.BOOK_OF_FIVE_RINGS",
            "RELIC.BRILLIANT_SCARF",
            "RELIC.DIAMOND_DIADEM",
            "RELIC.FAKE_HAPPY_FLOWER",
            "RELIC.FISHING_ROD",
            "RELIC.GALACTIC_DUST",
            "RELIC.GIRYA",
            "RELIC.HAPPY_FLOWER",
            "RELIC.IRON_CLUB",
            "RELIC.JOSS_PAPER",
            "RELIC.KUNAI",
            "RELIC.KUSARIGAMA",
            "RELIC.LASTING_CANDY",
            "RELIC.LETTER_OPENER",
            "RELIC.METRONOME",
            "RELIC.NUNCHAKU",
            "RELIC.ORNAMENTAL_FAN",
            "RELIC.PAELS_FLESH",
            "RELIC.PAELS_LEGION",
            "RELIC.PAELS_TOOTH",
            "RELIC.PAELS_WING",
            "RELIC.PENDULUM",
            "RELIC.PEN_NIB",
            "RELIC.POCKETWATCH",
            "RELIC.POLLINOUS_CORE",
            "RELIC.PUMPKIN_CANDLE",
            "RELIC.RAINBOW_RING",
            "RELIC.SHURIKEN",
            "RELIC.SILVER_CRUCIBLE",
            "RELIC.STONE_CALENDAR",
            "RELIC.SWORD_OF_STONE",
            "RELIC.TOY_BOX",
            "RELIC.TUNING_FORK",
            "RELIC.VELVET_CHOKER",
            "RELIC.WINGED_BOOTS",
            "RELIC.WONGOS_MYSTERY_TICKET",
        });

    public static IReadOnlyList<RelicTaxonomyCategory> Categories { get; } =
    [
        EnergyCategory,
        ChargeCategory,
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
