using System;
using System.Collections.Generic;
using System.Linq;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class RelicCompendiumFilterTests
{
    [Fact]
    public void Taxonomy_OmitsRemovedEnergyCategory()
    {
        Assert.DoesNotContain(RelicTaxonomy.Categories, category => category.Id == "energy");
        Assert.DoesNotContain(RelicTaxonomy.Categories, category => category.DisplayName == "Energy relics");
    }

    [Fact]
    public void ChargeTaxonomy_NestsChargeLeavesUnderChargeGroup()
    {
        var charge = RelicTaxonomy.RootCategories.Single();

        Assert.Equal(RelicTaxonomy.ChargeCategoryId, charge.Id);
        Assert.Equal("Charge", charge.DisplayName);
        Assert.Empty(charge.RelicIds);
        Assert.Equal(
            new[]
            {
                RelicTaxonomy.ChargeAcrossTurnsCategoryId,
                RelicTaxonomy.ChargeResetsEachTurnCategoryId,
            },
            charge.Children.Select(category => category.Id).ToArray());
        Assert.Equal(
            new[] { "Across turns", "Resets each turn" },
            charge.Children.Select(category => category.DisplayName).ToArray());
        Assert.Equal(
            charge.Children.ToArray(),
            RelicTaxonomy.LeafCategories.ToArray());
    }

    [Fact]
    public void ChargeAcrossTurnsTaxonomy_IncludesPersistentIncrementingRelics()
    {
        var charge = RelicTaxonomy.LeafCategories.Single(
            c => c.Id == RelicTaxonomy.ChargeAcrossTurnsCategoryId);

        Assert.Contains("RELIC.PEN_NIB", charge.RelicIds);
        Assert.Contains("RELIC.NUNCHAKU", charge.RelicIds);
        Assert.Contains("RELIC.IRON_CLUB", charge.RelicIds);
        Assert.Contains("RELIC.HAPPY_FLOWER", charge.RelicIds);
        Assert.Contains("RELIC.WINGED_BOOTS", charge.RelicIds);
        Assert.DoesNotContain("RELIC.LETTER_OPENER", charge.RelicIds);
        Assert.DoesNotContain("RELIC.VELVET_CHOKER", charge.RelicIds);
    }

    [Fact]
    public void ChargeResetsEachTurnTaxonomy_IncludesTurnLocalIncrementingRelics()
    {
        var charge = RelicTaxonomy.LeafCategories.Single(
            c => c.Id == RelicTaxonomy.ChargeResetsEachTurnCategoryId);

        Assert.Contains("RELIC.LETTER_OPENER", charge.RelicIds);
        Assert.Contains("RELIC.KUNAI", charge.RelicIds);
        Assert.Contains("RELIC.SHURIKEN", charge.RelicIds);
        Assert.Contains("RELIC.BRILLIANT_SCARF", charge.RelicIds);
        Assert.Contains("RELIC.VELVET_CHOKER", charge.RelicIds);
        Assert.DoesNotContain("RELIC.PEN_NIB", charge.RelicIds);
        Assert.DoesNotContain("RELIC.NUNCHAKU", charge.RelicIds);
    }

    [Fact]
    public void IsRelicInAnySelectedCategory_UsesSelectedCategories()
    {
        Assert.False(RelicTaxonomy.IsRelicInAnySelectedCategory(
            "RELIC.PEN_NIB",
            Enumerable.Empty<string>()));

        Assert.True(RelicTaxonomy.IsRelicInAnySelectedCategory(
            "RELIC.PEN_NIB",
            new[] { RelicTaxonomy.ChargeAcrossTurnsCategoryId }));

        Assert.True(RelicTaxonomy.IsRelicInAnySelectedCategory(
            "RELIC.LETTER_OPENER",
            new[] { RelicTaxonomy.ChargeResetsEachTurnCategoryId }));

        Assert.False(RelicTaxonomy.IsRelicInAnySelectedCategory(
            "RELIC.PEN_NIB",
            new[] { RelicTaxonomy.ChargeResetsEachTurnCategoryId }));
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
                RelicTaxonomy.ChargeAcrossTurnsCategoryId,
                RelicTaxonomy.ChargeResetsEachTurnCategoryId,
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

        Assert.Equal(2, selected.Count);
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
