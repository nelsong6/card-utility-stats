using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

public class CardUpgradeLineageTests
{
    [Fact]
    public void IsExactPermanentDeckCard_RequiresTheSameObjectReference()
    {
        var deckCard = Uninitialized<DrainPower>();
        var combatCopy = Uninitialized<DrainPower>();
        CardModel[] permanentDeck = [deckCard];

        Assert.True(RunTracker.IsExactPermanentDeckCardForTest(
            deckCard,
            permanentDeck));
        Assert.False(RunTracker.IsExactPermanentDeckCardForTest(
            combatCopy,
            permanentDeck));
        Assert.False(RunTracker.IsExactPermanentDeckCardForTest(
            deckCard,
            null));
    }

    [Fact]
    public void FilterPermanentUpgradeEvents_DropsLegacyTemporaryCloneEvents()
    {
        CardEvent[] events =
        [
            UpgradeEvent("temporary clone at base level", floor: 3, level: 0),
            UpgradeEvent("first permanent upgrade", floor: 5, level: 1),
            UpgradeEvent("temporary clone repeats deck level", floor: 7, level: 1),
            UpgradeEvent("temporary clone reports a lower level", floor: 8, level: 0),
            UpgradeEvent("second permanent upgrade", floor: 10, level: 2),
        ];

        var filtered = RunTracker.FilterPermanentUpgradeEvents(
            events,
            initialUpgradeLevel: 0);

        Assert.Collection(
            filtered,
            cardEvent => Assert.Equal("first permanent upgrade", cardEvent.T),
            cardEvent => Assert.Equal("second permanent upgrade", cardEvent.T));
    }

    [Fact]
    public void FilterPermanentUpgradeEvents_UsesCameUpgradedLevelAsBaseline()
    {
        CardEvent[] events =
        [
            UpgradeEvent("temporary clone", floor: 4, level: 1),
            UpgradeEvent("permanent upgrade", floor: 9, level: 2),
        ];

        var filtered = RunTracker.FilterPermanentUpgradeEvents(
            events,
            initialUpgradeLevel: 1);

        var cardEvent = Assert.Single(filtered);
        Assert.Equal("permanent upgrade", cardEvent.T);
    }

    private static CardEvent UpgradeEvent(string marker, int floor, int level)
        => new()
        {
            T = marker,
            Type = "card_upgraded",
            CardId = "CARD.DRAIN_POWER#1",
            Floor = floor,
            UpgradeLevel = level,
        };

    private static T Uninitialized<T>() where T : class
        => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
}
