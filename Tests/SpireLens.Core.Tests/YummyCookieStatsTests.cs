using System;
using System.Linq;
using System.Reflection;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class YummyCookieStatsTests
{
    private static readonly MethodInfo BuildYummyCookieBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildYummyCookieBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildYummyCookieBodyBBCode not found.");

    [Fact]
    public void RunTracker_YummyCookieTestHelper_RecordsOnlyNamedUpgradedCards()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordYummyCookieUpgradesForTest(
            agg,
            new[] { "Strike+", "", "Pommel Strike+", "  " });

        Assert.Equal(2, agg.CardsUpgraded);
        Assert.Equal(new[] { "Strike+", "Pommel Strike+" }, agg.UpgradedCards);
    }

    [Fact]
    public void RelicTooltip_YummyCookie_ListsUpgradedCards()
    {
        var body = BuildBody(new RelicAggregate
        {
            CardsUpgraded = 2,
            UpgradedCards = { "Strike+", "Pommel Strike+" },
        });

        Assert.Contains("Cards upgraded", body);
        Assert.Contains("Upgraded card", body);
        Assert.Contains("Strike+", body);
        Assert.Contains("Pommel Strike+", body);
        Assert.Contains("[b]2[/b]", body);
    }

    [Fact]
    public void RepairSelection_UsesOnlyFinalSynchronousPickupCluster()
    {
        var events = new[]
        {
            Upgrade("CARD.STRIKE#1", "2026-08-03T02:12:40.0000000Z"),
            Upgrade("CARD.SUCKER_PUNCH#1", "2026-08-03T02:12:45.0845120Z"),
            Upgrade("CARD.OUTBREAK#2", "2026-08-03T02:12:45.0878633Z"),
            Upgrade("CARD.OUTBREAK#1", "2026-08-03T02:12:45.0907542Z"),
            Upgrade("CARD.DAGGER_THROW#1", "2026-08-03T02:12:45.0932716Z"),
        };

        var selected = RunTracker.SelectYummyCookieUpgradeEventsForRepair(
            events,
            pickupFloor: 18);

        Assert.Equal(
            new[]
            {
                "CARD.SUCKER_PUNCH#1",
                "CARD.OUTBREAK#2",
                "CARD.OUTBREAK#1",
                "CARD.DAGGER_THROW#1",
            },
            selected.Select(cardEvent => cardEvent.CardId));
    }

    private static CardEvent Upgrade(string cardId, string timestamp)
        => new()
        {
            T = timestamp,
            Type = "card_upgraded",
            CardId = cardId,
            Floor = 18,
            UpgradeLevel = 1,
        };

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildYummyCookieBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildYummyCookieBodyBBCode returned null."));
}
