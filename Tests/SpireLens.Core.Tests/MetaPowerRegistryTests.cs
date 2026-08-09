using System.Linq;
using MegaCrit.Sts2.Core.Models.Powers;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class MetaPowerRegistryTests
{
    /// <summary>
    /// The registry's power ids must equal what the game emits at runtime,
    /// because TryGetByPower matches on power.Id. Shortened forms silently
    /// resolved to nothing for every entry, which cost five months of
    /// application and active-turn data before anyone noticed.
    /// </summary>
    [Fact]
    public void EveryEntryUsesTheCanonicalRuntimePowerId()
    {
        Assert.NotEmpty(MetaPowerRegistry.All);

        foreach (var definition in MetaPowerRegistry.All)
        {
            Assert.StartsWith("POWER.", definition.PowerId);
            Assert.EndsWith("_POWER", definition.PowerId);
            Assert.StartsWith("CARD.", definition.CardId);
        }
    }

    [Fact]
    public void LookupByLivePowerIdResolvesTheSameDefinition()
    {
        var expected = MetaPowerRegistry.All.Single(candidate =>
            candidate.CardId == "CARD.RUPTURE");

        Assert.Equal(ModelIds.TryGet<RupturePower>(), expected.PowerId);
        Assert.True(
            MetaPowerRegistry.TryGetByPowerId(expected.PowerId, out var byId));
        Assert.Equal(expected, byId);
    }

    [Fact]
    public void BufferIsRegisteredAsAMetaPower()
    {
        Assert.True(
            MetaPowerRegistry.TryGetByCardId("CARD.BUFFER", out var definition));
        Assert.Equal("POWER.BUFFER_POWER", definition!.PowerId);
        Assert.Equal("Buffer", definition.DisplayName);
    }

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
