using System.Linq;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class NotInDeckViewTests
{
    [Fact]
    public void SelectCardsForView_NormalModeUsesOnlyCurrentDeck()
    {
        var selected = DeckViewNotInDeckPatch.SelectCardsForView(
            deckCards: new[] { 1, 2, 3 },
            notInDeckCards: new[] { 4, 5 },
            showCardsNotInDeck: false);

        Assert.Equal(new[] { 1, 2, 3 }, selected);
    }

    [Fact]
    public void SelectCardsForView_NotInDeckModeReplacesCurrentDeck()
    {
        var selected = DeckViewNotInDeckPatch.SelectCardsForView(
            deckCards: new[] { 1, 2, 3 },
            notInDeckCards: new[] { 4, 5, 4 },
            showCardsNotInDeck: true);

        Assert.Equal(new[] { 4, 5 }, selected);
        Assert.DoesNotContain(1, selected);
        Assert.DoesNotContain(2, selected);
        Assert.DoesNotContain(3, selected);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void MetaCards_AppearWhenEncounteredOrShowAllIsEnabled(
        bool appearedThisRun,
        bool showAll,
        bool expected)
    {
        Assert.Equal(
            expected,
            RunTracker.ShouldIncludeMetaCardForTest(
                appearedThisRun,
                showAll));
    }

    [Fact]
    public void MetaCardRegistry_ContainsCurrentPooledCardSurfaces()
    {
        var ids = RunTracker.GetSupportedMetaCardDefinitionIdsForTest();

        Assert.Equal(
            new[]
            {
                "CARD.SHIV",
                "CARD.SOUL",
                "CARD.SOVEREIGN_BLADE",
                "CARD.AGGRESSION",
                "CARD.DANSE_MACABRE",
                "CARD.DARK_EMBRACE",
                "CARD.ENTROPY",
                "CARD.FEEL_NO_PAIN",
                "CARD.JUGGLING",
                "CARD.RUPTURE",
                "CARD.STAMPEDE",
                "CARD.UNMOVABLE",
                "CARD.VICIOUS",
            },
            ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void StatusMetaCards_DefaultViewUsesOnlyEncounteredDefinitions()
    {
        var ids = RunTracker.SelectStatusMetaCardDefinitionIdsForTest(
            allStatusDefinitionIds:
            [
                "CARD.WOUND",
                "CARD.BURN",
                "CARD.SLIMED",
                "CARD.BURN",
            ],
            encounteredStatusDefinitionIds:
            [
                "CARD.SLIMED",
                "CARD.UNKNOWN_STATUS",
            ],
            includeAllMetaCards: false);

        Assert.Equal(["CARD.SLIMED"], ids);
    }

    [Fact]
    public void StatusMetaCards_ShowAllUsesEveryKnownStatusDefinition()
    {
        var ids = RunTracker.SelectStatusMetaCardDefinitionIdsForTest(
            allStatusDefinitionIds:
            [
                "CARD.WOUND",
                "CARD.BURN",
                "CARD.SLIMED",
                "CARD.BURN",
            ],
            encounteredStatusDefinitionIds: [],
            includeAllMetaCards: true);

        Assert.Equal(
            ["CARD.BURN", "CARD.SLIMED", "CARD.WOUND"],
            ids);
    }
}
