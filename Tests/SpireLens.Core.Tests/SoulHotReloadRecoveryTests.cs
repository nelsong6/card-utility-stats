using System.Collections.Generic;
using Xunit;

namespace SpireLens.Core.Tests;

public class SoulHotReloadRecoveryTests
{
    [Fact]
    public void RecoveredUsage_AccumulatesAllObservedSoulInstances()
    {
        var committed = new CardAggregate
        {
            Plays = 1,
            TimesDrawn = 2,
            TimesExhausted = 1,
            TimesCardsDrawn = 2,
        };
        var recovered = new CardAggregate();

        RunTracker.ApplyRecoveredSoulUsageForTest(
            recovered,
            new SoulCombatUsageSnapshot
            {
                Plays = 3,
                TimesDrawn = 5,
                TimesExhausted = 3,
                TimesCardsDrawn = 6,
            });

        var pooled = CardAggregatePooler.PoolByDefinition(
            new Dictionary<string, CardAggregate>
            {
                ["CARD.SOUL#1"] = committed,
                ["CARD.SOUL#2"] = recovered,
            },
            "CARD.SOUL");

        Assert.NotNull(pooled);
        Assert.Equal(4, pooled!.Plays);
        Assert.Equal(7, pooled.TimesDrawn);
        Assert.Equal(4, pooled.TimesExhausted);
        Assert.Equal(8, pooled.TimesCardsDrawn);
    }

    [Fact]
    public void RecoveredUsage_IncludesDiscardAndPaidResources()
    {
        var aggregate = new CardAggregate();

        RunTracker.ApplyRecoveredSoulUsageForTest(
            aggregate,
            new SoulCombatUsageSnapshot
            {
                TimesDiscarded = 2,
                TotalEnergySpent = 3,
                TotalStarsSpent = 4,
            });

        Assert.Equal(2, aggregate.TimesDiscarded);
        Assert.Equal(3, aggregate.TotalEnergySpent);
        Assert.Equal(4, aggregate.TotalStarsSpent);
    }
}
