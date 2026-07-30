using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class MetaPowerRegistryTests
{
    [Fact]
    public void DivideMetaPowerRate_UsesTheSelectedDenominator()
    {
        Assert.Equal(
            2.5m,
            CardHoverShowPatch.DivideMetaPowerRate(10m, 4));
        Assert.Equal(
            0m,
            CardHoverShowPatch.DivideMetaPowerRate(10m, 0));
    }

    [Fact]
    public void Promotion_MergesMetaPowerCohortsAndObservationEraNumerators()
    {
        var run = new RunData();
        run.MetaStats.PowerAggregates["POWER.DANSE_MACABRE"] =
            new PowerAggregate
            {
                PowerId = "POWER.DANSE_MACABRE",
                DisplayName = "Danse Macabre",
                PowerCardsPlayed = 2,
                GeneratedPowerCardsPlayed = 1,
                SuccessfulApplications = 2,
                MetaDeckTurns = 4,
                MetaActiveTurns = 3,
                MetaActiveApplicationTurns = 5,
                RateTimesTriggered = 4,
                RateBlockGained = 12m,
            };

        var pending = new PendingCombat();
        pending.MetaStats.PowerAggregates["POWER.DANSE_MACABRE"] =
            new PowerAggregate
            {
                PowerId = "POWER.DANSE_MACABRE",
                DisplayName = "Danse Macabre",
                PowerCardsPlayed = 3,
                GeneratedPowerCardsPlayed = 1,
                SuccessfulApplications = 2,
                MetaDeckTurns = 8,
                MetaActiveTurns = 5,
                MetaActiveApplicationTurns = 8,
                RateTimesTriggered = 3,
                RateBlockGained = 12m,
            };

        RunTracker.PromotePendingCombatIntoRun(pending, run);

        var aggregate =
            run.MetaStats.PowerAggregates["POWER.DANSE_MACABRE"];
        Assert.Equal(5, aggregate.PowerCardsPlayed);
        Assert.Equal(2, aggregate.GeneratedPowerCardsPlayed);
        Assert.Equal(4, aggregate.SuccessfulApplications);
        Assert.Equal(12, aggregate.MetaDeckTurns);
        Assert.Equal(8, aggregate.MetaActiveTurns);
        Assert.Equal(13, aggregate.MetaActiveApplicationTurns);
        Assert.Equal(7, aggregate.RateTimesTriggered);
        Assert.Equal(24m, aggregate.RateBlockGained);
    }
}
