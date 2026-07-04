using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

/// <summary>
/// Pins the pending-combat → run promotion merge now shared by OnCombatEnded
/// and OnRunEnded. The OnRunEnded call is what keeps a lost run's fatal combat
/// (which never gets a CombatEnded) from being discarded with the buffer.
/// </summary>
public class PendingCombatPromotionTests
{
    [Fact]
    public void Promote_MergesCardAggregatesAndEventsIntoEmptyRun()
    {
        var pending = new PendingCombat();
        pending.CombatAggregates["CARD.DEATH_MARCH#1"] = new CardAggregate
        {
            Plays = 2,
            TotalIntended = 47,
            TotalEffective = 39,
            TotalOverkill = 8,
            Kills = 1,
        };
        pending.CombatEvents.Add(new CardEvent
        {
            Type = "damage_received",
            CardId = "CARD.DEATH_MARCH#1",
            Receiver = "MONSTER.FLYCONID",
            Unblocked = 39,
            Overkill = 8,
            Killed = true,
        });

        var run = new RunData();
        RunTracker.PromotePendingCombatIntoRun(pending, run);

        var agg = run.Aggregates["CARD.DEATH_MARCH#1"];
        Assert.Equal(2, agg.Plays);
        Assert.Equal(47, agg.TotalIntended);
        Assert.Equal(39, agg.TotalEffective);
        Assert.Equal(8, agg.TotalOverkill);
        Assert.Equal(1, agg.Kills);
        Assert.Single(run.Events);
        Assert.Equal("MONSTER.FLYCONID", run.Events[0].Receiver);
    }

    [Fact]
    public void Promote_AddsOntoExistingCommittedAggregates()
    {
        var pending = new PendingCombat();
        pending.CombatAggregates["CARD.STRIKE#1"] = new CardAggregate
        {
            Plays = 1,
            TotalIntended = 9,
            TotalEffective = 9,
        };

        var run = new RunData();
        run.Aggregates["CARD.STRIKE#1"] = new CardAggregate
        {
            Plays = 3,
            TotalIntended = 27,
            TotalEffective = 20,
            TotalBlocked = 7,
        };

        RunTracker.PromotePendingCombatIntoRun(pending, run);

        var agg = run.Aggregates["CARD.STRIKE#1"];
        Assert.Equal(4, agg.Plays);
        Assert.Equal(36, agg.TotalIntended);
        Assert.Equal(29, agg.TotalEffective);
        Assert.Equal(7, agg.TotalBlocked);
    }

    [Fact]
    public void Promote_MergesRelicAggregates_CountsActivationsOnce()
    {
        // Regression: the old inline merge added Activations twice
        // (copy-paste duplicate line), doubling every relic's run totals.
        var pending = new PendingCombat();
        pending.RelicAggregates["RELIC.MEAL_TICKET"] = new RelicAggregate
        {
            Activations = 3,
            EnergyGenerated = 2,
        };

        var run = new RunData();
        RunTracker.PromotePendingCombatIntoRun(pending, run);

        var relic = run.RelicAggregates["RELIC.MEAL_TICKET"];
        Assert.Equal(3, relic.Activations);
        Assert.Equal(2, relic.EnergyGenerated);
    }

    [Fact]
    public void Promote_MergesEnemyAggregatesAndMetaStats()
    {
        var pending = new PendingCombat();
        pending.EnemyAggregates["MONSTER.SHRINKER_BEETLE"] = new EnemyAggregate
        {
            EnemyId = "MONSTER.SHRINKER_BEETLE",
            DisplayName = "Shrinker Beetle",
            DamageInstances = 2,
            DamageAttempted = 16,
            DamageDealt = 11,
            DamageBlocked = 5,
        };
        pending.MetaStats.TotalOstyDamageAbsorbed = 4;

        var run = new RunData();
        RunTracker.PromotePendingCombatIntoRun(pending, run);

        var enemy = run.EnemyAggregates["MONSTER.SHRINKER_BEETLE"];
        Assert.Equal(2, enemy.DamageInstances);
        Assert.Equal(16, enemy.DamageAttempted);
        Assert.Equal(11, enemy.DamageDealt);
        Assert.Equal(5, enemy.DamageBlocked);
        Assert.Equal(4, run.MetaStats.TotalOstyDamageAbsorbed);
    }
}
