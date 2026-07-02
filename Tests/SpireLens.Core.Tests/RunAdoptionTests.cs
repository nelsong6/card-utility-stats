using System.Collections.Generic;
using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

/// <summary>
/// Pins the ghost-aggregate pruning that runs when a run is adopted (main-menu
/// Continue re-fire, Continue after game restart, or hot-reload resume).
///
/// Ghosts are the empty arrival stamps minted when CardEnterDeckPatch fires
/// before RunStarted on a continue-load: fresh CardModel refs get brand-new
/// instance numbers continuing the old counters (e.g. DEATH_MARCH#3/#4 for a
/// two-copy deck) before the adoption rebind can map them back to #1/#2.
/// Pruning must remove exactly those and nothing with recorded history.
/// </summary>
public class RunAdoptionTests
{
    private static CardAggregate GhostStamp() => new()
    {
        FloorAdded = 10,
        InitialUpgradeLevel = 1,
    };

    private static RunData RunWithGhosts()
    {
        var run = new RunData
        {
            DefCounters = new Dictionary<string, int> { ["CARD.DEATH_MARCH"] = 2 },
        };
        run.Aggregates["CARD.DEATH_MARCH#1"] = new CardAggregate { Plays = 6, TotalEffective = 60 };
        run.Aggregates["CARD.DEATH_MARCH#2"] = new CardAggregate { Plays = 3, TotalEffective = 72 };
        run.Aggregates["CARD.DEATH_MARCH#3"] = GhostStamp();
        run.Aggregates["CARD.DEATH_MARCH#4"] = GhostStamp();
        return run;
    }

    private static HashSet<string> Live(params string[] ids) => new(ids);

    [Fact]
    public void Prune_RemovesEmptyUnmappedStampsBeyondSavedCounter()
    {
        var run = RunWithGhosts();

        int pruned = RunTracker.PruneGhostAggregates(
            run, run.DefCounters, Live("CARD.DEATH_MARCH#1", "CARD.DEATH_MARCH#2"));

        Assert.Equal(2, pruned);
        Assert.True(run.Aggregates.ContainsKey("CARD.DEATH_MARCH#1"));
        Assert.True(run.Aggregates.ContainsKey("CARD.DEATH_MARCH#2"));
        Assert.False(run.Aggregates.ContainsKey("CARD.DEATH_MARCH#3"));
        Assert.False(run.Aggregates.ContainsKey("CARD.DEATH_MARCH#4"));
    }

    [Fact]
    public void Prune_KeepsAggregatesWithinSavedCounter()
    {
        // An empty, unmapped stamp at or below the saved counter predates the
        // last save — e.g. a card that entered the deck and was legitimately
        // saved before ever being drawn. Never prune those.
        var run = new RunData
        {
            DefCounters = new Dictionary<string, int> { ["CARD.STRIKE"] = 3 },
        };
        run.Aggregates["CARD.STRIKE#3"] = GhostStamp();

        int pruned = RunTracker.PruneGhostAggregates(run, run.DefCounters, Live());

        Assert.Equal(0, pruned);
        Assert.True(run.Aggregates.ContainsKey("CARD.STRIKE#3"));
    }

    [Fact]
    public void Prune_KeepsLiveMappedNumbers()
    {
        // A number beyond the saved counter that IS bound to a live card is a
        // legitimately-added card whose save just hadn't happened yet.
        var run = new RunData
        {
            DefCounters = new Dictionary<string, int> { ["CARD.STRIKE"] = 2 },
        };
        run.Aggregates["CARD.STRIKE#3"] = GhostStamp();

        int pruned = RunTracker.PruneGhostAggregates(run, run.DefCounters, Live("CARD.STRIKE#3"));

        Assert.Equal(0, pruned);
        Assert.True(run.Aggregates.ContainsKey("CARD.STRIKE#3"));
    }

    [Fact]
    public void Prune_KeepsAggregatesWithRecordedActivity()
    {
        var run = new RunData
        {
            DefCounters = new Dictionary<string, int> { ["CARD.STRIKE"] = 2 },
        };
        run.Aggregates["CARD.STRIKE#3"] = new CardAggregate { TimesDrawn = 1 };

        int pruned = RunTracker.PruneGhostAggregates(run, run.DefCounters, Live());

        Assert.Equal(0, pruned);
        Assert.True(run.Aggregates.ContainsKey("CARD.STRIKE#3"));
    }

    [Fact]
    public void Prune_KeepsAggregatesReferencedByEvents()
    {
        var run = new RunData
        {
            DefCounters = new Dictionary<string, int> { ["CARD.STRIKE"] = 2 },
        };
        run.Aggregates["CARD.STRIKE#3"] = GhostStamp();
        run.Events.Add(new CardEvent { Type = "card_upgraded", CardId = "CARD.STRIKE#3" });

        int pruned = RunTracker.PruneGhostAggregates(run, run.DefCounters, Live());

        Assert.Equal(0, pruned);
        Assert.True(run.Aggregates.ContainsKey("CARD.STRIKE#3"));
    }

    [Fact]
    public void Prune_KeepsRemovedAggregates()
    {
        var run = new RunData
        {
            DefCounters = new Dictionary<string, int> { ["CARD.STRIKE"] = 2 },
        };
        run.Aggregates["CARD.STRIKE#3"] = new CardAggregate { Removed = true, RemovedAtFloor = 5 };

        int pruned = RunTracker.PruneGhostAggregates(run, run.DefCounters, Live());

        Assert.Equal(0, pruned);
        Assert.True(run.Aggregates.ContainsKey("CARD.STRIKE#3"));
    }

    [Fact]
    public void Prune_MissingDefCounters_StillGuardedByLiveSetAndActivity()
    {
        // Files without counters for a def (or legacy nulls) fall back to a
        // zero counter — pruning then rests entirely on the live-map, event,
        // and emptiness guards.
        var run = new RunData();
        run.Aggregates["CARD.FETCH#1"] = new CardAggregate { Plays = 4 };
        run.Aggregates["CARD.FETCH#2"] = GhostStamp();

        int pruned = RunTracker.PruneGhostAggregates(run, null, Live("CARD.FETCH#1"));

        Assert.Equal(1, pruned);
        Assert.True(run.Aggregates.ContainsKey("CARD.FETCH#1"));
        Assert.False(run.Aggregates.ContainsKey("CARD.FETCH#2"));
    }
}
