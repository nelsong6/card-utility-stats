using System.Linq;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class RelicCompendiumFilterTests
{
    [Fact]
    public void EnergyTaxonomy_IncludesTrackedEnergyRelics()
    {
        var energy = RelicTaxonomy.Categories.Single(c => c.Id == RelicTaxonomy.EnergyCategoryId);

        Assert.Contains("RELIC.HAPPY_FLOWER", energy.RelicIds);
        Assert.Contains("RELIC.NUNCHAKU", energy.RelicIds);
        Assert.Contains("RELIC.BOOMING_CONCH", energy.RelicIds);
        Assert.Contains("RELIC.PRISMATIC_GEM", energy.RelicIds);
        Assert.Contains("RELIC.BRILLIANT_SCARF", energy.RelicIds);
    }

    [Fact]
    public void ChargeTaxonomy_IncludesIncrementingRelics()
    {
        var charge = RelicTaxonomy.Categories.Single(c => c.Id == RelicTaxonomy.ChargeCategoryId);

        Assert.Contains("RELIC.PEN_NIB", charge.RelicIds);
        Assert.Contains("RELIC.LETTER_OPENER", charge.RelicIds);
        Assert.Contains("RELIC.NUNCHAKU", charge.RelicIds);
        Assert.Contains("RELIC.IRON_CLUB", charge.RelicIds);
        Assert.Contains("RELIC.HAPPY_FLOWER", charge.RelicIds);
        Assert.Contains("RELIC.WINGED_BOOTS", charge.RelicIds);
    }

    [Fact]
    public void IsRelicInAnySelectedCategory_UsesSelectedCategories()
    {
        Assert.True(RelicTaxonomy.IsRelicInAnySelectedCategory(
            "RELIC.LANTERN",
            new[] { RelicTaxonomy.EnergyCategoryId }));

        Assert.False(RelicTaxonomy.IsRelicInAnySelectedCategory(
            "RELIC.ANCHOR",
            new[] { RelicTaxonomy.EnergyCategoryId }));

        Assert.False(RelicTaxonomy.IsRelicInAnySelectedCategory(
            "RELIC.LANTERN",
            Enumerable.Empty<string>()));

        Assert.True(RelicTaxonomy.IsRelicInAnySelectedCategory(
            "RELIC.PEN_NIB",
            new[] { RelicTaxonomy.ChargeCategoryId }));

        Assert.False(RelicTaxonomy.IsRelicInAnySelectedCategory(
            "RELIC.PEN_NIB",
            new[] { RelicTaxonomy.EnergyCategoryId }));
    }

    [Fact]
    public void GetVisualAction_MapsModeAndMatchToVisualAction()
    {
        Assert.Equal(
            CompendiumRelicEntryVisualAction.Normal,
            RelicCompendiumFilterContext.GetVisualAction(CompendiumRelicFilterMode.Off, true, false));
        Assert.Equal(
            CompendiumRelicEntryVisualAction.Normal,
            RelicCompendiumFilterContext.GetVisualAction(CompendiumRelicFilterMode.Compare, true, true));
        Assert.Equal(
            CompendiumRelicEntryVisualAction.Dim,
            RelicCompendiumFilterContext.GetVisualAction(CompendiumRelicFilterMode.Compare, true, false));
        Assert.Equal(
            CompendiumRelicEntryVisualAction.Normal,
            RelicCompendiumFilterContext.GetVisualAction(CompendiumRelicFilterMode.Filter, true, true));
        Assert.Equal(
            CompendiumRelicEntryVisualAction.Hidden,
            RelicCompendiumFilterContext.GetVisualAction(CompendiumRelicFilterMode.Filter, true, false));
        Assert.Equal(
            CompendiumRelicEntryVisualAction.Normal,
            RelicCompendiumFilterContext.GetVisualAction(CompendiumRelicFilterMode.Filter, false, false));
    }
}
