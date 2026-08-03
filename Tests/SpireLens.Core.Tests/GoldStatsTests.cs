using MegaCrit.Sts2.Core.Rooms;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class GoldStatsTests
{
    [Fact]
    public void Accumulation_UsesObservedGainAndSpentContexts()
    {
        var stats = new RunGoldStats();

        RunTracker.RecordGoldGainForTest(stats, 50, RoomType.Monster);
        RunTracker.RecordGoldGainForTest(stats, 30, RoomType.Event);
        RunTracker.RecordGoldGainForTest(stats, 20, RoomType.Shop);
        RunTracker.RecordGoldSpentForTest(stats, 40, RoomType.Shop);
        RunTracker.RecordGoldSpentForTest(stats, 10, RoomType.Event);
        RunTracker.RecordGoldSpentForTest(stats, 5, RoomType.Monster);

        Assert.Equal(100, stats.GoldAcquired);
        Assert.Equal(50, stats.GoldGainedInCombats);
        Assert.Equal(30, stats.GoldGainedInEvents);
        Assert.Equal(55, stats.GoldSpent);
        Assert.Equal(40, stats.GoldSpentInShops);
        Assert.Equal(10, stats.GoldSpentInEvents);
    }

    [Fact]
    public void RoomVisits_AreZeroInclusiveAndIdempotentPerFloor()
    {
        var stats = new RunGoldStats();

        Assert.True(RunTracker.RecordGoldRoomVisitForTest(stats, RoomType.Shop, 4));
        Assert.False(RunTracker.RecordGoldRoomVisitForTest(stats, RoomType.Shop, 4));
        Assert.True(RunTracker.RecordGoldRoomVisitForTest(stats, RoomType.Shop, 8));
        Assert.True(RunTracker.RecordGoldRoomVisitForTest(stats, RoomType.Event, 8));
        Assert.False(RunTracker.RecordGoldRoomVisitForTest(stats, RoomType.Event, 8));

        Assert.Equal(2, stats.ShopsVisited);
        Assert.Equal(1, stats.EventsVisited);
    }

    [Fact]
    public void CombatPromotion_MergesGoldFlowAndDenominator()
    {
        var run = new RunData
        {
            GoldStats = new RunGoldStats
            {
                GoldAcquired = 100,
                GoldGainedInCombats = 40,
                Combats = 2,
            },
        };
        var pending = new PendingCombat
        {
            GoldStats = new RunGoldStats
            {
                GoldAcquired = 25,
                GoldGainedInCombats = 25,
                GoldSpent = 5,
                Combats = 1,
            },
        };

        RunTracker.PromotePendingCombatIntoRun(pending, run);

        Assert.Equal(125, run.GoldStats.GoldAcquired);
        Assert.Equal(65, run.GoldStats.GoldGainedInCombats);
        Assert.Equal(5, run.GoldStats.GoldSpent);
        Assert.Equal(3, run.GoldStats.Combats);
    }

    [Fact]
    public void Tooltip_ShowsRequestedGoldRowsAndRates()
    {
        var body = GoldStatsTooltip.BuildBodyBBCode(
            new RunGoldStats
            {
                GoldAcquired = 480,
                GoldSpent = 300,
                GoldSpentInShops = 220,
                GoldSpentInEvents = 50,
                GoldGainedInCombats = 180,
                GoldGainedInEvents = 80,
                ShopsVisited = 4,
                EventsVisited = 5,
                Combats = 10,
            },
            floors: 12);

        var rows = body.Split('\n');
        Assert.Contains(rows, row => row.Contains("Gold gained:")
            && row.Contains("Acquired   [b]480[/b]"));
        Assert.Contains(rows, row => row.Contains("Gold:")
            && row.Contains("Spent   [b]300[/b]"));
        Assert.Contains(rows, row => row.Contains("Merchant:")
            && row.Contains("Gold:")
            && row.Contains("Spent   [b]220[/b]"));
        Assert.Contains(rows, row => row.Contains("Average:")
            && row.Contains("Merchant:")
            && row.Contains("Gold:")
            && row.Contains("Spent   [b]55[/b]"));
        Assert.Contains(rows, row => row.Contains("Unknown room:")
            && row.Contains("Gold:")
            && row.Contains("Spent   [b]50[/b]"));
        Assert.Contains(rows, row => row.Contains("Average:")
            && row.Contains("Floor:")
            && row.Contains("Gold gained:")
            && row.Contains("Gained   [b]40[/b]"));
        Assert.Contains(rows, row => row.Contains("Average:")
            && row.Contains("Combat:")
            && row.Contains("Gold gained:")
            && row.Contains("Gained   [b]18[/b]"));
        Assert.Contains(rows, row => row.Contains("Average:")
            && row.Contains("Unknown room:")
            && row.Contains("Gold gained:")
            && row.Contains("Gained   [b]16[/b]"));
        Assert.DoesNotContain("Gold spent", body);
        Assert.DoesNotContain("per shop", body);
        Assert.DoesNotContain("per floor", body);
        Assert.DoesNotContain("per combat", body);
        Assert.DoesNotContain("per event", body);
    }
}
