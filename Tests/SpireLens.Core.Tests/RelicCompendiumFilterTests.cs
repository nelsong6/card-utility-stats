using System;
using System.Collections.Generic;
using System.Linq;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class RelicCompendiumFilterTests
{
    [Fact]
    public void CombatRelevanceTurn_IsAnExclusiveCutoff()
    {
        Assert.True(RelicBarFilterPatch.IsBeforeCombatRelevanceCutoff(0, 2));
        Assert.True(RelicBarFilterPatch.IsBeforeCombatRelevanceCutoff(1, 2));
        Assert.False(RelicBarFilterPatch.IsBeforeCombatRelevanceCutoff(2, 2));
        Assert.False(RelicBarFilterPatch.IsBeforeCombatRelevanceCutoff(3, 2));
        Assert.True(RelicBarFilterPatch.IsBeforeCombatRelevanceCutoff(3, null));
    }

    [Fact]
    public void Taxonomy_OmitsRemovedEnergyCategory()
    {
        Assert.DoesNotContain(RelicTaxonomy.Categories, category => category.Id == "energy");
        Assert.DoesNotContain(RelicTaxonomy.Categories, category => category.DisplayName == "Energy relics");
    }

    [Fact]
    public void ChargeTaxonomy_NestsCombatAndTurnResetGroups()
    {
        var charge = RelicTaxonomy.RootCategories.Single();

        Assert.Equal(RelicTaxonomy.ChargeCategoryId, charge.Id);
        Assert.Equal("Charge", charge.DisplayName);
        Assert.Empty(charge.RelicIds);
        Assert.Equal(
            new[]
            {
                RelicTaxonomy.ChargeAcrossCombatsCategoryId,
                RelicTaxonomy.ChargeAcrossTurnsCategoryId,
                RelicTaxonomy.ChargeResetsEachTurnCategoryId,
            },
            charge.Children.Select(category => category.Id).ToArray());
        Assert.Equal(
            new[] { "Across combats", "Across turns", "Resets each turn" },
            charge.Children.Select(category => category.DisplayName).ToArray());

        var acrossCombats = charge.Children.Single(
            category => category.Id == RelicTaxonomy.ChargeAcrossCombatsCategoryId);

        Assert.Empty(acrossCombats.RelicIds);
        Assert.Equal(
            new[]
            {
                RelicTaxonomy.ChargeAcrossCombatsCyclingCategoryId,
                RelicTaxonomy.ChargeAcrossCombatsNonCyclingCategoryId,
            },
            acrossCombats.Children.Select(category => category.Id).ToArray());
        Assert.Equal(
            new[] { "Cycling", "Non-cycling" },
            acrossCombats.Children.Select(category => category.DisplayName).ToArray());

        var resetsEachTurn = charge.Children.Single(
            category => category.Id == RelicTaxonomy.ChargeResetsEachTurnCategoryId);

        Assert.Empty(resetsEachTurn.RelicIds);
        Assert.Equal(
            new[]
            {
                RelicTaxonomy.ChargeResetsEachTurnLimitedActivationsCategoryId,
                RelicTaxonomy.ChargeResetsEachTurnUnlimitedActivationsCategoryId,
            },
            resetsEachTurn.Children.Select(category => category.Id).ToArray());
        Assert.Equal(
            new[] { "Limited activations", "Unlimited activations" },
            resetsEachTurn.Children.Select(category => category.DisplayName).ToArray());
        Assert.Equal(
            new[]
            {
                RelicTaxonomy.ChargeAcrossCombatsCyclingCategoryId,
                RelicTaxonomy.ChargeAcrossCombatsNonCyclingCategoryId,
                RelicTaxonomy.ChargeAcrossTurnsCategoryId,
                RelicTaxonomy.ChargeResetsEachTurnLimitedActivationsCategoryId,
                RelicTaxonomy.ChargeResetsEachTurnUnlimitedActivationsCategoryId,
            },
            RelicTaxonomy.LeafCategories.Select(category => category.Id).ToArray());
    }

    [Fact]
    public void ChargeAcrossTurnsTaxonomy_IncludesOnlyCombatLocalCounters()
    {
        var charge = RelicTaxonomy.LeafCategories.Single(
            c => c.Id == RelicTaxonomy.ChargeAcrossTurnsCategoryId);

        Assert.Equal(
            new[]
            {
                "RELIC.METRONOME",
                "RELIC.PAELS_FLESH",
                "RELIC.PAELS_LEGION",
                "RELIC.STONE_CALENDAR",
            },
            charge.RelicIds.OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void ChargeAcrossCombatsCyclingTaxonomy_IncludesRestartingCounters()
    {
        var charge = RelicTaxonomy.LeafCategories.Single(
            c => c.Id == RelicTaxonomy.ChargeAcrossCombatsCyclingCategoryId);

        Assert.Equal(
            new[]
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
            charge.RelicIds.OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void ChargeAcrossCombatsNonCyclingTaxonomy_IncludesNonWrappingCounters()
    {
        var charge = RelicTaxonomy.LeafCategories.Single(
            c => c.Id == RelicTaxonomy.ChargeAcrossCombatsNonCyclingCategoryId);

        Assert.Equal(
            new[]
            {
                "RELIC.GIRYA",
                "RELIC.PAELS_TOOTH",
                "RELIC.PUMPKIN_CANDLE",
                "RELIC.SILVER_CRUCIBLE",
                "RELIC.SWORD_OF_STONE",
                "RELIC.WINGED_BOOTS",
                "RELIC.WONGOS_MYSTERY_TICKET",
            },
            charge.RelicIds.OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void ChargeResetsEachTurnLimitedActivationsTaxonomy_IncludesCappedRelics()
    {
        var charge = RelicTaxonomy.LeafCategories.Single(
            c => c.Id == RelicTaxonomy.ChargeResetsEachTurnLimitedActivationsCategoryId);

        Assert.Equal(
            new[]
            {
                "RELIC.BRILLIANT_SCARF",
                "RELIC.DIAMOND_DIADEM",
                "RELIC.POCKETWATCH",
                "RELIC.RAINBOW_RING",
                "RELIC.VELVET_CHOKER",
            },
            charge.RelicIds.OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void ChargeResetsEachTurnUnlimitedActivationsTaxonomy_IncludesRepeatableRelics()
    {
        var charge = RelicTaxonomy.LeafCategories.Single(
            c => c.Id == RelicTaxonomy.ChargeResetsEachTurnUnlimitedActivationsCategoryId);

        Assert.Equal(
            new[]
            {
                "RELIC.KUNAI",
                "RELIC.KUSARIGAMA",
                "RELIC.LETTER_OPENER",
                "RELIC.ORNAMENTAL_FAN",
                "RELIC.SHURIKEN",
            },
            charge.RelicIds.OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void IsRelicInAnySelectedCategory_UsesSelectedCategories()
    {
        Assert.False(RelicTaxonomy.IsRelicInAnySelectedCategory(
            "RELIC.PEN_NIB",
            Enumerable.Empty<string>()));

        Assert.True(RelicTaxonomy.IsRelicInAnySelectedCategory(
            "RELIC.PEN_NIB",
            new[] { RelicTaxonomy.ChargeAcrossCombatsCyclingCategoryId }));

        Assert.True(RelicTaxonomy.IsRelicInAnySelectedCategory(
            "RELIC.GIRYA",
            new[] { RelicTaxonomy.ChargeAcrossCombatsNonCyclingCategoryId }));

        Assert.True(RelicTaxonomy.IsRelicInAnySelectedCategory(
            "RELIC.METRONOME",
            new[] { RelicTaxonomy.ChargeAcrossTurnsCategoryId }));

        Assert.True(RelicTaxonomy.IsRelicInAnySelectedCategory(
            "RELIC.LETTER_OPENER",
            new[] { RelicTaxonomy.ChargeResetsEachTurnUnlimitedActivationsCategoryId }));

        Assert.True(RelicTaxonomy.IsRelicInAnySelectedCategory(
            "RELIC.BRILLIANT_SCARF",
            new[] { RelicTaxonomy.ChargeResetsEachTurnLimitedActivationsCategoryId }));

        Assert.False(RelicTaxonomy.IsRelicInAnySelectedCategory(
            "RELIC.PEN_NIB",
            new[] { RelicTaxonomy.ChargeResetsEachTurnUnlimitedActivationsCategoryId }));
    }

    [Fact]
    public void ChargeSelection_ParentControlsLeavesAndReflectsPartialState()
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(
            RelicTaxonomyCategorySelectionState.Unselected,
            RelicTaxonomy.GetSelectionState(RelicTaxonomy.ChargeCategoryId, selected));

        RelicTaxonomy.SetCategorySelection(
            selected,
            RelicTaxonomy.ChargeCategoryId,
            selected: true);

        Assert.Equal(
            new[]
            {
                RelicTaxonomy.ChargeAcrossCombatsCyclingCategoryId,
                RelicTaxonomy.ChargeAcrossCombatsNonCyclingCategoryId,
                RelicTaxonomy.ChargeAcrossTurnsCategoryId,
                RelicTaxonomy.ChargeResetsEachTurnLimitedActivationsCategoryId,
                RelicTaxonomy.ChargeResetsEachTurnUnlimitedActivationsCategoryId,
            },
            selected.OrderBy(id => id, StringComparer.Ordinal).ToArray());
        Assert.Equal(
            RelicTaxonomyCategorySelectionState.Selected,
            RelicTaxonomy.GetSelectionState(RelicTaxonomy.ChargeCategoryId, selected));

        RelicTaxonomy.SetCategorySelection(
            selected,
            RelicTaxonomy.ChargeResetsEachTurnCategoryId,
            selected: false);

        Assert.Equal(
            RelicTaxonomyCategorySelectionState.Partial,
            RelicTaxonomy.GetSelectionState(RelicTaxonomy.ChargeCategoryId, selected));

        RelicTaxonomy.SetCategorySelection(
            selected,
            RelicTaxonomy.ChargeCategoryId,
            selected: true);

        Assert.Equal(5, selected.Count);
        Assert.Equal(
            RelicTaxonomyCategorySelectionState.Selected,
            RelicTaxonomy.GetSelectionState(RelicTaxonomy.ChargeCategoryId, selected));

        RelicTaxonomy.SetCategorySelection(
            selected,
            RelicTaxonomy.ChargeCategoryId,
            selected: false);

        Assert.Empty(selected);
        Assert.Equal(
            RelicTaxonomyCategorySelectionState.Unselected,
            RelicTaxonomy.GetSelectionState(RelicTaxonomy.ChargeCategoryId, selected));
    }

    [Fact]
    public void AcrossCombatsSelection_ReflectsMixedCyclingChildren()
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            RelicTaxonomy.ChargeAcrossCombatsCyclingCategoryId,
        };

        Assert.Equal(
            RelicTaxonomyCategorySelectionState.Partial,
            RelicTaxonomy.GetSelectionState(
                RelicTaxonomy.ChargeAcrossCombatsCategoryId,
                selected));

        RelicTaxonomy.SetCategorySelection(
            selected,
            RelicTaxonomy.ChargeAcrossCombatsCategoryId,
            selected: true);

        Assert.Equal(2, selected.Count);
        Assert.Equal(
            RelicTaxonomyCategorySelectionState.Selected,
            RelicTaxonomy.GetSelectionState(
                RelicTaxonomy.ChargeAcrossCombatsCategoryId,
                selected));
    }

    [Fact]
    public void ResetsEachTurnSelection_ReflectsMixedActivationChildren()
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            RelicTaxonomy.ChargeResetsEachTurnLimitedActivationsCategoryId,
        };

        Assert.Equal(
            RelicTaxonomyCategorySelectionState.Partial,
            RelicTaxonomy.GetSelectionState(
                RelicTaxonomy.ChargeResetsEachTurnCategoryId,
                selected));

        RelicTaxonomy.SetCategorySelection(
            selected,
            RelicTaxonomy.ChargeResetsEachTurnCategoryId,
            selected: true);

        Assert.Equal(2, selected.Count);
        Assert.Equal(
            RelicTaxonomyCategorySelectionState.Selected,
            RelicTaxonomy.GetSelectionState(
                RelicTaxonomy.ChargeResetsEachTurnCategoryId,
                selected));
    }

    [Fact]
    public void GetVisualAction_MapsModeAndMatchToVisualAction()
    {
        Assert.Equal(
            CompendiumRelicEntryVisualAction.Normal,
            RelicCompendiumFilterContext.GetVisualAction(CompendiumRelicFilterMode.Off, true, true, false));
        Assert.Equal(
            CompendiumRelicEntryVisualAction.Normal,
            RelicCompendiumFilterContext.GetVisualAction(CompendiumRelicFilterMode.Off, false, false, false));
        Assert.Equal(
            CompendiumRelicEntryVisualAction.Normal,
            RelicCompendiumFilterContext.GetVisualAction(CompendiumRelicFilterMode.Compare, true, true, true));
        Assert.Equal(
            CompendiumRelicEntryVisualAction.Dim,
            RelicCompendiumFilterContext.GetVisualAction(CompendiumRelicFilterMode.Compare, true, true, false));
        Assert.Equal(
            CompendiumRelicEntryVisualAction.Normal,
            RelicCompendiumFilterContext.GetVisualAction(CompendiumRelicFilterMode.Compare, false, true, false));
        Assert.Equal(
            CompendiumRelicEntryVisualAction.Hidden,
            RelicCompendiumFilterContext.GetVisualAction(CompendiumRelicFilterMode.Compare, false, false, false));
        Assert.Equal(
            CompendiumRelicEntryVisualAction.Normal,
            RelicCompendiumFilterContext.GetVisualAction(CompendiumRelicFilterMode.Filter, true, true, true));
        Assert.Equal(
            CompendiumRelicEntryVisualAction.Hidden,
            RelicCompendiumFilterContext.GetVisualAction(CompendiumRelicFilterMode.Filter, true, true, false));
        Assert.Equal(
            CompendiumRelicEntryVisualAction.Normal,
            RelicCompendiumFilterContext.GetVisualAction(CompendiumRelicFilterMode.Filter, false, true, false));
        Assert.Equal(
            CompendiumRelicEntryVisualAction.Hidden,
            RelicCompendiumFilterContext.GetVisualAction(CompendiumRelicFilterMode.Filter, false, false, false));
    }
}
