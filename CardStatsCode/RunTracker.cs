using System;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace CardStats.CardStatsCode;

/// <summary>
/// Holds the current run's stats in memory. All mutations funnel through here
/// to keep aggregation + event-log logic in one place.
///
/// Thread safety: the combat history callbacks all fire on the game thread,
/// but we defensively lock anyway in case a future patch adds async sources.
///
/// MVP scope (M1, issue #5):
/// - Tracks card plays (count)
/// - Tracks damage attributed to cards (BlockedDamage / UnblockedDamage / OverkillDamage / WasTargetKilled)
/// - Does NOT track block/energy/draw closure yet (M2/M3)
///
/// Run boundary detection is also pending (issue forthcoming) — for M1 we use
/// a single in-memory run that starts when the mod loads and writes every event
/// under that run_id. "Per actual run" boundaries come later.
/// </summary>
public static class RunTracker
{
    private static readonly object _lock = new();
    private static RunData _current = NewRun();

    /// <summary>Exposed for diagnostics / future UI reads. Do not mutate from outside.</summary>
    public static RunData Current { get { lock (_lock) return _current; } }

    private static RunData NewRun()
    {
        string now = DateTime.UtcNow.ToString("o");
        return new RunData
        {
            RunId = Guid.NewGuid().ToString("N"),
            StartedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Called from the CombatHistory.Add postfix with a fully-constructed entry.
    /// Returns fast for entry types we don't care about yet (so we can enable
    /// more types incrementally as later milestones need them).
    /// </summary>
    public static void Observe(object entry)
    {
        try
        {
            switch (entry)
            {
                case CardPlayFinishedEntry cpf:
                    OnCardPlayFinished(cpf.CardPlay);
                    break;
                case DamageReceivedEntry dre when dre.CardSource != null:
                    OnDamageFromCard(dre);
                    break;
                // Other entry types ignored for M1; handled in later milestones.
            }
        }
        catch (Exception e)
        {
            // Never let tracker exceptions escape into the game loop.
            MainFile.Logger.Error($"RunTracker.Observe failed: {e}");
        }
    }

    private static void OnCardPlayFinished(CardPlay cardPlay)
    {
        string cardId = cardPlay.Card.Id.Entry;

        lock (_lock)
        {
            var agg = GetOrCreate(cardId);
            agg.Plays++;

            _current.Events.Add(new CardEvent
            {
                T = DateTime.UtcNow.ToString("o"),
                Type = "card_played",
                CardId = cardId,
                Target = cardPlay.Target?.Monster?.Id.Entry,
            });

            Touch();
        }

        RunStorage.SaveAsync(_current);
    }

    private static void OnDamageFromCard(DamageReceivedEntry entry)
    {
        // CardSource is non-null per the caller's filter.
        string cardId = entry.CardSource!.Id.Entry;
        var result = entry.Result;

        // Intended: total damage attempted, before block absorption.
        // Effective: damage that actually removed HP (unblocked minus overkill waste).
        int intended = result.BlockedDamage + result.UnblockedDamage;
        int effective = result.UnblockedDamage - result.OverkillDamage;

        lock (_lock)
        {
            var agg = GetOrCreate(cardId);
            agg.TotalIntended += intended;
            agg.TotalBlocked += result.BlockedDamage;
            agg.TotalOverkill += result.OverkillDamage;
            agg.TotalEffective += effective;
            if (result.WasTargetKilled) agg.Kills++;

            _current.Events.Add(new CardEvent
            {
                T = DateTime.UtcNow.ToString("o"),
                Type = "damage_received",
                CardId = cardId,
                Receiver = entry.Receiver.IsPlayer
                    ? entry.Receiver.Player?.Character?.Id.Entry
                    : entry.Receiver.Monster?.Id.Entry,
                Blocked = result.BlockedDamage,
                Unblocked = result.UnblockedDamage,
                Overkill = result.OverkillDamage,
                Killed = result.WasTargetKilled,
            });

            Touch();
        }

        RunStorage.SaveAsync(_current);
    }

    private static CardAggregate GetOrCreate(string cardId)
    {
        if (!_current.Aggregates.TryGetValue(cardId, out var agg))
        {
            agg = new CardAggregate();
            _current.Aggregates[cardId] = agg;
        }
        return agg;
    }

    private static void Touch() => _current.UpdatedAt = DateTime.UtcNow.ToString("o");
}
