using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using SpireLens.Core.Patches;

namespace SpireLens.Core;

/// <summary>
/// One SpireLens-tracked number the deck view can be ordered by. Adding a new
/// entry to <see cref="DeckViewSpireLensSort.Metrics"/> is the whole cost of
/// adding a new option to the menu — the ordering, the menu rows, and the
/// handoff back to the game's own sorters are all metric-agnostic.
/// </summary>
internal sealed class DeckSortMetric
{
    internal DeckSortMetric(string id, string label, Func<CardAggregate, long> select)
    {
        Id = id;
        Label = label;
        Select = select;
    }

    internal string Id { get; }

    internal string Label { get; }

    /// <summary>Value to order by. Untracked cards score 0 and trail.</summary>
    internal Func<CardAggregate, long> Select { get; }
}

/// <summary>
/// Orders the in-run deck view by a SpireLens attribution stat instead of one
/// of the game's four built-in sorts.
///
/// How it reaches the grid: <c>NCardGrid.SetCards</c> only runs its own
/// comparator when <c>sortingPriority[0]</c> is something other than
/// <c>Ascending</c> / <c>Descending</c> — with <c>Ascending</c> at the head it
/// renders the supplied list verbatim. So a SpireLens ordering is just
/// "reorder <c>_cards</c>, then pin <c>Ascending</c>", applied from the same
/// <c>DisplayCards</c> prefix that already decides which cards to show
/// (see <see cref="DeckViewNotInDeckPatch"/>). No new Harmony target, and no
/// patch on the shared card grid.
///
/// Handing ordering back: the four stock sorters mutate <c>_sortingPriority</c>
/// and their own direction flag. <c>OnObtainedSort</c>'s ascending result is
/// byte-identical to the head we pin, so the priority list cannot tell us a
/// stock sorter ran — the direction flags can, and they flip on release for
/// mouse and controller alike. We snapshot all four each time we order, and
/// stand down as soon as one of them moves.
///
/// State is deliberately session-only (no loader-backed pref): persisting it
/// would need a new Loader bridge method, which a Core hot reload cannot add,
/// and a view ordering is not worth a game restart.
/// </summary>
internal static class DeckViewSpireLensSort
{
    internal static readonly IReadOnlyList<DeckSortMetric> Metrics = new[]
    {
        // "Total damage" here is exactly the row the card tooltip prints under
        // that name: effective damage, i.e. HP this physical card actually
        // removed across the run. Block and overkill waste are excluded, which
        // is what players mean by "this card has done X damage".
        new DeckSortMetric("total_damage", "Total damage", agg => agg.TotalEffective),
    };

    internal static DeckSortMetric? ActiveMetric { get; private set; }

    /// <summary>Highest first. The default for every metric we offer.</summary>
    internal static bool Descending { get; private set; } = true;

    private static NDeckViewScreen? _snapshotScreen;
    private static bool[]? _snapshotDirections;

    internal static bool IsActive(DeckSortMetric metric)
        => ReferenceEquals(metric, ActiveMetric);

    /// <summary>
    /// Choose a metric. Choosing the one already active flips the direction,
    /// mirroring how the game's own sorters reverse on a second click.
    /// </summary>
    internal static void Select(DeckSortMetric metric, string source)
    {
        if (IsActive(metric))
        {
            Descending = !Descending;
        }
        else
        {
            ActiveMetric = metric;
            Descending = true;
        }

        ForgetSnapshot();
        CoreMain.Logger.Info(
            $"Deck sort set to {metric.Id} {(Descending ? "descending" : "ascending")} ({source})");
        DeckViewSortMenu.RefreshButtonText();
        DeckViewSortMenu.RefreshDeckView();
    }

    /// <summary>Stand down and let the game's own sorting govern again.</summary>
    internal static void Clear(string source)
    {
        if (ActiveMetric == null) return;

        ActiveMetric = null;
        ForgetSnapshot();
        CoreMain.Logger.Info($"Deck sort cleared ({source})");
        DeckViewSortMenu.RefreshButtonText();
    }

    /// <summary>
    /// Reorder the screen's pending card list. Called at the end of the
    /// <c>DisplayCards</c> prefix, after the not-in-deck view has decided
    /// which collection is on screen, so it orders whatever is actually shown.
    /// </summary>
    internal static void Apply(NDeckViewScreen screen)
    {
        try
        {
            var metric = ActiveMetric;
            if (metric == null) return;

            if (StockSorterMoved(screen))
            {
                // The player reached for one of the game's sorters. Clearing
                // before we reorder means this very render already honours
                // their choice — no stale frame, no second DisplayCards pass.
                Clear("stock sorter used");
                return;
            }

            // The run-history viewer reuses this screen type over a rebuilt
            // historical deck, so its numbers must come from the archived run
            // rather than from whatever run is live now.
            var historical = RunHistoryDeckViewer.IsHistoricalDeckViewer(screen);

            var cards = screen._cards;
            if (cards != null && cards.Count > 1)
            {
                // OrderBy is stable, so cards with equal values keep the order
                // the game gave us. Untracked cards score 0 and land at the
                // far end rather than disappearing.
                screen._cards = (Descending
                        ? cards.OrderByDescending(card => ValueFor(card, metric, historical))
                        : cards.OrderBy(card => ValueFor(card, metric, historical)))
                    .ToList();
            }

            PinUnsortedHead(screen);
            TakeSnapshot(screen);

            CoreMain.LogDebug(
                $"DeckViewSpireLensSort: ordered {screen._cards?.Count ?? 0} cards by " +
                $"{metric.Id} {(Descending ? "desc" : "asc")}");
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"DeckViewSpireLensSort.Apply failed: {e.Message}");
        }
    }

    internal static void Reset()
    {
        ActiveMetric = null;
        Descending = true;
        ForgetSnapshot();
    }

    private static long ValueFor(CardModel card, DeckSortMetric metric, bool historical)
    {
        try
        {
            CardAggregate? aggregate;
            if (historical)
            {
                RunHistoryStatsContext.TryGetHistoricalDeckAggregate(card, out aggregate);
            }
            else
            {
                aggregate = RunTracker.GetEffectiveAggregate(card);
            }

            return aggregate == null ? 0L : metric.Select(aggregate);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Warn($"DeckViewSpireLensSort: value lookup failed: {e.Message}");
            return 0L;
        }
    }

    private static void PinUnsortedHead(NDeckViewScreen screen)
    {
        var priority = screen._sortingPriority;
        if (priority == null) return;

        priority.Remove(SortingOrders.Ascending);
        priority.Remove(SortingOrders.Descending);
        priority.Insert(0, SortingOrders.Ascending);
    }

    private static bool StockSorterMoved(NDeckViewScreen screen)
    {
        // A freshly opened screen has nothing to compare against: its sorters
        // start at their scene defaults and the player has not touched them.
        if (!ReferenceEquals(screen, _snapshotScreen) || _snapshotDirections == null)
            return false;

        var current = ReadDirections(screen);
        for (var i = 0; i < current.Length; i++)
        {
            if (current[i] != _snapshotDirections[i]) return true;
        }

        return false;
    }

    private static void TakeSnapshot(NDeckViewScreen screen)
    {
        _snapshotScreen = screen;
        _snapshotDirections = ReadDirections(screen);
    }

    private static void ForgetSnapshot()
    {
        _snapshotScreen = null;
        _snapshotDirections = null;
    }

    private static bool[] ReadDirections(NDeckViewScreen screen) =>
    [
        ReadDirection(screen._obtainedSorter),
        ReadDirection(screen._typeSorter),
        ReadDirection(screen._costSorter),
        ReadDirection(screen._alphabetSorter),
    ];

    // Read only: the IsDescending SETTER re-renders the sorter's arrow icon.
    private static bool ReadDirection(NCardViewSortButton? sorter)
        => sorter != null && GodotObject.IsInstanceValid(sorter) && sorter.IsDescending;
}
