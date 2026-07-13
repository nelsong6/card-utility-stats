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
    public void ChargeTaxonomy_NestsActivationLeavesUnderResetsEachTurn()
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
                RelicTaxonomy.ChargeAcrossTurnsCategoryId,
                RelicTaxonomy.ChargeResetsEachTurnLimitedActivationsCategoryId,
                RelicTaxonomy.ChargeResetsEachTurnUnlimitedActivationsCategoryId,
            },
            RelicTaxonomy.LeafCategories.Select(category => category.Id).ToArray());
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

        Assert.Equal(3, selected.Count);
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
