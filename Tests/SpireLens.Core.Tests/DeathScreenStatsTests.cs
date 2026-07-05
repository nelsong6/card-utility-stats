using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

public class DeathScreenStatsTests
{
    [Fact]
    public void LastEndedRelicAggregate_ReturnsFinishedRunStatsAsClone()
    {
        var run = new RunData
        {
            Outcome = "loss",
            FloorReached = 37,
        };
        run.RelicAggregates["RELIC.PLANISPHERE"] = new RelicAggregate
        {
            Activations = 4,
            TotalHealingRestored = 20,
            TotalHealingLost = 5,
        };

        try
        {
            RunTracker.SetLastEndedRunForTest(run);

            var aggregate = RunTracker.GetLastEndedRelicAggregate("RELIC.PLANISPHERE");

            Assert.NotNull(aggregate);
            Assert.Equal(4, aggregate!.Activations);
            Assert.Equal(20, aggregate.TotalHealingRestored);
            Assert.Equal(5, aggregate.TotalHealingLost);
            Assert.Equal(37, RunTracker.GetLastEndedFloorForRateStats());

            aggregate.Activations = 0;

            var aggregateAgain = RunTracker.GetLastEndedRelicAggregate("RELIC.PLANISPHERE");
            Assert.NotNull(aggregateAgain);
            Assert.Equal(4, aggregateAgain!.Activations);
        }
        finally
        {
            RunTracker.SetLastEndedRunForTest(null);
        }
    }

    [Fact]
    public void LastEndedPooledCardAggregate_ReadsFinishedRunCardStats()
    {
        var run = new RunData { Outcome = "loss" };
        run.Aggregates["CARD.ENTHRALLED#1"] = new CardAggregate
        {
            TimesDrawn = 3,
            TimesDiscarded = 2,
        };
        run.Aggregates["CARD.ENTHRALLED#2"] = new CardAggregate
        {
            TimesDrawn = 4,
            TimesExhausted = 1,
        };

        try
        {
            RunTracker.SetLastEndedRunForTest(run);

            var aggregate = RunTracker.GetLastEndedPooledCardAggregateByDefinition("CARD.ENTHRALLED");

            Assert.NotNull(aggregate);
            Assert.Equal(7, aggregate!.TimesDrawn);
            Assert.Equal(2, aggregate.TimesDiscarded);
            Assert.Equal(1, aggregate.TimesExhausted);
        }
        finally
        {
            RunTracker.SetLastEndedRunForTest(null);
        }
    }
}
