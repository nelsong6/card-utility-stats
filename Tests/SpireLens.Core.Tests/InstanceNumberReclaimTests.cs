using System.Collections.Generic;
using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

/// <summary>
/// Pins the fallback that stops a run minting a fresh generation of instance
/// numbers for cards it already tracks. Before this, any deck card the saved
/// ID snapshot did not cover minted a new number and orphaned the stats already
/// recorded against that physical card; across a run's Continues and Core
/// reloads that accumulated whole generations per definition.
/// </summary>
public class InstanceNumberReclaimTests
{
    private const string Strike = "CARD.STRIKE_DEFECT";

    [Fact]
    public void Reclaim_TakesTheLowestUnclaimedNumberForTheDefinition()
    {
        // The observed shape: #1-#4 played then orphaned, #5-#8 pure ghosts,
        // #9-#11 the live generation, #12 removed.
        var aggs = Aggregates(
            (Strike, 1, false), (Strike, 2, false), (Strike, 3, false),
            (Strike, 4, false), (Strike, 5, false), (Strike, 6, false),
            (Strike, 7, false), (Strike, 8, false), (Strike, 9, false),
            (Strike, 10, false), (Strike, 11, false), (Strike, 12, true));

        var selected = RunTracker.SelectOrphanedInstanceNumber(
            aggs, Strike, Bound(9, 10, 11), queuedNumbers: null);

        Assert.Equal(1, selected);
    }

    [Fact]
    public void Reclaim_HandsOutNumbersInDeckRankOrderAcrossSuccessiveCards()
    {
        var aggs = Aggregates((Strike, 1, false), (Strike, 2, false), (Strike, 3, false));
        var bound = new HashSet<int>();

        var first = RunTracker.SelectOrphanedInstanceNumber(aggs, Strike, bound, null);
        bound.Add(first!.Value);
        var second = RunTracker.SelectOrphanedInstanceNumber(aggs, Strike, bound, null);
        bound.Add(second!.Value);
        var third = RunTracker.SelectOrphanedInstanceNumber(aggs, Strike, bound, null);

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(3, third);
    }

    [Fact]
    public void Reclaim_NeverTakesARemovedCardsIdentity()
    {
        var aggs = Aggregates((Strike, 1, true), (Strike, 2, false));

        Assert.Equal(
            2,
            RunTracker.SelectOrphanedInstanceNumber(aggs, Strike, null, null));
    }

    [Fact]
    public void Reclaim_SkipsNumbersAlreadyBoundToALiveCard()
    {
        var aggs = Aggregates((Strike, 1, false), (Strike, 2, false));

        Assert.Equal(
            2,
            RunTracker.SelectOrphanedInstanceNumber(aggs, Strike, Bound(1), null));
    }

    [Fact]
    public void Reclaim_LeavesTheSavedSnapshotQueueUntouched()
    {
        // The snapshot queue is the authoritative restore order; reclaim is only
        // the fallback for what the queue does not cover.
        var aggs = Aggregates((Strike, 1, false), (Strike, 2, false));

        Assert.Equal(
            2,
            RunTracker.SelectOrphanedInstanceNumber(aggs, Strike, null, Bound(1)));
    }

    [Fact]
    public void Reclaim_IgnoresOtherDefinitions()
    {
        var aggs = Aggregates(("CARD.DEFEND_DEFECT", 1, false), (Strike, 7, false));

        Assert.Equal(
            7,
            RunTracker.SelectOrphanedInstanceNumber(aggs, Strike, null, null));
    }

    [Fact]
    public void Reclaim_MintsFreshWhenEveryTrackedNumberIsAccountedFor()
    {
        var aggs = Aggregates((Strike, 1, false), (Strike, 2, true));

        Assert.Null(
            RunTracker.SelectOrphanedInstanceNumber(aggs, Strike, Bound(1), null));
    }

    [Fact]
    public void Reclaim_IgnoresPooledKeysThatCarryNoInstanceNumber()
    {
        var aggs = new Dictionary<string, CardAggregate> { [Strike] = new() };

        Assert.Null(
            RunTracker.SelectOrphanedInstanceNumber(aggs, Strike, null, null));
    }

    [Fact]
    public void Reclaim_HandlesAnEmptyRun()
    {
        Assert.Null(
            RunTracker.SelectOrphanedInstanceNumber(
                new Dictionary<string, CardAggregate>(), Strike, null, null));
    }

    private static Dictionary<string, CardAggregate> Aggregates(
        params (string DefId, int Number, bool Removed)[] entries)
    {
        var result = new Dictionary<string, CardAggregate>();
        foreach (var (defId, number, removed) in entries)
            result[$"{defId}#{number}"] = new CardAggregate { Removed = removed };
        return result;
    }

    private static HashSet<int> Bound(params int[] numbers) => new(numbers);
}
