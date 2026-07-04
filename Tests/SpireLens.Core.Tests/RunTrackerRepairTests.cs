using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

public class RunTrackerRepairTests
{
    [Fact]
    public void RepairOffensiveDamageAggregatesFromEvents_RebuildsLethalOverkillDamage()
    {
        var run = new RunData();
        run.Aggregates["CARD.SOVEREIGN_BLADE#1"] = new CardAggregate
        {
            Plays = 1,
            TotalIntended = 1,
            TotalEffective = -22,
            TotalOverkill = 23,
            Kills = 1,
        };
        run.Events.Add(new CardEvent
        {
            Type = "damage_received",
            CardId = "CARD.SOVEREIGN_BLADE#1",
            Receiver = "MONSTER.GAS_BOMB",
            Blocked = 0,
            Unblocked = 1,
            Overkill = 23,
            Killed = true,
        });

        bool changed = RunTracker.RepairOffensiveDamageAggregatesFromEvents(run);

        var aggregate = run.Aggregates["CARD.SOVEREIGN_BLADE#1"];
        Assert.True(changed);
        Assert.Equal(24, aggregate.TotalIntended);
        Assert.Equal(1, aggregate.TotalEffective);
        Assert.Equal(23, aggregate.TotalOverkill);
        Assert.Equal(1, aggregate.Kills);
        Assert.Equal(1, aggregate.Plays);
    }

    [Fact]
    public void RepairOffensiveDamageAggregatesFromEvents_NullReceiverIsTreatedAsEnemyDamage()
    {
        // Regression (#254): the pre-fix writer could stamp a null Receiver
        // for an enemy hit (the receiver had no Monster ref). The repair used
        // to drop null Receivers, permanently wiping that card's damage +
        // kills on every adopt/Continue rebuild. Now a null Receiver is
        // recovered as enemy damage.
        var run = new RunData();
        run.Aggregates["CARD.DEATH_MARCH#1"] = new CardAggregate
        {
            Plays = 1,
            TotalIntended = 20,
            TotalEffective = 20,
            Kills = 1,
        };
        run.Events.Add(new CardEvent
        {
            Type = "damage_received",
            CardId = "CARD.DEATH_MARCH#1",
            Receiver = null,
            Blocked = 0,
            Unblocked = 20,
            Overkill = 0,
            Killed = true,
        });

        bool changed = RunTracker.RepairOffensiveDamageAggregatesFromEvents(run);

        var aggregate = run.Aggregates["CARD.DEATH_MARCH#1"];
        Assert.True(changed);
        Assert.Equal(20, aggregate.TotalIntended);
        Assert.Equal(20, aggregate.TotalEffective);
        Assert.Equal(1, aggregate.Kills);
    }

    [Fact]
    public void RepairOffensiveDamageAggregatesFromEvents_ExcludesPlayerSelfDamage()
    {
        // Self-damage carries a non-null, non-"MONSTER." receiver and must
        // stay out of offensive totals — the null-Receiver recovery above
        // must not sweep genuine self-damage into the enemy branch.
        var run = new RunData();
        run.Aggregates["CARD.HEMOKINESIS#1"] = new CardAggregate { Plays = 1 };
        run.Events.Add(new CardEvent
        {
            Type = "damage_received",
            CardId = "CARD.HEMOKINESIS#1",
            Receiver = "PLAYER",
            Blocked = 0,
            Unblocked = 3,
            Overkill = 0,
            Killed = false,
        });

        RunTracker.RepairOffensiveDamageAggregatesFromEvents(run);

        var aggregate = run.Aggregates["CARD.HEMOKINESIS#1"];
        Assert.Equal(0, aggregate.TotalIntended);
        Assert.Equal(0, aggregate.TotalEffective);
        Assert.Equal(0, aggregate.Kills);
    }
}
