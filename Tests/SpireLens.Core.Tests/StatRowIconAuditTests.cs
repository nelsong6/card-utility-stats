using MegaCrit.Sts2.Core.Map;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class StatRowIconAuditTests
{
    [Fact]
    public void MapSummary_UsesConceptIconsWithoutRepeatingTheirProse()
    {
        var body = MapLegendStatsTooltip.BuildBodyBBCode(
            MapPointType.RestSite,
            new MapLegendCategoryStats
            {
                Visits = 2,
                FloorsBetweenVisitsTotal = 6,
                FloorsBetweenVisitsSamples = 1,
                HpHealed = 19,
                CardsUpgraded = 2,
                PotionsOffered = 1,
            },
            potionsOffered: 1,
            potionsGained: 1,
            maxHpGained: 3,
            maxHpLost: 0);

        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("campfire"), body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("upgraded"), body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("offered"), body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("potion_gained"), body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("max_hp_gained"), body);
        Assert.DoesNotContain("Cards upgraded   [b]", body);
        Assert.DoesNotContain("Potions offered   [b]", body);
        Assert.DoesNotContain("Potions gained   [b]", body);
        Assert.DoesNotContain("Max HP gained   [b]", body);
        Assert.DoesNotContain("between   [b]", body);
    }
}
