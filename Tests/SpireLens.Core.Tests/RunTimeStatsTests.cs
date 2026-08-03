using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

public class RunTimeStatsTests
{
    [Fact]
    public void MergeRunTimeStatsInto_AddsElapsedTimeAndDenominatorsOnly()
    {
        var target = new RunTimeStats
        {
            CombatSeconds = 30,
            RewardScreenSeconds = 10,
            EventSeconds = 20,
            MapSeconds = 5,
            Combats = 1,
            CombatTurns = 3,
            LastObservedRunTime = 100,
            ActiveCategory = "Map",
        };
        var source = new RunTimeStats
        {
            CombatSeconds = 45,
            RewardScreenSeconds = 7,
            EventSeconds = 8,
            MapSeconds = 9,
            Combats = 1,
            CombatTurns = 4,
            LastObservedRunTime = 999,
            ActiveCategory = "Combat",
        };

        RunTracker.MergeRunTimeStatsInto(target, source);

        Assert.Equal(75, target.CombatSeconds);
        Assert.Equal(17, target.RewardScreenSeconds);
        Assert.Equal(28, target.EventSeconds);
        Assert.Equal(14, target.MapSeconds);
        Assert.Equal(2, target.Combats);
        Assert.Equal(7, target.CombatTurns);
        Assert.Equal(100, target.LastObservedRunTime);
        Assert.Equal("Map", target.ActiveCategory);
    }

    [Fact]
    public void PromotePendingCombatIntoRun_CommitsCombatTimerStats()
    {
        var run = new RunData
        {
            TimeStats = new RunTimeStats
            {
                CombatSeconds = 60,
                Combats = 2,
                CombatTurns = 8,
            },
        };
        var pending = new PendingCombat
        {
            TimeStats = new RunTimeStats
            {
                CombatSeconds = 37,
                Combats = 1,
                CombatTurns = 5,
            },
        };

        RunTracker.PromotePendingCombatIntoRun(pending, run);

        Assert.Equal(97, run.TimeStats.CombatSeconds);
        Assert.Equal(3, run.TimeStats.Combats);
        Assert.Equal(13, run.TimeStats.CombatTurns);
    }
}
