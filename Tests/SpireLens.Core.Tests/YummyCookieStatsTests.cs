using System;
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

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildYummyCookieBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildYummyCookieBodyBBCode returned null."));
}
