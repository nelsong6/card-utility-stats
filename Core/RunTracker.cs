using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace SpireLens.Core;

/// <summary>
/// Tracks the current run's stats in memory and commits them to disk at
/// combat boundaries.
///
/// Key rule (per Nelson): nothing is written to the permanent run file until
/// a combat *finishes*. During combat, card plays and damage events accumulate
/// in <see cref="_pendingCombat"/> only. On <c>CombatEnded</c> we promote the
/// pending buffer into the run's committed aggregates + event log and save.
/// On run start the previous run (if any) is finalized first.
///
/// Thread safety: game events all fire on the main thread. We still lock
/// defensively since file I/O is on a background task.
///
/// Current scope:
///   - per-instance card identity and run persistence
///   - combat-boundary aggregation into committed run data
///   - attack, block, energy, draw, exhaust, and effect attribution
///   - case-specific downstream attribution such as poison tick damage
/// </summary>
public static class RunTracker
{
    private const string EnthralledDefinitionId = "CARD.ENTHRALLED";
    private const string ShivDefinitionId = "CARD.SHIV";
    private const string SovereignBladeLegacyDefinitionToken = "SOVEREIGN_BLADE";
    private const string SovereignBladeLegacyDefinitionId = "CARD.SOVEREIGN_BLADE";
    private const string ShivGeneratedEventType = "shiv_generated";
    private const string SovereignBladeForgedEventType = "sovereign_blade_forged";

    private static readonly object _lock = new();

    // Co-op local-player scoping (#260). NetId of the player whose run we track.
    // Null => single-player OR local player unresolved: every guard ALLOWS, so
    // SP is byte-identical and a mis-detection can never disable tracking.
    // Re-derived from LocalContext.NetId on run start/adopt; never persisted.
    private static ulong? _trackedNetId;
    private static RunData? _currentRun;
    private static RunData? _lastEndedRun;
    private static PendingCombat? _pendingCombat;
    private static CardPlay? _currentPlayerCardPlay;
    private static CardPlay? _recentCompletedPlayerCardPlay;
    private static int _recentCompletedPlayerCardPlayHistoryCount;
    private static CardModel? _pendingDrawSourceCard;
    private static readonly List<PendingDrawAttempt> _pendingDrawAttempts = new();
    private static CardModel? _pendingEffectSourceCard;
    private static int _pendingEffectSourceHistoryCount;
    private static readonly List<PendingPowerChangeAttempt> _pendingPowerChangeAttempts = new();
    private static readonly System.Threading.AsyncLocal<EnemyStatusSourceFrame?> _enemyStatusSourceFrame = new();
    private static int _pendingPlayerBlockClearAmount;
    private static bool _pendingPlayerBlockClearArmed;
    private static bool _pendingAkabekoVigorAttribution;
    private static readonly List<PendingRelicHealing> _pendingRelicHeals = new();
    private static readonly List<Player> _pendingPendulumDrawAttributions = new();
    private static readonly List<Creature> _pendingParryingShieldDamageAttributions = new();
    private static readonly List<Creature> _pendingFestivePopperDamageAttributions = new();
    private static readonly List<Creature> _pendingMercuryHourglassDamageAttributions = new();
    private static readonly List<Creature> _pendingMrStrugglesDamageAttributions = new();
    private static readonly Dictionary<PowerModel, int> _bronzeScalesThornsContributions = new(ReferenceEqualityComparer.Instance);
    private static readonly List<PendingBronzeScalesDamageAttribution> _pendingBronzeScalesDamageAttributions = new();
    private static readonly List<Creature> _pendingHornCleatBlockAttributions = new();
    private static readonly Dictionary<string, int> _lastEnergyResetRoundByRelicAndPlayer = new();
    private static int _pendingWhiteBeastPotionRewards;
    private static int _pendingToolboxOfferScreens;
    private static readonly List<Player> _pendingHeftyTabletChoicePlayers = new();
    private static readonly HashSet<PotionReward> _whiteBeastPotionRewards = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<CardReward, PendingPaelSacrificeReward> _paelSacrificeRewards = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Player, PendingRegalPillowRestHeal> _pendingRegalPillowRestHeals = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Player, PendingPrecariousShearsPickup> _pendingPrecariousShearsPickups = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<Player> _pendingLeafyPoulticePickups = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Player, PendingSandCastlePickup> _pendingSandCastlePickups = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Player, PendingWhetstonePickup> _pendingWhetstonePickups = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Player, PendingWarPaintPickup> _pendingWarPaintPickups = new(ReferenceEqualityComparer.Instance);
    private static bool _shivAvailableThisRun;
    private static CardModel? _shivDeckViewCard;
    private const decimal PoisonOwnershipEpsilon = 0.0001m;
    private static bool _sovereignBladeAvailableThisRun;
    private static CardModel? _sovereignBladeDeckViewCard;
    private static string? _sovereignBladeDefinitionIdThisRun;

    // Per-instance identity. Every physical card in the player's deck gets
    // a stable number the first time we observe it — NOT just when it's
    // played. Two reasons Nelson insisted on this:
    //   1. Hover-before-play: unplayed cards still need a stable identifier.
    //      If "Strike #1" only gets assigned on first play, the same physical
    //      card appears as "Strike" then jumps to "Strike #1" mid-run — and
    //      a different Strike that got played first would steal the "#1".
    //   2. Removal-safe: if Strike #2 is removed from the deck (Smith, etc.)
    //      and later a new Strike is added, the new one is Strike #3 (or
    //      whatever's next on the monotonic counter), NOT a renumbered #2.
    //      Numbers are never reused, so accumulated stats never silently
    //      migrate to a different physical card.
    //
    // Numbers are assigned by:
    //   - RunStarted: walk the starting deck in order → Strike #1, #2, #3...
    //   - Lazy on first touch (hover or play): catches cards added mid-run
    //     via rewards, shops, events. Numbers keep incrementing monotonically.
    private static readonly Dictionary<CardModel, int> _instanceNumbers = new();
    // Monotonic counter per card definition — so 3 Strikes become #1/#2/#3.
    // Never decremented. If a Strike is removed, the counter stays put; the
    // next added Strike gets the NEXT number, not a reused old one.
    private static readonly Dictionary<string, int> _defCounters = new();
    private static readonly HashSet<CardModel> _pendingMakeItSoSummons = new();
    private static readonly Dictionary<CardModel, Queue<PendingReplayExtraPlaySource>> _pendingReplayExtraPlaySources = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<CardModel, bool> _pendingReplayExtraPlaySeriesStarted = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<CardPlay, PendingReplayAttackOutcome> _pendingReplayAttackOutcomes = new(ReferenceEqualityComparer.Instance);

    // DamageResult objects already seen through a real DamageReceivedEntry
    // this combat. Lets the combat-ending capture (HookAfterDamageGivenPatch →
    // RecordCombatEndingSuppressedDamage) tell a hit whose history entry the
    // game suppressed apart from one it recorded normally. Reference-keyed —
    // the game allocates a distinct DamageResult per hit.
    private static readonly HashSet<DamageResult> _observedDamageResults = new(ReferenceEqualityComparer.Instance);

    // Saved instance numbers still waiting for the deck to (re)populate after
    // a resume/adoption. Continue-loads don't guarantee whether RunStarted
    // fires before or after CardPile.AddInternal repopulates the deck; numbers
    // the adoption deck walk couldn't bind yet are claimed here in arrival
    // (deck) order by CardEnterDeckPatch instead of minting fresh numbers
    // that would orphan the saved stats.
    private static readonly Dictionary<string, Queue<int>> _pendingRankRestores = new();

    /// <summary>
    /// Wire up game event subscriptions. Called by <see cref="CoreMain.Initialize"/>
    /// on first load and each hot-reload. Safe to call before CombatManager/RunManager
    /// singletons receive their first state — we subscribe to events, not read state eagerly.
    /// </summary>
    public static void InitializeHooks()
    {
        RunManager.Instance.RunStarted += OnRunStarted;
        CombatManager.Instance.CombatSetUp += OnCombatSetUp;
        CombatManager.Instance.CombatEnded += OnCombatEnded;
        CoreMain.Logger.Info("SpireLens hooks wired (RunStarted, CombatSetUp, CombatEnded).");
    }

    /// <summary>
    /// Unsubscribe from the game's events before the assembly unloads.
    /// Essential for hot-reload — otherwise RunManager and CombatManager
    /// hold delegate references back into this (old) assembly, preventing
    /// ALC collection and leaking the assembly on every reload.
    /// </summary>
    public static void TeardownHooks()
    {
        RunManager.Instance.RunStarted -= OnRunStarted;
        CombatManager.Instance.CombatSetUp -= OnCombatSetUp;
        CombatManager.Instance.CombatEnded -= OnCombatEnded;
        CoreMain.Logger.Info("SpireLens hooks unwired.");
    }

    /// <summary>Exposed read-only for diagnostics and (future) UI reads.</summary>
    public static RunData? Current
    {
        get { lock (_lock) return _currentRun; }
    }

    /// <summary>
    /// Serialize the current run to JSON in the canonical on-disk wire format
    /// (snake_case, WhenWritingNull, IncludeFields) UNDER the lock, so external
    /// API consumers read exactly the run-file shape and never observe a
    /// half-promoted run mid-CombatEnded. The public API (SpireLensApiRegistry)
    /// calls this reflectively instead of serializing the live ref itself,
    /// which both raced OnCombatEnded and emitted an incompatible PascalCase
    /// shape (#86/#96).
    /// </summary>
    public static string? SerializeCurrentRunJson()
    {
        lock (_lock)
        {
            if (_currentRun == null) return null;
            return System.Text.Json.JsonSerializer.Serialize(_currentRun, RunStorage.Options);
        }
    }

    /// <summary>
    /// Resolve any CardModel (combat clone or deck original) to its canonical
    /// per-deck reference. Combat clones have <c>DeckVersion</c> set to the
    /// original deck card by <c>Player.PopulateCombatState</c>
    /// (<c>cardModel.DeckVersion = item</c>). Deck-view cards ARE the original,
    /// so <c>DeckVersion</c> is null — the card itself is canonical.
    ///
    /// Using this as our dict key is what makes play-time (combat clone) and
    /// hover-time (deck original) lookups converge. Without it, ref-keyed
    /// dictionaries always miss because they see two different objects for
    /// what the player perceives as the same physical card.
    /// </summary>
    private static CardModel Canonical(CardModel card) => card.DeckVersion ?? card;

    /// <summary>
    /// Effective aggregate for a specific card instance — committed run-level
    /// stats PLUS whatever's in the current combat's pending buffer. Keyed by
    /// CardModel reference (stable within a run) through our instance-id map.
    /// Returns null if we haven't tracked this specific card yet.
    /// </summary>
    public static CardAggregate? GetEffectiveAggregate(CardModel card)
    {
        lock (_lock)
        {
            if (IsShivDeckViewCardLocked(card))
                return GetShivDeckViewAggregateLocked();
            if (IsSovereignBladeDeckViewCardLocked(card))
                return GetSovereignBladeDeckViewAggregateLocked();

            // Non-assigning: if the card isn't tracked (preview/template
            // not yet a real deck member), return null so the tooltip
            // shows the empty-aggregate layout without creating a spurious
            // instance number. Tracked cards (in deck or played as
            // ephemeral) resolve normally.
            if (!TryGetInstanceId(card, out var instanceId)) return null;

            CardAggregate? result = null;

            if (_currentRun != null && _currentRun.Aggregates.TryGetValue(instanceId, out var committed))
                result = CloneAggregate(committed);

            if (_pendingCombat != null && _pendingCombat.CombatAggregates.TryGetValue(instanceId, out var pending))
            {
                result ??= new CardAggregate();
                MergeAggregateInto(result, pending);
            }

            return result;
        }
    }

    public static RunMetaStats GetEffectiveMetaStats()
    {
        lock (_lock)
        {
            var result = new RunMetaStats();
            if (_currentRun != null)
                MergeMetaStatsInto(result, _currentRun.MetaStats);
            if (_pendingCombat != null)
                MergeMetaStatsInto(result, _pendingCombat.MetaStats);
            return result;
        }
    }

    public static int? GetEtherealCardsPlayedThisCombat()
    {
        lock (_lock)
        {
            return _pendingCombat?.EtherealCardsPlayed;
        }
    }

    /// <summary>
    /// The instance number for a card for UI display purposes — derived from
    /// the card's position in the player's deck among other cards of the same
    /// definition. Stable across a run, doesn't depend on play order or on
    /// our own tracking state. If two Strikes are in the deck, the first in
    /// deck order is "Strike 1" and the second is "Strike 2", regardless of
    /// whether either has been played yet.
    ///
    /// In-combat subtlety: during combat, cards are distributed across the
    /// draw/hand/discard/exhaust/play piles and may NOT be in player.Deck at
    /// the moment of hover (depends on the game's internal bookkeeping). We
    /// enumerate all piles so the numbering stays consistent mid-combat too.
    ///
    /// Returns 0 if the card isn't found anywhere (shouldn't happen in
    /// practice unless it's been fully removed from the run).
    /// </summary>
    public static int GetInstanceNumber(CardModel card)
    {
        if (card == null) return 0;
        lock (_lock)
        {
            // NON-assigning lookup. Only cards that have actually entered
            // the deck (via CardEnterDeckPatch) or been played (via Record
            // paths) have numbers. Hovering a preview/template card that
            // hasn't entered the deck returns 0, which the tooltip
            // renders as "Strike" with no instance number — we don't
            // want to burn monotonic counters on UI previews.
            var key = Canonical(card);
            return _instanceNumbers.TryGetValue(key, out var existing) ? existing : 0;
        }
    }

    public static bool IsShivDeckViewCard(CardModel card)
    {
        if (card == null) return false;
        lock (_lock) return IsShivDeckViewCardLocked(card);
    }

    private static bool IsShivDeckViewCardLocked(CardModel card)
    {
        return _shivDeckViewCard != null
            && ReferenceEquals(Canonical(card), _shivDeckViewCard);
    }

    private static CardAggregate? GetShivDeckViewAggregateLocked()
    {
        return GetPooledEffectiveAggregateByDefinitionLocked(ShivDefinitionId);
    }

    public static CardAggregate GetEnthralledCurseAggregate()
    {
        lock (_lock)
        {
            return GetPooledEffectiveAggregateByDefinitionLocked(EnthralledDefinitionId)
                   ?? new CardAggregate();
        }
    }

    public static bool IsSovereignBladeDeckViewCard(CardModel card)
    {
        if (card == null) return false;
        lock (_lock) return IsSovereignBladeDeckViewCardLocked(card);
    }

    private static bool IsSovereignBladeDeckViewCardLocked(CardModel card)
    {
        return _sovereignBladeDeckViewCard != null
            && ReferenceEquals(Canonical(card), _sovereignBladeDeckViewCard);
    }

    private static CardAggregate? GetSovereignBladeDeckViewAggregateLocked()
    {
        var definitionId = GetSovereignBladeDefinitionIdLocked();
        if (string.IsNullOrWhiteSpace(definitionId)) return null;

        return GetPooledEffectiveAggregateByDefinitionLocked(definitionId);
    }

    private static CardAggregate? GetPooledEffectiveAggregateByDefinitionLocked(string definitionId)
    {
        CardAggregate? pooled = null;

        if (_currentRun != null)
            pooled = CardAggregatePooler.PoolByDefinition(_currentRun.Aggregates, definitionId);

        if (_pendingCombat != null)
        {
            var pending = CardAggregatePooler.PoolByDefinition(
                _pendingCombat.CombatAggregates,
                definitionId);
            if (pending != null)
            {
                pooled ??= new CardAggregate();
                CardAggregatePooler.MergeInto(pooled, pending);
            }
        }

        return pooled;
    }

    private static bool HasShivDataLocked()
    {
        if (_currentRun?.Events.Any(e => e.Type == ShivGeneratedEventType) == true)
            return true;

        if (_pendingCombat?.CombatEvents.Any(e => e.Type == ShivGeneratedEventType) == true)
            return true;

        if (_currentRun?.Aggregates.Keys.Any(key =>
                CardAggregatePooler.IsAggregateForDefinition(key, ShivDefinitionId)) == true)
            return true;

        if (_pendingCombat?.CombatAggregates.Keys.Any(key =>
                CardAggregatePooler.IsAggregateForDefinition(key, ShivDefinitionId)) == true)
            return true;

        return false;
    }

    private static bool HasSovereignBladeDataLocked()
    {
        if (_currentRun?.Events.Any(e => e.Type == SovereignBladeForgedEventType) == true)
            return true;

        if (_pendingCombat?.CombatEvents.Any(e => e.Type == SovereignBladeForgedEventType) == true)
            return true;

        var definitionId = GetSovereignBladeDefinitionIdLocked();
        if (!string.IsNullOrWhiteSpace(definitionId))
        {
            if (_currentRun?.Aggregates.Keys.Any(key =>
                    CardAggregatePooler.IsAggregateForDefinition(key, definitionId)) == true)
                return true;

            if (_pendingCombat?.CombatAggregates.Keys.Any(key =>
                    CardAggregatePooler.IsAggregateForDefinition(key, definitionId)) == true)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Clear the synthetic deck-view card caches and availability flags.
    /// Single home for the per-run deck-view reset — used by fresh run start,
    /// run end, and run adoption (which recomputes availability right after
    /// via the Refresh*AvailabilityLocked pair).
    /// </summary>
    private static void ResetDeckViewCachesLocked()
    {
        _shivAvailableThisRun = false;
        _shivDeckViewCard = null;
        _sovereignBladeAvailableThisRun = false;
        _sovereignBladeDeckViewCard = null;
        _sovereignBladeDefinitionIdThisRun = null;
    }

    private static void RefreshShivAvailabilityLocked()
    {
        _shivAvailableThisRun = HasShivDataLocked();
        if (!_shivAvailableThisRun)
            _shivDeckViewCard = null;
    }

    private static void RefreshSovereignBladeAvailabilityLocked()
    {
        _sovereignBladeAvailableThisRun = HasSovereignBladeDataLocked();
        if (!_sovereignBladeAvailableThisRun)
        {
            _sovereignBladeDeckViewCard = null;
            _sovereignBladeDefinitionIdThisRun = null;
        }
    }

    private static CardModel? GetShivDeckViewCardLocked()
    {
        if (!_shivAvailableThisRun) return null;
        if (_shivDeckViewCard != null) return _shivDeckViewCard;

        try
        {
            var modelId = ModelId.Deserialize(ShivDefinitionId);
            _shivDeckViewCard = ModelDb.GetById<CardModel>(modelId).ToMutable();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"GetShivDeckViewCardLocked failed: {e.Message}");
        }

        return _shivDeckViewCard;
    }

    private static CardModel? GetSovereignBladeDeckViewCardLocked()
    {
        if (!_sovereignBladeAvailableThisRun) return null;
        if (_sovereignBladeDeckViewCard != null) return _sovereignBladeDeckViewCard;

        try
        {
            var definitionId = GetSovereignBladeDefinitionIdLocked();
            if (string.IsNullOrWhiteSpace(definitionId)) return null;

            var modelId = ModelId.Deserialize(definitionId);
            _sovereignBladeDeckViewCard = ModelDb.GetById<CardModel>(modelId).ToMutable();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"GetSovereignBladeDeckViewCardLocked failed: {e.Message}");
        }

        return _sovereignBladeDeckViewCard;
    }

    private static string? GetSovereignBladeDefinitionIdLocked()
    {
        if (!string.IsNullOrWhiteSpace(_sovereignBladeDefinitionIdThisRun))
            return _sovereignBladeDefinitionIdThisRun;

        if (_sovereignBladeDeckViewCard != null)
        {
            _sovereignBladeDefinitionIdThisRun = _sovereignBladeDeckViewCard.Id.ToString();
            return _sovereignBladeDefinitionIdThisRun;
        }

        string? eventCardId = _pendingCombat?.CombatEvents
            .LastOrDefault(e => e.Type == SovereignBladeForgedEventType && !string.IsNullOrWhiteSpace(e.CardId))
            ?.CardId;
        eventCardId ??= _currentRun?.Events
            .LastOrDefault(e => e.Type == SovereignBladeForgedEventType && !string.IsNullOrWhiteSpace(e.CardId))
            ?.CardId;

        if (!string.IsNullOrWhiteSpace(eventCardId))
            _sovereignBladeDefinitionIdThisRun = eventCardId;

        if (!string.IsNullOrWhiteSpace(_sovereignBladeDefinitionIdThisRun))
            return _sovereignBladeDefinitionIdThisRun;

        _sovereignBladeDefinitionIdThisRun =
            TryInferSovereignBladeDefinitionIdFromAggregateKeys(_pendingCombat?.CombatAggregates.Keys)
            ?? TryInferSovereignBladeDefinitionIdFromAggregateKeys(_currentRun?.Aggregates.Keys);

        if (!string.IsNullOrWhiteSpace(_sovereignBladeDefinitionIdThisRun))
            return _sovereignBladeDefinitionIdThisRun;

        try
        {
            _sovereignBladeDefinitionIdThisRun =
                ModelDb.GetId(typeof(MegaCrit.Sts2.Core.Models.Cards.SovereignBlade)).ToString();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"GetSovereignBladeDefinitionIdLocked fallback failed: {e.Message}");
        }

        _sovereignBladeDefinitionIdThisRun ??= SovereignBladeLegacyDefinitionId;
        return _sovereignBladeDefinitionIdThisRun;
    }

    internal static string? TryInferSovereignBladeDefinitionIdFromAggregateKeys(IEnumerable<string>? aggregateKeys)
    {
        if (aggregateKeys == null) return null;

        foreach (var aggregateKey in aggregateKeys)
        {
            if (string.IsNullOrWhiteSpace(aggregateKey)) continue;

            int separatorIndex = aggregateKey.LastIndexOf('#');
            if (separatorIndex <= 0) continue;

            string definitionId = aggregateKey[..separatorIndex];
            if (!definitionId.Contains(SovereignBladeLegacyDefinitionToken, StringComparison.Ordinal))
                continue;

            return definitionId;
        }

        return null;
    }

    private static bool IsSovereignBladeCard(CardModel card)
    {
        return card is MegaCrit.Sts2.Core.Models.Cards.SovereignBlade
            || string.Equals(card.GetType().Name, "SovereignBlade", StringComparison.Ordinal);
    }

    /// <summary>
    /// Core assignment primitive. Returns the stable 1-based instance
    /// number for this card, assigning on first call and caching thereafter.
    /// Counter is per-card-definition so 3 Strikes become #1/#2/#3 even
    /// if the deck also has 4 Defends (those are DEFEND#1..#4 separately).
    ///
    /// Always keyed by the canonical deck ref. Combat-time clones resolve
    /// back to their deck original via <c>DeckVersion</c>, so playing a
    /// card and hovering it afterward converge on the same number.
    /// </summary>
    private static int GetOrAssignNumber(CardModel card)
    {
        var key = Canonical(card);
        if (_instanceNumbers.TryGetValue(key, out var existing)) return existing;

        var defId = key.Id.ToString();

        // A resume/adoption may run before the game repopulates the deck.
        // Saved numbers the adoption deck walk couldn't bind wait in
        // _pendingRankRestores and are claimed here in arrival (deck) order,
        // so the reload boundary doesn't mint fresh numbers that would
        // orphan the saved stats. Cleared at combat setup, so combat-time
        // ephemeral cards can't steal a waiting number.
        if (_pendingRankRestores.TryGetValue(defId, out var waiting) && waiting.Count > 0)
        {
            var restored = waiting.Dequeue();
            if (waiting.Count == 0) _pendingRankRestores.Remove(defId);
            _instanceNumbers[key] = restored;
            StampArrival(key, restored);
            return restored;
        }

        _defCounters.TryGetValue(defId, out var n);
        n++;
        _defCounters[defId] = n;
        _instanceNumbers[key] = n;
        StampArrival(key, n);

        return n;
    }

    /// <summary>
    /// Called from <see cref="Patches.CardEnterDeckPatch"/> whenever a card
    /// enters the player's Deck pile — starter-deck population, reward/shop
    /// acquisitions, event grants, Ascender's Bane, all routed through
    /// <c>CardPile.AddInternal</c>. We just need to trigger number assignment
    /// and arrival stamping; everything downstream is automatic.
    ///
    /// This replaces the earlier "walk the deck at RunStarted" approach,
    /// which had a race condition where the deck wasn't yet populated when
    /// the RunStarted event fired on fresh runs.
    /// </summary>
    public static void RecordCardEntered(CardModel card)
    {
        lock (_lock)
        {
            // Co-op: CardPile.AddInternal(Deck) fires for BOTH decks; only mint
            // identity for the tracked player's cards.
            if (!IsTrackedCard(card)) return;
            GetOrAssignNumber(card);
            var deckCountsChanged =
                RefreshStrikeDummyDeckCountsIfOwnedLocked()
                | RefreshMiniatureCannonDeckCountsIfOwnedLocked();
            if (deckCountsChanged)
                SaveCurrentRun();
        }
    }

    /// <summary>
    /// Record when and at what upgrade level a card was first seen.
    /// Creates a bare aggregate entry if one doesn't exist yet for this
    /// instance, so the lineage info is preserved even for cards that
    /// never get played. No-op if <c>_currentRun</c> isn't set yet (pre-
    /// RunStarted edge case — rare).
    /// </summary>
    private static void StampArrival(CardModel card, int number)
    {
        if (_currentRun == null) return;
        var instanceId = $"{card.Id}#{number}";
        if (_currentRun.Aggregates.ContainsKey(instanceId)) return;  // already stamped

        // FloorAddedToDeck is the game's own truth, set in multiple places:
        //   - Player.PopulateStartingDeck: hard-coded to 1 for all starters
        //   - Mid-run adds (rewards, shops, events): set to the current floor
        //   - Card transforms that create new refs: may leave null
        //   - Ephemeral combat-only cards (Souls, Shivs): null (never enter deck)
        //
        // Fallback for null: use current floor. This matters for transformed
        // cards (the ref didn't enter the deck via the normal populate path)
        // and ephemeral cards observed via play/draw. For cards that DID enter
        // the deck properly, FloorAddedToDeck will never be null.
        int? floorAdded = card.FloorAddedToDeck;
        if (floorAdded == null)
        {
            try { floorAdded = RunManager.Instance?.State?.TotalFloor; }
            catch { /* leave null if RunManager state isn't ready */ }
        }

        _currentRun.Aggregates[instanceId] = new CardAggregate
        {
            FloorAdded = floorAdded,
            InitialUpgradeLevel = card.CurrentUpgradeLevel,
        };
    }

    /// <summary>
    /// Get-or-assign the full string instance id ("STRIKE#3" format) used
    /// as the aggregates dictionary key and the on-disk identifier. Only
    /// call from paths that SHOULD create new instance numbers — i.e. combat
    /// Record paths where an ephemeral card (Soul/Shiv/generated) being
    /// observed deserves a fresh number even if it never entered the deck.
    /// For non-assigning contexts (hover, upgrade, removal), use
    /// <see cref="TryGetInstanceId"/>.
    /// </summary>
    private static string GetOrAssignInstanceId(CardModel card)
    {
        var n = GetOrAssignNumber(card);
        var defId = Canonical(card).Id.ToString();
        return $"{defId}#{n}";
    }

    /// <summary>
    /// Non-assigning lookup — returns false if the card hasn't been observed
    /// via a deck-entry or play path. Lets upgrade/removal/hover handlers
    /// check "have we seen this card?" without burning monotonic counters
    /// on preview/template/preview-UI card observations.
    /// </summary>
    private static bool TryGetInstanceId(CardModel card, out string instanceId)
    {
        var key = Canonical(card);
        if (_instanceNumbers.TryGetValue(key, out var n))
        {
            instanceId = $"{key.Id}#{n}";
            return true;
        }
        instanceId = "";
        return false;
    }


    /// <summary>
    /// Snapshot the current <c>_instanceNumbers</c> map into the format that
    /// survives serialization: <c>{def_id → [number, number, ...]}</c> ordered
    /// by current deck-rank among same-def cards. Called before every save.
    ///
    /// The snapshot is derived (not primary) data — it's reconstructible
    /// from <c>_instanceNumbers</c> + the live deck. We keep primary in-memory
    /// and snapshot to disk only for resume-after-reload.
    /// </summary>
    private static Dictionary<string, List<int>> CaptureInstanceNumbersByDeckRank()
    {
        var result = new Dictionary<string, List<int>>();
        try
        {
            var player = RunManager.Instance.State?.Players.FirstOrDefault();
            if (player?.Deck != null)
            {
                foreach (var card in player.Deck.Cards)
                {
                    var key = Canonical(card);
                    if (!_instanceNumbers.TryGetValue(key, out var n)) continue;
                    var defId = key.Id.ToString();
                    if (!result.TryGetValue(defId, out var list))
                    {
                        list = new List<int>();
                        result[defId] = list;
                    }
                    list.Add(n);
                }
            }
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CaptureInstanceNumbersByDeckRank failed: {e.Message}");
        }

        // Numbers still queued for late deck population are part of the
        // mapping too: deck arrivals claim the queue front in deck order, so
        // the remaining queue is exactly the deck-rank suffix after whatever
        // the walk above saw. Without this, a save during adoption (repair or
        // ghost-prune) on a not-yet-repopulated deck would overwrite the
        // on-disk snapshot with just the visible prefix — and a crash before
        // the next save would permanently orphan every queued card's stats.
        foreach (var kv in _pendingRankRestores)
        {
            if (kv.Value.Count == 0) continue;
            if (!result.TryGetValue(kv.Key, out var list))
            {
                list = new List<int>();
                result[kv.Key] = list;
            }
            list.AddRange(kv.Value);
        }
        return result;
    }

    /// <summary>
    /// Populate the snapshot fields on <c>_currentRun</c> and save. Single
    /// gateway for all persistence paths — if you want to save, call this
    /// instead of <c>RunStorage.SaveAsync</c> directly, so the snapshot is
    /// always fresh on disk.
    /// </summary>
    private static void SaveCurrentRun()
    {
        if (_currentRun == null) return;

        // Never capture identity from a deck that belongs to a DIFFERENT game
        // run. During a new-run embark or continue-load, CardEnterDeckPatch
        // can repopulate the deck before RunStarted re-points _currentRun —
        // recapturing here would overwrite the old run's last good snapshot
        // and counters with the new run's deck state. Freeze identity in that
        // window; the aggregates themselves still save.
        long liveGameStartTime = 0;
        try { liveGameStartTime = RunManager.Instance._startTime; }
        catch { /* no live run state — keep the frozen snapshot */ }
        if (_currentRun.GameStartTime == null || _currentRun.GameStartTime == liveGameStartTime)
        {
            _currentRun.InstanceNumbersByDef = CaptureInstanceNumbersByDeckRank();
            _currentRun.DefCounters = new Dictionary<string, int>(_defCounters);
        }

        RunStorage.SaveAsync(_currentRun);
    }

    /// <summary>
    /// Lazily create <c>_currentRun</c> when combat/relic data arrives before
    /// <c>RunStarted</c> fired (mod hot-loaded mid-run). Always stamps
    /// <c>GameStartTime</c> so a later Continue/hot-reload can match and resume
    /// this record instead of stranding it — the previous inline mints omitted
    /// it, re-seeding the stranding bug. Single home for the lazy mint.
    /// </summary>
    private static void EnsureLazyCurrentRunLocked()
    {
        if (_currentRun != null) return;
        long gameStartTime = 0;
        try { gameStartTime = RunManager.Instance._startTime; }
        catch { /* no live run state — leave GameStartTime null */ }
        string now = Now();
        _currentRun = new RunData
        {
            RunId = Guid.NewGuid().ToString("N"),
            StartedAt = now,
            UpdatedAt = now,
            GameStartTime = gameStartTime != 0 ? gameStartTime : null,
        };
    }

    /// <summary>
    /// Run a game-event handler body under a top-level guard. These handlers
    /// are subscribed directly to RunManager/CombatManager events, so an
    /// unhandled throw would propagate into the game's own dispatch and could
    /// break combat/run flow. Always-on Error log so a broken handler is
    /// visible without a debug flag.
    /// </summary>
    // -------- Co-op tracked-player scoping (#260); all ALLOW when _trackedNetId==null --------

    private static void ResolveTrackedPlayerLocked(RunState? runState)
    {
        try
        {
            var players = runState?.Players;
            var localNetId = MegaCrit.Sts2.Core.Context.LocalContext.NetId;
            if (localNetId.HasValue && players != null && players.Any(p => p.NetId == localNetId.Value))
                _trackedNetId = localNetId.Value;
            else if (players != null && players.Count == 1)
                _trackedNetId = players[0].NetId; // SP: sole player is us
            else
                _trackedNetId = null; // co-op w/ LocalContext missing: fail open
        }
        catch (Exception e)
        {
            _trackedNetId = null;
            CoreMain.LogDebug($"ResolveTrackedPlayer failed (tracking all): {e.Message}");
        }
        CoreMain.Logger.Info($"TrackedPlayer resolved: net_id={(_trackedNetId?.ToString() ?? "<all>")}");
    }

    private static bool IsTrackedPlayer(Player? player)
        => _trackedNetId == null || (player != null && player.NetId == _trackedNetId.Value);

    private static bool IsTrackedPlayerCreature(Creature? creature)
        => _trackedNetId == null || (creature?.Player != null && creature.Player.NetId == _trackedNetId.Value);

    private static bool IsTrackedCard(CardModel? card)
    {
        if (_trackedNetId == null) return true;
        if (card == null || !card.IsMutable) return true;
        var owner = card.Owner;
        return owner == null || owner.NetId == _trackedNetId.Value;
    }

    public static bool IsTrackedRelic(MegaCrit.Sts2.Core.Models.RelicModel? relic)
    {
        lock (_lock)
        {
            if (_trackedNetId == null) return true;
            if (relic == null || !relic.IsMutable) return true;
            var owner = relic.Owner;
            return owner == null || owner.NetId == _trackedNetId.Value;
        }
    }

    private static void GuardLifecycle(string site, Action body)
    {
        try { body(); }
        catch (Exception e) { CoreMain.Logger.Error($"{site} failed: {e}"); }
    }

    private static void ResetCombatContextState()
    {
        _currentPlayerCardPlay = null;
        _recentCompletedPlayerCardPlay = null;
        _recentCompletedPlayerCardPlayHistoryCount = 0;
        _pendingDrawSourceCard = null;
        _pendingDrawAttempts.Clear();
        _pendingEffectSourceCard = null;
        _pendingEffectSourceHistoryCount = 0;
        _pendingPowerChangeAttempts.Clear();
        _pendingPlayerBlockClearAmount = 0;
        _pendingPlayerBlockClearArmed = false;
        // Ported windows (Orichalcum, Anchor, Abacus, BoneFlute, CloakClasp,
        // HappyFlower, BoomingConch, GremlinHorn energy+draw) now live on
        // PendingCombat.Windows and reset with a fresh PendingCombat.
        // DEFERRED (not ported this pass — keep their own reset):
        _pendingAkabekoVigorAttribution = false;
        _pendingPendulumDrawAttributions.Clear();
        _pendingParryingShieldDamageAttributions.Clear();
        _pendingFestivePopperDamageAttributions.Clear();
        _pendingMercuryHourglassDamageAttributions.Clear();
        _pendingMrStrugglesDamageAttributions.Clear();
        _bronzeScalesThornsContributions.Clear();
        _pendingBronzeScalesDamageAttributions.Clear();
        _pendingHornCleatBlockAttributions.Clear();
        _lastEnergyResetRoundByRelicAndPlayer.Clear();
        _pendingToolboxOfferScreens = 0;
        _pendingMakeItSoSummons.Clear();
        _pendingReplayExtraPlaySources.Clear();
        _pendingReplayExtraPlaySeriesStarted.Clear();
        _pendingReplayAttackOutcomes.Clear();
        _observedDamageResults.Clear();
        if (_pendingRankRestores.Count > 0)
        {
            // Leftover queued numbers mean saved cards never re-arrived via
            // CardEnterDeckPatch before combat — their aggregates stay
            // unbound. Should be empty here; log so a rebind gap is visible.
            var leftovers = string.Join(", ", _pendingRankRestores.Select(kv => $"{kv.Key}x{kv.Value.Count}"));
            CoreMain.Logger.Info($"ResetCombatContextState: discarding unclaimed rank restores: {leftovers}");
            _pendingRankRestores.Clear();
        }
    }

    private static void ResetRewardContextState()
    {
        _pendingHeftyTabletChoicePlayers.Clear();
        _paelSacrificeRewards.Clear();
        _pendingRegalPillowRestHeals.Clear();
        _pendingPrecariousShearsPickups.Clear();
        _pendingLeafyPoulticePickups.Clear();
        _pendingSandCastlePickups.Clear();
        _pendingWhetstonePickups.Clear();
        _pendingWarPaintPickups.Clear();
    }

    /// <summary>
    /// On Core assembly reload, detect if the game is in an active run and,
    /// if so, load the matching run file from disk and rebuild the
    /// CardModel → number mapping so stats attribution continues uninterrupted.
    ///
    /// Matching key is <c>RunManager._startTime</c> (Unix seconds of run
    /// start) — stable across our reloads because the game's RunManager
    /// lives in the game's assembly, not ours. Our saved run file records
    /// this in <c>RunData.GameStartTime</c> on every save, so we can scan
    /// the runs/ dir and find our in-progress record.
    ///
    /// If no active run, no-op — next <c>OnRunStarted</c> will set things
    /// up fresh when a run begins.
    ///
    /// If the game IS in a run but no saved file matches (e.g. the user
    /// played through several combats before first installing this mod),
    /// also no-op — we start tracking fresh from the next combat, which
    /// loses history but doesn't crash.
    /// </summary>
    public static void TryResumeActiveRun()
    {
        lock (_lock)
        {
            try
            {
                var runState = RunManager.Instance.State;
                if (runState == null)
                {
                    CoreMain.LogDebug("TryResumeActiveRun: no active RunState; nothing to resume");
                    return;
                }

                var gameStartTime = RunManager.Instance._startTime;
                if (gameStartTime == 0)
                {
                    CoreMain.LogDebug("TryResumeActiveRun: _startTime is 0; nothing to resume");
                    return;
                }

                // requireInProgress: a hot reload on the game-over screen still
                // has live RunState and _startTime — adopting the just-
                // finalized record would resurrect a finished run.
                var saved = RunStorage.FindByGameStartTime(gameStartTime, out var foundUnsupportedMatch, requireInProgress: true);
                if (saved == null)
                {
                    if (foundUnsupportedMatch)
                    {
                        CoreMain.Logger.Info(
                            $"TryResumeActiveRun: found saved run for game_start_time={gameStartTime}, " +
                            "but its schema is not resumable into current live tracking");
                    }
                    else
                    {
                        CoreMain.Logger.Info(
                            $"TryResumeActiveRun: no saved run matches game_start_time={gameStartTime}; " +
                            "tracking will begin fresh on next combat");
                    }
                    return;
                }

                AdoptRunLocked(saved, runState, "TryResumeActiveRun", repairAggregates: true);
            }
            catch (Exception e)
            {
                CoreMain.Logger.Error($"TryResumeActiveRun failed: {e}");
            }
        }
    }

    /// <summary>
    /// Make <paramref name="run"/> the tracked current run and rebind per-card
    /// identity to the CURRENT CardModel refs. Shared by hot-reload resume
    /// (<see cref="TryResumeActiveRun"/>), main-menu Continue re-fires of
    /// RunStarted (same run object kept in memory), and Continue-after-game-
    /// restart adoption from disk.
    ///
    /// The deck walk maps live refs to saved numbers by (def, deck-rank);
    /// snapshot numbers the walk couldn't bind (deck not yet repopulated on
    /// some continue-load orderings) wait in <see cref="_pendingRankRestores"/>
    /// for CardEnterDeckPatch arrivals. Ghost aggregates minted by
    /// CardEnterDeckPatch firing BEFORE RunStarted on continue-loads are
    /// pruned afterward.
    /// </summary>
    private static void AdoptRunLocked(RunData run, RunState? runState, string context, bool repairAggregates)
    {
        _currentRun = run;
        // Re-derive the tracked local player on every adopt/resume/continue
        // (hot-reload, main-menu Continue, continue-after-restart).
        ResolveTrackedPlayerLocked(runState);
        // Old builds stamped EndedAt on every Continue re-fire; an in-progress
        // run is live again the moment we adopt it. Guarded on outcome so a
        // defensive future caller can't resurrect a genuinely finished record
        // (every current caller already filters to in_progress).
        if (run.Outcome == "in_progress")
            run.EndedAt = null;

        bool repairedDamageAggregates = repairAggregates && RepairOffensiveDamageAggregatesFromEvents(run);

        _pendingCombat = null;
        ResetCombatContextState();
        ResetRewardContextState();
        _instanceNumbers.Clear();
        _defCounters.Clear();
        ResetDeckViewCachesLocked();

        // Restore monotonic counters first so any lazy-assign after
        // this picks up the next unused number (not a conflict).
        if (run.DefCounters != null)
        {
            foreach (var kv in run.DefCounters) _defCounters[kv.Key] = kv.Value;
        }

        // Seed every saved instance number, in deck-rank order, into the
        // pending-restore queues, then walk the live deck through the normal
        // GetOrAssignNumber path: each deck card claims its def's next queued
        // number in arrival order — the same (def, rank) binding the snapshot
        // was captured with. Removal-safe: if the player Smith'd Strike #3,
        // the saved list for STRIKE is [1, 2, 4, 5] and deck order claims
        // #1, #2, #4, #5. Whatever the walk can't bind (deck not yet
        // repopulated on some continue-load orderings) stays queued for
        // CardEnterDeckPatch arrivals; deck cards beyond the snapshot fall
        // through to the restored counters. One binding mechanism regardless
        // of whether the deck populates before or after this runs.
        int seeded = 0;
        if (run.InstanceNumbersByDef != null)
        {
            foreach (var kv in run.InstanceNumbersByDef)
            {
                if (kv.Value.Count == 0) continue;
                _pendingRankRestores[kv.Key] = new Queue<int>(kv.Value);
                seeded += kv.Value.Count;
            }
        }

        int deckCards = 0;
        var player = runState?.Players.FirstOrDefault();
        if (player?.Deck != null)
        {
            foreach (var card in player.Deck.Cards)
            {
                deckCards++;
                GetOrAssignNumber(card);
            }
        }
        bool strikeDummyDeckCountsChanged = RefreshStrikeDummyDeckCountsIfOwnedLocked();
        bool miniatureCannonDeckCountsChanged = RefreshMiniatureCannonDeckCountsIfOwnedLocked();
        int stillWaiting = _pendingRankRestores.Values.Sum(q => q.Count);
        int restored = seeded - stillWaiting;
        int unmatched = deckCards - restored;

        // If the deck was already (re)populated when we adopted, leftover
        // queued numbers aren't late-population waiters — they're snapshot
        // entries the game's own save no longer contains (e.g. a crash rolled
        // the save back past a card acquisition). Keeping them queued would
        // let the next same-def acquisition claim a dead card's number and
        // silently inherit its stats. Drop them; their aggregates stay in the
        // file as history (the ≤-saved-counter rule shields them from the
        // ghost prune below).
        if (deckCards > 0 && stillWaiting > 0)
        {
            var dropped = string.Join(", ", _pendingRankRestores.Select(kv => $"{kv.Key}x{kv.Value.Count}"));
            CoreMain.Logger.Info($"{context}: dropping stale rank restores not present in the live deck: {dropped}");
            _pendingRankRestores.Clear();
        }

        // Reconstruct refs for REMOVED cards. These aren't in
        // player.Deck.Cards anymore, so the deck walk above didn't
        // find them. But they still need entries in _instanceNumbers
        // so GetRemovedCards() can surface them for the deck-view
        // injection.
        //
        // State-accurate reconstruction: we snapshot the card's
        // full SerializableCard state at removal time (upgrade
        // level, enchantment, etc.) and use CardModel.FromSerializable
        // to rebuild a ref matching the removed card's state.
        // If no snapshot exists (aggregate from a pre-snapshot
        // build), fall back to a canonical ref via ModelDb.
        int reconstructedRemoved = 0;
        if (run.Aggregates != null)
        {
            foreach (var kv in run.Aggregates)
            {
                if (!kv.Value.Removed) continue;
                if (!TryParseAggregateKey(kv.Key, out var defIdStr, out var num)) continue;
                try
                {
                    CardModel reconstructed;
                    if (kv.Value.RemovedSnapshot != null)
                    {
                        reconstructed = CardModel.FromSerializable(kv.Value.RemovedSnapshot);
                    }
                    else
                    {
                        var modelId = MegaCrit.Sts2.Core.Models.ModelId.Deserialize(defIdStr);
                        reconstructed = MegaCrit.Sts2.Core.Models.ModelDb.GetById<CardModel>(modelId).ToMutable();
                    }
                    _instanceNumbers[reconstructed] = num;
                    reconstructedRemoved++;
                }
                catch (Exception e)
                {
                    CoreMain.LogDebug($"{context}: couldn't reconstruct {kv.Key}: {e.Message}");
                }
            }
        }

        int prunedGhosts = PruneGhostAggregates(run, run.DefCounters, BuildLiveInstanceIdSetLocked());

        CoreMain.Logger.Info(
            $"{context}: resumed run_id={run.RunId} " +
            $"game_start_time={run.GameStartTime} aggregates={run.Aggregates?.Count ?? 0} " +
            $"reconstructed_removed={reconstructedRemoved} " +
            $"restored_numbers={restored} unmatched_in_deck={unmatched} " +
            $"pending_rank_restores={_pendingRankRestores.Count} pruned_ghosts={prunedGhosts}");

        RefreshShivAvailabilityLocked();
        RefreshSovereignBladeAvailabilityLocked();

        if (repairedDamageAggregates)
        {
            CoreMain.Logger.Info(
                $"{context}: repaired offensive damage aggregates for run_id={run.RunId}");
        }
        if (repairedDamageAggregates
            || prunedGhosts > 0
            || strikeDummyDeckCountsChanged
            || miniatureCannonDeckCountsChanged)
        {
            SaveCurrentRun();
        }
    }

    /// <summary>
    /// Every instance id ("DEF#N") currently bound to a live card ref, plus
    /// numbers still waiting in <see cref="_pendingRankRestores"/> for late
    /// deck population. Input to <see cref="PruneGhostAggregates"/>.
    /// </summary>
    private static HashSet<string> BuildLiveInstanceIdSetLocked()
    {
        var live = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kv in _instanceNumbers)
            live.Add($"{kv.Key.Id}#{kv.Value}");
        foreach (var kv in _pendingRankRestores)
            foreach (var n in kv.Value)
                live.Add($"{kv.Key}#{n}");
        return live;
    }

    /// <summary>
    /// Remove ghost aggregates minted by CardEnterDeckPatch firing before
    /// RunStarted on a continue-load: the repopulated deck's fresh CardModel
    /// refs get brand-new instance numbers (continuing the old counters) and
    /// empty arrival stamps before the adoption rebind can map them back to
    /// their saved numbers. Those stamps are pure file pollution — never
    /// played, never referenced, invisible after the rebind.
    ///
    /// Conservative by construction: prunes only aggregates whose number
    /// exceeds the last-saved counter for their def, that aren't bound to any
    /// live card, hold no recorded activity, and are referenced by no event.
    /// A pruned number can be re-minted later; that's safe precisely because
    /// nothing ever referenced it.
    /// </summary>
    internal static int PruneGhostAggregates(
        RunData run,
        Dictionary<string, int>? savedDefCounters,
        HashSet<string> liveInstanceIds)
    {
        if (run.Aggregates == null || run.Aggregates.Count == 0) return 0;

        // One pass over the event log up front; candidates check membership
        // instead of rescanning the (thousands-long) list each.
        HashSet<string>? eventCardIds = null;
        if (run.Events != null && run.Events.Count > 0)
        {
            eventCardIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var cardEvent in run.Events) eventCardIds.Add(cardEvent.CardId);
        }

        List<string>? toRemove = null;
        foreach (var kv in run.Aggregates)
        {
            if (!TryParseAggregateKey(kv.Key, out var defId, out var num)) continue;

            int savedCounter = 0;
            savedDefCounters?.TryGetValue(defId, out savedCounter);
            if (num <= savedCounter) continue;                       // predates the last save — trust it
            if (liveInstanceIds.Contains(kv.Key)) continue;          // bound to a live card
            if (!IsGhostStampAggregate(kv.Value)) continue;          // has recorded activity
            if (eventCardIds != null && eventCardIds.Contains(kv.Key)) continue;

            (toRemove ??= new List<string>()).Add(kv.Key);
        }

        if (toRemove == null) return 0;
        foreach (var key in toRemove) run.Aggregates.Remove(key);
        return toRemove.Count;
    }

    /// <summary>
    /// Split an aggregate key ("CARD.STRIKE#3") into its definition id and
    /// instance number. False for keys that don't carry a numeric instance
    /// suffix (e.g. historic pooled-shape keys).
    /// </summary>
    private static bool TryParseAggregateKey(string key, out string defId, out int number)
    {
        defId = "";
        number = 0;
        var hashIdx = key.LastIndexOf('#');
        if (hashIdx < 0) return false;
        if (!int.TryParse(key.Substring(hashIdx + 1), out number)) return false;
        defId = key.Substring(0, hashIdx);
        return true;
    }

    private static readonly System.Text.Json.JsonSerializerOptions GhostCompareOptions = new();

    /// <summary>
    /// True when the aggregate holds nothing beyond an arrival stamp
    /// (FloorAdded / InitialUpgradeLevel) — the shape a pre-RunStarted
    /// CardEnterDeckPatch ghost has. Compared structurally against a bare
    /// stamp with the same lineage so ANY recorded activity — including
    /// fields added after this check was written — disqualifies. Runs only
    /// during adoption on a handful of candidates, so the serialization cost
    /// is irrelevant next to the maintenance risk of a hand-kept field list.
    /// </summary>
    private static bool IsGhostStampAggregate(CardAggregate agg)
    {
        if (agg.Removed) return false;
        var bareStamp = new CardAggregate
        {
            FloorAdded = agg.FloorAdded,
            InitialUpgradeLevel = agg.InitialUpgradeLevel,
        };
        return System.Text.Json.JsonSerializer.Serialize(agg, GhostCompareOptions)
            == System.Text.Json.JsonSerializer.Serialize(bareStamp, GhostCompareOptions);
    }

    // -------- Lifecycle callbacks --------

    private static void OnRunStarted(RunState runState) =>
        GuardLifecycle(nameof(OnRunStarted), () => OnRunStartedImpl(runState));

    private static void OnRunStartedImpl(RunState runState)
    {
        lock (_lock)
        {
            long gameStartTime = 0;
            try { gameStartTime = RunManager.Instance._startTime; }
            catch (Exception e) { CoreMain.LogDebug($"OnRunStarted: couldn't read _startTime: {e.Message}"); }

            // The game re-fires RunStarted with the SAME _startTime whenever a
            // saved run is continued from the main menu ("Continuing run with
            // character"). That's a continuation of the run we're already
            // tracking, not a new run — minting a fresh RunData here is what
            // used to strand every previously-committed stat in an orphaned
            // file and reset tooltips to zero mid-run.
            if (gameStartTime != 0 && _currentRun != null
                && _currentRun.GameStartTime == gameStartTime
                && _currentRun.Outcome == "in_progress")
            {
                AdoptRunLocked(_currentRun, runState, "RunStarted(continue)", repairAggregates: false);
                return;
            }

            // If a previous (different) run was in progress (mod reload, unusual path), finalize it first.
            if (_currentRun != null)
            {
                // The NEW run's pre-RunStarted deck population may have stamped
                // ghost aggregates into the old run's data (CardEnterDeckPatch
                // fires before RunStarted). This is the old run's last save —
                // scrub them now or they're permanent. Empty live set: nothing
                // in the identity map legitimately belongs to the old run
                // beyond its last-saved counters at this point.
                PruneGhostAggregates(_currentRun, _currentRun.DefCounters, new HashSet<string>(StringComparer.Ordinal));
                _currentRun.EndedAt = Now();
                SaveCurrentRun();
            }

            // Continue after a game restart: our statics are fresh but the
            // run isn't. Adopt the newest resumable on-disk record for this
            // game run rather than starting a stranded parallel file.
            if (gameStartTime != 0)
            {
                var saved = RunStorage.FindByGameStartTime(gameStartTime, out _, requireInProgress: true);
                if (saved != null && saved.Outcome == "in_progress")
                {
                    AdoptRunLocked(saved, runState, "RunStarted(adopt-saved)", repairAggregates: true);
                    return;
                }
            }

            // Per-instance identity is per-run. Clear assignments so the next
            // run's Strike #1 is genuinely "this new run's first Strike,"
            // not a hangover from a previous run.
            _lastEndedRun = null;
            _instanceNumbers.Clear();
            _defCounters.Clear();
            ResetCombatContextState();
            ResetRewardContextState();
            ResetDeckViewCachesLocked();
            ResolveTrackedPlayerLocked(runState);

            string now = Now();
            _currentRun = new RunData
            {
                RunId = Guid.NewGuid().ToString("N"),
                StartedAt = now,
                UpdatedAt = now,
                Character = runState.Players.FirstOrDefault()?.Character?.Id.ToString(),
                Ascension = runState.AscensionLevel,
                FloorReached = runState.TotalFloor,
                // The game's own run identifier (RunManager._startTime via
                // Publicizer) — matches the filename it uses for its
                // run-history save ({StartTime}.run). Enables M5 correlation.
                // Reuses the guarded read from the top of this method: a
                // second raw read here would rethrow into the game's
                // RunStarted dispatch exactly when the first read failed.
                GameStartTime = gameStartTime != 0 ? gameStartTime : null,
            };
            _pendingCombat = null;

            // Note: deck cards are NOT walked here. The RunStarted event
            // fires before the game finishes populating player.Deck.Cards
            // on fresh runs, so walking now would miss the starters. Instead
            // we observe each card as it enters via CardEnterDeckPatch, which
            // catches starter population, mid-run acquisitions, and
            // Ascender's Bane uniformly.

            CoreMain.Logger.Info($"RunStarted: {_currentRun.RunId} character={_currentRun.Character} ascension={_currentRun.Ascension} game_start_time={_currentRun.GameStartTime}");
            SaveCurrentRun();
        }
    }

    /// <summary>
    /// Called from the RunManager.OnEnded postfix. Stamps the final outcome and
    /// EndedAt on the current run, persists it, and nulls the tracker state so
    /// the next RunStarted starts fresh.
    ///
    /// Outcome priority (matches the game's own truth):
    ///   abandoned   — user chose Abandon Run (IsAbandoned)
    ///   win         — cleared final act boss (isVictory && !IsAbandoned)
    ///   loss        — player died (neither of the above)
    /// </summary>
    public static void OnRunEnded(string outcome)
    {
        lock (_lock)
        {
            if (_currentRun == null) return;

            // A loss reaches RunManager.OnEnded synchronously from the killing
            // action; the fatal combat's CombatEnded only fires LATER via
            // ProcessPendingLoss — after this handler has consumed the buffer.
            // So promote the fatal combat here: the fight genuinely resolved
            // (the player died). Loss only: an Abandon Run mid-combat is NOT a
            // resolved combat, and promoting it would commit a half-played
            // fight (and mislabel surviving block as wasted); wins always get
            // a normal CombatEnded first, so the buffer is already null there.
            if (string.Equals(outcome, "loss", StringComparison.Ordinal))
                PromotePendingCombatIntoRunLocked();

            _currentRun.Outcome = outcome;
            _currentRun.EndedAt = Now();
            _currentRun.UpdatedAt = _currentRun.EndedAt;

            // Capture final floor too — run could have ended mid-combat (loss)
            // with map position already advanced, or mid-rest, etc.
            var runState = RunManager.Instance.State;
            if (runState != null)
            {
                _currentRun.FloorReached = runState.TotalFloor;
            }

            CoreMain.Logger.Info($"RunEnded: {_currentRun.RunId} outcome={outcome} floor={_currentRun.FloorReached}");
            SaveCurrentRun();

            _lastEndedRun = _currentRun;

            // Clear state so the next OnRunStarted sees a clean slate.
            _currentRun = null;
            _pendingCombat = null;
            ResetCombatContextState();
            ResetRewardContextState();
            ResetDeckViewCachesLocked();
        }
    }

    private static void OnCombatSetUp(CombatState state) =>
        GuardLifecycle(nameof(OnCombatSetUp), () => OnCombatSetUpImpl(state));

    private static void OnCombatSetUpImpl(CombatState state)
    {
        lock (_lock)
        {
            // Fresh pending buffer for this combat. Anything accumulated from a prior
            // combat that didn't get a CombatEnded (shouldn't happen but defensive) is dropped.
            _pendingCombat = new PendingCombat();
            ResetCombatContextState();
            RecordCombatsInDeckForCurrentDeckLocked();
            RecordHeldCombatRelicBaselinesForTrackedPlayerLocked(requireActiveCombat: false, createPendingIfNeeded: false);
        }
    }

    private static void OnCombatEnded(CombatRoom room) =>
        GuardLifecycle(nameof(OnCombatEnded), () => OnCombatEndedImpl(room));

    private static void OnCombatEndedImpl(CombatRoom room)
    {
        lock (_lock)
        {
            if (_pendingCombat == null) return;  // nothing to commit

            // Lazy run creation: if events came in before RunStarted ever fired
            // (e.g. mod loaded mid-run), create a minimal run record now so we
            // don't drop the combat's data.
            EnsureLazyCurrentRunLocked();

            PromotePendingCombatIntoRunLocked();

            // Refresh run-level metadata from the current game state (floor may have advanced).
            var runState = RunManager.Instance.State;
            if (runState != null)
            {
                _currentRun.FloorReached = runState.TotalFloor;
                _currentRun.Ascension ??= runState.AscensionLevel;
                _currentRun.Character ??= runState.Players.FirstOrDefault()?.Character?.Id.ToString();
            }
            _currentRun.UpdatedAt = Now();

            _pendingCombat = null;
            ResetCombatContextState();
            SaveCurrentRun();
        }
    }

    /// <summary>
    /// Promote the pending combat buffer into the committed run state. Shared
    /// by <see cref="OnCombatEnded"/> (the normal path) and
    /// <see cref="OnRunEnded"/> — a lost run ends mid-combat with no
    /// CombatEnded, and without this promotion everything the player did in
    /// the fatal combat would be discarded with the buffer.
    /// </summary>
    private static void PromotePendingCombatIntoRunLocked()
    {
        if (_pendingCombat == null || _currentRun == null) return;
        RecordHeldCombatRelicBaselinesForTrackedPlayerLocked(requireActiveCombat: false, createPendingIfNeeded: false);
        RecordPaelsEyeCombatsWithoutActivationForTrackedPlayerLocked();
        RecordNunchakuCombatEndChargeForTrackedPlayerLocked();

        // Surviving player block at combat end never absorbed future
        // damage, so treat any remaining ledger as wasted before
        // promoting the combat aggregates into the run.
        AttributeUnusedBlockLocked(TotalTrackedPlayerBlockLocked());

        PromotePendingCombatIntoRun(_pendingCombat, _currentRun);
    }

    /// <summary>
    /// Pure merge of a pending combat buffer into a run record. Internal so
    /// tests can pin the promotion behavior without a live game.
    /// </summary>
    internal static void PromotePendingCombatIntoRun(PendingCombat pending, RunData run)
    {
        // Promote pending buffer into the run's committed state.
        foreach (var (cardId, combatAgg) in pending.CombatAggregates)
        {
            var runAgg = GetOrCreateAggregate(run, cardId);
            MergeAggregateInto(runAgg, combatAgg);
        }
        run.Events.AddRange(pending.CombatEvents);

        foreach (var (relicId, pendingRelicAgg) in pending.RelicAggregates)
        {
            if (!run.RelicAggregates.TryGetValue(relicId, out var runRelicAgg))
            {
                runRelicAgg = new RelicAggregate();
                run.RelicAggregates[relicId] = runRelicAgg;
            }
            MergeRelicAggregateInto(runRelicAgg, pendingRelicAgg);
        }

        foreach (var (enemyId, pendingEnemyAgg) in pending.EnemyAggregates)
        {
            if (!run.EnemyAggregates.TryGetValue(enemyId, out var runEnemyAgg))
            {
                runEnemyAgg = new EnemyAggregate { EnemyId = enemyId };
                run.EnemyAggregates[enemyId] = runEnemyAgg;
            }

            MergeEnemyAggregateInto(runEnemyAgg, pendingEnemyAgg);
        }

        MergeMetaStatsInto(run.MetaStats, pending.MetaStats);
    }

    /// <summary>
    /// Additive merge of one relic aggregate into another. The single home
    /// for relic-field accumulation (mirrors <c>MergeAggregateInto</c> /
    /// <c>MergeEnemyAggregateInto</c>) — used by both combat promotion and
    /// the mid-combat tooltip overlay so the two can't drift. Add new relic
    /// stat fields HERE, once.
    /// </summary>
    internal static void MergeRelicAggregateInto(RelicAggregate target, RelicAggregate source)
    {
        target.Activations += source.Activations;
        target.EnemiesAffected += source.EnemiesAffected;
        target.VulnerableApplied += source.VulnerableApplied;
        target.WeakApplied += source.WeakApplied;
        target.AdditionalCardsDrawn += source.AdditionalCardsDrawn;
        target.AdditionalBlockGained += source.AdditionalBlockGained;
        target.BlockedTriggers += source.BlockedTriggers;
        target.StrengthAdded += source.StrengthAdded;
        target.PlatingAdded += source.PlatingAdded;
        target.CardsUpgraded += source.CardsUpgraded;
        MergeUpgradedCardsInto(target, source);
        target.BoneFluteTriggers += source.BoneFluteTriggers;
        target.TotalOstyHpSummoned += source.TotalOstyHpSummoned;
        target.CursesAcquired += source.CursesAcquired;
        target.TotalMaxHpGained += source.TotalMaxHpGained;
        target.TotalHealingAttempted += source.TotalHealingAttempted;
        target.TotalHealingRestored += source.TotalHealingRestored;
        target.TotalHealingLost += source.TotalHealingLost;
        MergeHealingLostReasonsInto(target, source);
        target.MaxHpGained += source.MaxHpGained;
        if (source.OriginalMaxHp.HasValue && !target.OriginalMaxHp.HasValue)
            target.OriginalMaxHp = source.OriginalMaxHp;
        if (source.NewMaxHp.HasValue)
            target.NewMaxHp = source.NewMaxHp;
        target.DoomDeathTriggers += source.DoomDeathTriggers;
        target.DoomKills += source.DoomKills;
        target.EnergyGenerated += source.EnergyGenerated;
        target.EnergyGeneratedCombats += source.EnergyGeneratedCombats;
        target.SecondTurnsEndedWithExcessEnergy += source.SecondTurnsEndedWithExcessEnergy;
        target.VigorGained += source.VigorGained;
        target.TotalDamageAttempted += source.TotalDamageAttempted;
        target.TotalDamageDealt += source.TotalDamageDealt;
        target.TotalDamageBlocked += source.TotalDamageBlocked;
        target.TotalDamageOverkill += source.TotalDamageOverkill;
        target.Kills += source.Kills;
        target.TotalTargets += source.TotalTargets;
        target.PotionsGained += source.PotionsGained;
        target.CommonPotionsGained += source.CommonPotionsGained;
        target.UncommonPotionsGained += source.UncommonPotionsGained;
        target.RarePotionsGained += source.RarePotionsGained;
        target.PotionsSkipped += source.PotionsSkipped;
        target.RelicsAcquired += source.RelicsAcquired;
        target.CommonRelicsAcquired += source.CommonRelicsAcquired;
        target.UncommonRelicsAcquired += source.UncommonRelicsAcquired;
        target.RareRelicsAcquired += source.RareRelicsAcquired;
        target.CampfiresNotDug += source.CampfiresNotDug;
        target.UncommonCardsOffered += source.UncommonCardsOffered;
        target.RareCardsOffered += source.RareCardsOffered;
        target.UncommonCardsTaken += source.UncommonCardsTaken;
        target.RareCardsTaken += source.RareCardsTaken;
        target.CommonCardsConsumed += source.CommonCardsConsumed;
        target.UncommonCardsConsumed += source.UncommonCardsConsumed;
        target.RareCardsConsumed += source.RareCardsConsumed;
        target.SacrificesMade += source.SacrificesMade;
        target.SacrificesSkipped += source.SacrificesSkipped;
        target.StatusCardsExhausted += source.StatusCardsExhausted;
        target.CurseCardsExhausted += source.CurseCardsExhausted;
        target.CombatsWithoutActivation += source.CombatsWithoutActivation;

        target.StrikeDummyStrikesPlayed += source.StrikeDummyStrikesPlayed;
        if (source.StrikeDummyBaseStrikesInDeck != 0 || target.StrikeDummyBaseStrikesInDeck == 0)
            target.StrikeDummyBaseStrikesInDeck = source.StrikeDummyBaseStrikesInDeck;
        if (source.StrikeDummyNonBaseStrikeCardsInDeck != 0 || target.StrikeDummyNonBaseStrikeCardsInDeck == 0)
            target.StrikeDummyNonBaseStrikeCardsInDeck = source.StrikeDummyNonBaseStrikeCardsInDeck;

        if (source.MiniatureCannonUpgradedAttacksInDeck != 0 || target.MiniatureCannonUpgradedAttacksInDeck == 0)
            target.MiniatureCannonUpgradedAttacksInDeck = source.MiniatureCannonUpgradedAttacksInDeck;
        target.MiniatureCannonUpgradedAttackPlays += source.MiniatureCannonUpgradedAttackPlays;
        target.MiniatureCannonUpgradedAttackHits += source.MiniatureCannonUpgradedAttackHits;

        target.BookmarkCombats += source.BookmarkCombats;
        target.BookmarkCommonActivations += source.BookmarkCommonActivations;
        target.BookmarkUncommonActivations += source.BookmarkUncommonActivations;
        target.BookmarkRareActivations += source.BookmarkRareActivations;

        target.NunchakuAttacksPlayed += source.NunchakuAttacksPlayed;
        target.NunchakuCombatsEndedOn8Charges += source.NunchakuCombatsEndedOn8Charges;
        target.NunchakuCombatsEndedOn9Charges += source.NunchakuCombatsEndedOn9Charges;
        target.NunchakuCombatEndChargeTotal += source.NunchakuCombatEndChargeTotal;

        target.DiscountCombats += source.DiscountCombats;
        target.DiscountsOffered += source.DiscountsOffered;
        target.DiscountsTaken += source.DiscountsTaken;
        target.EnergySavedByDiscount += source.EnergySavedByDiscount;
        MergeDiscountedCardCosts(target, source);
        target.CardsDiscarded += source.CardsDiscarded;
        target.QuestionMarkSitesEntered += source.QuestionMarkSitesEntered;
        MergeCardsRemovedInto(target, source);
        if (source.StartingMaxHp.HasValue) target.StartingMaxHp = source.StartingMaxHp;
        if (source.ResultingMaxHp.HasValue) target.ResultingMaxHp = source.ResultingMaxHp;
        target.CardRewardsAffected += source.CardRewardsAffected;
        MergeCardRewardCategories(target.CardRewardCategories, source.CardRewardCategories);
        MergeRelicCardsGranted(target.CardsGranted, source.CardsGranted);
        target.CardChoicesSkipped += source.CardChoicesSkipped;
        MergeRelicCardTransformations(target, source);
    }

    private static void MergeDiscountedCardCosts(RelicAggregate target, RelicAggregate source)
    {
        target.DiscountedCardCosts ??= new Dictionary<string, DiscountedCardCostAggregate>();
        if (source.DiscountedCardCosts == null) return;

        foreach (var bucket in source.DiscountedCardCosts.Values)
        {
            AddDiscountedCardCost(target, bucket.EnergyCost, bucket.StarCost, bucket.Count);
        }
    }

    // -------- Event observation (from CombatHistory.Add postfix) --------

    /// <summary>
    /// Route a freshly-added CombatHistoryEntry into the pending combat buffer.
    /// Only attack-relevant entries are consumed in M1; others will be handled
    /// by later milestones.
    /// </summary>
    private static int _observeCount;
    public static void Observe(object entry)
    {
        // Guard + throttle HERE, not just at the CombatHistoryAddPatch caller.
        // This method wraps its whole body and never rethrows, so a PatchGuard
        // at the call site would never see a throw — its per-site throttle
        // would be dead. Routing Observe's own guard through PatchGuard gives
        // this (the busiest hook) the rate-limited always-on Error logging, and
        // covers every Observe caller — including the combat-ending
        // suppressed-damage path, which is NOT itself PatchGuard-wrapped.
        PatchGuard.Run("RunTracker.Observe", () =>
        {
            var n = System.Threading.Interlocked.Increment(ref _observeCount);

            // Debug-level: per-event trace. Silent in production, verbose
            // when CUS_DEBUG is set. (The always-on [CUS-diag] type-count and
            // first-500 dumps that lived here were a temporary draw-tracking
            // probe — the question is resolved and documented in the primer,
            // and the backing counter was the one tracker static mutated
            // outside _lock, so it was removed.)
            CoreMain.LogDebug($"Observe #{n}: {entry.GetType().Name}");

            switch (entry)
            {
                case CardPlayStartedEntry cps when cps.CardPlay != null:
                    NoteCardPlayStarted(cps.CardPlay);
                    break;
                case CardPlayFinishedEntry cpf:
                    var card = cpf.CardPlay?.Card;
                    // Log both the raw (clone) hash and canonical (deck) hash.
                    // At hover time, the deck view sees canonicalHash — matching
                    // the two is how we verify the DeckVersion-based key works.
                    CoreMain.LogDebug($"  -> RecordCardPlay '{card?.Title ?? "?"}' hash={card?.GetHashCode()} canonicalHash={(card == null ? 0 : Canonical(card).GetHashCode())}");
                    if (cpf.CardPlay != null)
                    {
                        NoteCardPlayFinished(cpf.CardPlay);
                        RecordCardPlay(cpf.CardPlay);
                    }
                    break;
                case CardDrawnEntry cde:
                    // Draw-hook validation is complete; keep the trace debug-gated.
                    CoreMain.LogDebug($"CardDrawnEntry card='{cde.Card?.Title ?? "null"}' fromHandDraw={cde.FromHandDraw}");
                    if (cde.Card != null) RecordCardDrawn(cde);
                    break;
                case CardDiscardedEntry cdisc when cdisc.Card != null:
                    RecordCardDiscarded(cdisc.Card);
                    break;
                case CardExhaustedEntry cex when cex.Card != null:
                    RecordCardExhausted(cex.Card);
                    break;
                case BlockGainedEntry bge:
                    RecordBlockGainedEntry(bge);
                    break;
                case DamageReceivedEntry dre:
                    // Remember the result ref so the combat-ending capture
                    // (RecordCombatEndingSuppressedDamage) knows this hit was
                    // recorded normally and won't synthesize a duplicate.
                    TryMarkDamageResultObserved(dre.Result);

                    if (dre.Receiver.IsPlayer)
                    {
                        RecordPlayerBlockedDamage(dre);
                    }

                    RecordEnemyDamage(dre);

                    if (dre.CardSource != null)
                    {
                        CoreMain.LogDebug($"  -> RecordDamage from '{dre.CardSource.Title}' intended={dre.Result.BlockedDamage + dre.Result.UnblockedDamage} canonicalHash={Canonical(dre.CardSource).GetHashCode()}");
                        RecordDamageFromCard(dre);
                    }
                    else if (!dre.Receiver.IsPlayer && TryRecordPoisonTickDamage(dre))
                    {
                        break;
                    }
                    else
                    {
                        if (!dre.Receiver.IsPlayer)
                        {
                            // Diagnostic: the game emitted a DamageReceivedEntry
                            // but didn't attribute it to a card. We silently dropped
                            // these before, but it caused ambiguity — hovering a
                            // card showed "Played 1" with no damage stats, and we
                            // couldn't tell if the game emitted null-source damage
                            // we dropped, or didn't emit anything at all. Always-on
                            // (not CUS_DEBUG-gated) because these should be rare
                            // and when they happen we want to know without the user
                            // having to reproduce under a debug flag.
                            var recvDesc = DescribeCreature(dre.Receiver);
                            var dealerDesc = DescribeCreature(dre.Dealer);
                            CoreMain.Logger.Info(
                                $"DamageReceivedEntry CardSource=null " +
                                $"receiver={recvDesc} dealer={dealerDesc} " +
                                $"blocked={dre.Result.BlockedDamage} unblocked={dre.Result.UnblockedDamage} " +
                                $"overkill={dre.Result.OverkillDamage} killed={dre.Result.WasTargetKilled}");
                        }
                    }
                    break;
                case PowerReceivedEntry pre when pre.Power != null:
                    RecordPowerReceived(pre);
                    break;
            }
        });
    }

    private static void RecordCardPlay(CardPlay cardPlay)
    {
        lock (_lock)
        {
            if (cardPlay?.Card == null) return;

            // Co-op: only the tracked local player's card plays are ours.
            if (!IsTrackedCard(cardPlay.Card)) return;
            // Defensive: if CombatSetUp never fired (unusual), allocate lazily.
            _pendingCombat ??= new PendingCombat();

            // Per-instance tracking: each physical card in the deck gets its
            // own aggregates bucket. First play assigns its instance id.
            var instanceId = GetOrAssignInstanceId(cardPlay.Card);

            var agg = GetOrCreateAggregate(_pendingCombat, instanceId);
            agg.Plays++;
            if (IsEtherealCard(cardPlay.Card))
                _pendingCombat.EtherealCardsPlayed++;
            RecordStrikeDummyStrikePlayedIfOwnedLocked(cardPlay.Card);
            RecordMiniatureCannonUpgradedAttackPlayedIfOwnedLocked(cardPlay.Card);
            if (IsReplayExtraPlay(cardPlay))
            {
                agg.TimesReplayExtraPlayed++;
                var reason = ConsumeReplayExtraPlaySourceLocked(cardPlay.Card);
                if (!reason.FromPlannedSource)
                    RecordReplayExtraPlayPlannedReason(agg, reason.ReasonId, reason.DisplayName, 1);
                RecordReplayExtraPlayReason(agg, reason.ReasonId, reason.DisplayName);
                if (TryFinishReplayAttackNoDamageLocked(cardPlay))
                    RecordReplayAttackNoDamageReason(agg, reason.ReasonId, reason.DisplayName);
            }
            // Energy spent = actual energy paid this play, accounting for any
            // cost modifiers (Mummified Hand / similar making a card free
            // still counts 0 here, which is what we want — the card DIDN'T
            // cost you energy this play). EnergyValue would be the listed
            // cost, but that's less useful for "how much does this card
            // actually cost me on average" analysis.
            agg.TotalEnergySpent += cardPlay.Resources.EnergySpent;
            agg.TotalStarsSpent += cardPlay.Resources.StarsSpent;
            RecordBrilliantScarfDiscountTaken(cardPlay);

            _pendingCombat.CombatEvents.Add(new CardEvent
            {
                T = Now(),
                Type = "card_played",
                CardId = instanceId,
                Target = cardPlay.Target?.Monster?.Id.ToString(),
                EnergySpent = cardPlay.Resources.EnergySpent,
                StarsSpent = cardPlay.Resources.StarsSpent,
            });
        }
    }

    internal static bool IsReplayExtraPlay(CardPlay cardPlay)
    {
        return cardPlay != null && cardPlay.PlayIndex > 0;
    }

    public static void NoteGlamReplayPlayCount(Glam glam, int baseCount, int finalCount)
    {
        if (glam?.Card == null) return;
        int extra = Math.Max(0, finalCount - baseCount);
        if (extra <= 0) return;

        lock (_lock)
        {
            EnqueueReplayExtraPlaySourceLocked(glam.Card, "enchantment:GLAM", "Glam", extra);
        }
    }

    public static void NoteReplayPlayCountModifiers(
        CardModel card,
        int baseCount,
        int finalCount,
        IEnumerable<AbstractModel>? modifyingModels)
    {
        if (card == null || modifyingModels == null) return;

        int remaining = Math.Max(0, finalCount - baseCount);
        if (remaining <= 0) return;

        lock (_lock)
        {
            foreach (var modifier in modifyingModels)
            {
                if (modifier == null || remaining <= 0) break;
                var source = ResolveReplayExtraPlaySource(modifier, remaining);
                if (source.Count <= 0) continue;

                int count = Math.Min(source.Count, remaining);
                EnqueueReplayExtraPlaySourceLocked(card, source.ReasonId, source.DisplayName, count);
                remaining -= count;
            }
        }
    }

    private static void EnqueueReplayExtraPlaySourceLocked(
        CardModel card,
        string reasonId,
        string displayName,
        int count)
    {
        if (card == null || count <= 0) return;

        var key = Canonical(card);
        if (_pendingReplayExtraPlaySeriesStarted.Remove(key))
            _pendingReplayExtraPlaySources.Remove(key);

        if (!_pendingReplayExtraPlaySources.TryGetValue(key, out var queue))
        {
            queue = new Queue<PendingReplayExtraPlaySource>();
            _pendingReplayExtraPlaySources[key] = queue;
        }

        queue.Enqueue(new PendingReplayExtraPlaySource
        {
            ReasonId = string.IsNullOrWhiteSpace(reasonId) ? "replay" : reasonId,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Replay" : displayName,
            Count = count,
        });

        _pendingCombat ??= new PendingCombat();
        var instanceId = GetOrAssignInstanceId(card);
        var agg = GetOrCreateAggregate(_pendingCombat, instanceId);
        RecordReplayExtraPlayPlannedReason(agg, reasonId, displayName, count);
    }

    private static (string ReasonId, string DisplayName, bool FromPlannedSource) ConsumeReplayExtraPlaySourceLocked(CardModel? card)
    {
        if (card == null) return ("replay", "Replay", false);

        var key = Canonical(card);
        if (!_pendingReplayExtraPlaySources.TryGetValue(key, out var queue))
            return ("replay", "Replay", false);

        while (queue.Count > 0)
        {
            var source = queue.Peek();
            if (source.Count <= 0)
            {
                queue.Dequeue();
                continue;
            }

            source.Count--;
            var result = (source.ReasonId, source.DisplayName, true);
            if (source.Count <= 0)
                queue.Dequeue();
            if (queue.Count == 0)
            {
                _pendingReplayExtraPlaySources.Remove(key);
                _pendingReplayExtraPlaySeriesStarted.Remove(key);
            }
            return result;
        }

        _pendingReplayExtraPlaySources.Remove(key);
        _pendingReplayExtraPlaySeriesStarted.Remove(key);
        return ("replay", "Replay", false);
    }

    private static PendingReplayExtraPlaySource ResolveReplayExtraPlaySource(AbstractModel modifier, int remaining)
    {
        if (modifier is PowerModel power)
        {
            var effectId = power.Id.ToString();
            var count = modifier.GetType().Name == "TagTeamPower" && power.Amount > 0
                ? Math.Min(power.Amount, remaining)
                : 1;
            return new PendingReplayExtraPlaySource
            {
                ReasonId = $"power:{effectId}",
                DisplayName = GetPowerDisplayName(power),
                Count = count,
            };
        }

        if (modifier is EnchantmentModel enchantment)
        {
            var name = enchantment.GetType().Name;
            return new PendingReplayExtraPlaySource
            {
                ReasonId = $"enchantment:{name}",
                DisplayName = GetReadableTypeName(name),
                Count = 1,
            };
        }

        var typeName = modifier.GetType().Name;
        return new PendingReplayExtraPlaySource
        {
            ReasonId = $"modifier:{modifier.GetType().FullName}",
            DisplayName = GetReadableTypeName(typeName),
            Count = 1,
        };
    }

    private static void RecordReplayExtraPlayReason(CardAggregate agg, string reasonId, string displayName)
    {
        RecordReplayReason(agg.ReplayExtraPlayReasons, reasonId, displayName, 1);
    }

    private static void RecordReplayExtraPlayPlannedReason(CardAggregate agg, string reasonId, string displayName, int count)
    {
        if (count <= 0) return;
        agg.TimesReplayExtraPlanned += count;
        RecordReplayReason(agg.ReplayExtraPlayPlannedReasons, reasonId, displayName, count);
    }

    private static void RecordReplayAttackNoDamageReason(CardAggregate agg, string reasonId, string displayName)
    {
        agg.TimesReplayAttackNoDamage++;
        RecordReplayReason(agg.ReplayAttackNoDamageReasons, reasonId, displayName, 1);
    }

    private static void RecordReplayReason(
        Dictionary<string, ReplayExtraPlayReasonAggregate> reasons,
        string reasonId,
        string displayName,
        int count)
    {
        if (count <= 0) return;
        reasonId = string.IsNullOrWhiteSpace(reasonId) ? "replay" : reasonId;
        displayName = string.IsNullOrWhiteSpace(displayName) ? "Replay" : displayName;

        if (!reasons.TryGetValue(reasonId, out var reason))
        {
            reason = new ReplayExtraPlayReasonAggregate
            {
                ReasonId = reasonId,
                DisplayName = displayName,
            };
            reasons[reasonId] = reason;
        }

        reason.Count += count;
        if (string.IsNullOrWhiteSpace(reason.DisplayName) && !string.IsNullOrWhiteSpace(displayName))
            reason.DisplayName = displayName;
    }

    private static string GetReadableTypeName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return "Replay";
        var trimmed = typeName;
        if (trimmed.EndsWith("Power", StringComparison.Ordinal))
            trimmed = trimmed[..^"Power".Length];
        if (trimmed.EndsWith("Enchantment", StringComparison.Ordinal))
            trimmed = trimmed[..^"Enchantment".Length];

        return string.Concat(trimmed.Select((ch, index) =>
            index > 0 && char.IsUpper(ch) && !char.IsWhiteSpace(trimmed[index - 1])
                ? " " + ch
                : ch.ToString()));
    }

    private static void NoteCardPlayStarted(CardPlay cardPlay)
    {
        lock (_lock)
        {
            // Co-op: don't adopt a partner's play as the resolving play, or
            // downstream energy/draw/power attribution would credit our cards.
            if (!IsTrackedCard(cardPlay?.Card)) return;
            _currentPlayerCardPlay = cardPlay;
            if (cardPlay?.Card != null)
            {
                var key = Canonical(cardPlay.Card);
                if (cardPlay.PlayIndex == 0 && _pendingReplayExtraPlaySources.ContainsKey(key))
                    _pendingReplayExtraPlaySeriesStarted[key] = true;

                if (IsReplayExtraPlay(cardPlay) && cardPlay.Card.Type == CardType.Attack)
                    _pendingReplayAttackOutcomes[cardPlay] = new PendingReplayAttackOutcome();
            }
            _recentCompletedPlayerCardPlay = null;
            _recentCompletedPlayerCardPlayHistoryCount = 0;
            _pendingDrawSourceCard = null;
            _pendingDrawAttempts.Clear();
            _pendingEffectSourceCard = null;
            _pendingEffectSourceHistoryCount = 0;
        }
    }

    private static void NoteCardPlayFinished(CardPlay cardPlay)
    {
        lock (_lock)
        {
            if (_currentPlayerCardPlay?.Card != null
                && cardPlay.Card != null
                && ReferenceEquals(Canonical(_currentPlayerCardPlay.Card), Canonical(cardPlay.Card)))
            {
                _currentPlayerCardPlay = null;
            }

            if (cardPlay?.Card != null && cardPlay.IsLastInSeries)
            {
                var key = Canonical(cardPlay.Card);
                _pendingReplayExtraPlaySources.Remove(key);
                _pendingReplayExtraPlaySeriesStarted.Remove(key);
            }

            _recentCompletedPlayerCardPlay = cardPlay;
            _recentCompletedPlayerCardPlayHistoryCount = CombatManager.Instance?.History?.Entries?.Count() ?? 0;
        }
    }

    /// <summary>
    /// Record energy added to the player's pool while a card is currently
    /// resolving. Called from <see cref="Patches.PlayerGainEnergyPatch"/>,
    /// which patches <c>PlayerCombatState.GainEnergy</c> and forwards the
    /// ACTUAL post-clamp delta rather than the requested amount.
    ///
    /// Attribution rule: only count gains that happen during a live
    /// CardPlayStartedEntry → CardPlayFinishedEntry window, and only if the
    /// resolving card's owner matches the PlayerCombatState being modified.
    /// This keeps relic / power / start-of-turn gains out of the card stat.
    /// </summary>
    public static void RecordEnergyGained(MegaCrit.Sts2.Core.Entities.Players.PlayerCombatState combatState, int amount)
    {
        if (amount <= 0) return;

        lock (_lock)
        {
            try
            {
                var causingPlay = FindCurrentlyResolvingCardPlay();
                if (causingPlay?.Card == null) return;

                var sourceCard = causingPlay.Card;
                var targetPlayer = combatState._player;
                if (targetPlayer != null && sourceCard.Owner != null
                    && !ReferenceEquals(sourceCard.Owner, targetPlayer))
                    return;

                _pendingCombat ??= new PendingCombat();
                var instanceId = GetOrAssignInstanceId(sourceCard);
                var agg = GetOrCreateAggregate(_pendingCombat, instanceId);
                agg.TotalEnergyGenerated += amount;

                _pendingCombat.CombatEvents.Add(new CardEvent
                {
                    T = Now(),
                    Type = "energy_gained",
                    CardId = instanceId,
                    EnergyGained = amount,
                });
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordEnergyGained failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record stars added to the player's pool while a card is currently
    /// resolving. Mirrors <see cref="RecordEnergyGained"/> but targets
    /// Regent's separate star resource.
    /// </summary>
    public static void RecordStarsGained(MegaCrit.Sts2.Core.Entities.Players.PlayerCombatState combatState, int amount)
    {
        if (amount <= 0) return;

        lock (_lock)
        {
            try
            {
                var causingPlay = FindCurrentlyResolvingCardPlay();
                if (causingPlay?.Card == null) return;

                var sourceCard = causingPlay.Card;
                var targetPlayer = combatState._player;
                if (targetPlayer != null && sourceCard.Owner != null
                    && !ReferenceEquals(sourceCard.Owner, targetPlayer))
                    return;

                _pendingCombat ??= new PendingCombat();
                var instanceId = GetOrAssignInstanceId(sourceCard);
                var agg = GetOrCreateAggregate(_pendingCombat, instanceId);
                agg.TotalStarsGenerated += amount;

                _pendingCombat.CombatEvents.Add(new CardEvent
                {
                    T = Now(),
                    Type = "stars_gained",
                    CardId = instanceId,
                    StarsGained = amount,
                });
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordStarsGained failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record forge added by a card. Sourced directly from
    /// <see cref="Patches.HookAfterForgePatch"/>, which sees the actual
    /// forge amount passed through the game's Forge command path.
    /// </summary>
    public static void RecordForgeGranted(decimal amount, Player? forger, AbstractModel? source)
    {
        if (amount <= 0m) return;

        lock (_lock)
        {
            try
            {
                if (source is not CardModel sourceCard) return;
                if (forger != null && sourceCard.Owner != null
                    && !ReferenceEquals(sourceCard.Owner, forger))
                    return;

                _pendingCombat ??= new PendingCombat();
                var instanceId = GetOrAssignInstanceId(sourceCard);
                var agg = GetOrCreateAggregate(_pendingCombat, instanceId);
                agg.TotalForgeGenerated += amount;

                _pendingCombat.CombatEvents.Add(new CardEvent
                {
                    T = Now(),
                    Type = "forge_gained",
                    CardId = instanceId,
                    ForgeGained = amount,
                    Floor = RunManager.Instance?.State?.TotalFloor,
                });
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordForgeGranted failed: {e.Message}");
            }
        }
    }

    // -------- Relic stat recording --------

    private const string BagOfMarblesRelicId = "RELIC.BAG_OF_MARBLES";
    private const string RedMaskRelicId = "RELIC.RED_MASK";
    private const string PocketwatchRelicId = "RELIC.POCKETWATCH";
    private const string OrichalcumRelicId = "RELIC.ORICHALCUM";
    private const string PermafrostRelicId = "RELIC.PERMAFROST";
    private const string TuningForkRelicId = "RELIC.TUNING_FORK";
    private const string AnchorRelicId = "RELIC.ANCHOR";
    private const string TheAbacusRelicId = "RELIC.THE_ABACUS";
    private const string LetterOpenerRelicId = "RELIC.LETTER_OPENER";
    private const int LetterOpenerDamagePerTarget = 5;
    private const string PenNibRelicId = "RELIC.PEN_NIB";
    private const string AkabekoRelicId = "RELIC.AKABEKO";
    private const string BookRepairKnifeRelicId = "RELIC.BOOK_REPAIR_KNIFE";
    private const string EternalFeatherRelicId = "RELIC.ETERNAL_FEATHER";
    private const string BoneFluteRelicId = "RELIC.BONE_FLUTE";
    private const string HealingLostFullHpReasonId = "full_hp";
    private const string HealingLostOtherReasonId = "other";
    private const string HappyFlowerRelicId = "RELIC.HAPPY_FLOWER";
    private const string BoomingConchRelicId = "RELIC.BOOMING_CONCH";
    private const string GremlinHornRelicId = "RELIC.GREMLIN_HORN";
    private const string NunchakuRelicId = "RELIC.NUNCHAKU";
    private const string CandelabraRelicId = "RELIC.CANDELABRA";
    private const string PendulumRelicId = "RELIC.PENDULUM";
    private const string ParryingShieldRelicId = "RELIC.PARRYING_SHIELD";
    private const string FestivePopperRelicId = "RELIC.FESTIVE_POPPER";
    private const string MercuryHourglassRelicId = "RELIC.MERCURY_HOURGLASS";
    private const string MrStrugglesRelicId = "RELIC.MR_STRUGGLES";
    private const string BronzeScalesRelicId = "RELIC.BRONZE_SCALES";
    private const string HornCleatRelicId = "RELIC.HORN_CLEAT";
    private const string PrismaticGemRelicId = "RELIC.PRISMATIC_GEM";
    private const string BloodSoakedRoseRelicId = "RELIC.BLOOD_SOAKED_ROSE";
    private const string CloakClaspRelicId = "RELIC.CLOAK_CLASP";
    private const string ReptileTrinketRelicId = "RELIC.REPTILE_TRINKET";
    private const string GorgetRelicId = "RELIC.GORGET";
    private const string StoneCrackerRelicId = "RELIC.STONE_CRACKER";
    private const string WhetstoneRelicId = "RELIC.WHETSTONE";
    private const string WarPaintRelicId = "RELIC.WAR_PAINT";
    private const string MealTicketRelicId = "RELIC.MEAL_TICKET";
    private const string BurningBloodRelicId = "RELIC.BURNING_BLOOD";
    private const string BloodVialRelicId = "RELIC.BLOOD_VIAL";
    private const string PantographRelicId = "RELIC.PANTOGRAPH";
    private const string PlanisphereRelicId = "RELIC.PLANISPHERE";
    private const string LeesWaffleRelicId = "RELIC.LEES_WAFFLE";
    private const string StrawberryRelicId = "RELIC.STRAWBERRY";
    private const string PearRelicId = "RELIC.PEAR";
    private const string ChosenCheeseRelicId = "RELIC.CHOSEN_CHEESE";
    private const string DarkstonePeriaptRelicId = "RELIC.DARKSTONE_PERIAPT";
    private const string LeafyPoulticeRelicId = "RELIC.LEAFY_POULTICE";
    private const string RegalPillowRelicId = "RELIC.REGAL_PILLOW";
    private const string WhiteBeastStatueRelicId = "RELIC.WHITE_BEAST_STATUE";
    private const string ShovelRelicId = "RELIC.SHOVEL";
    private const string BoundPhylacteryRelicId = "RELIC.BOUND_PHYLACTERY";
    private const string PhylacteryUnboundRelicId = "RELIC.PHYLACTERY_UNBOUND";
    private const string ToolboxRelicId = "RELIC.TOOLBOX";
    private const string PaelsWingRelicId = "RELIC.PAELS_WING";
    private const string PaelsEyeRelicId = "RELIC.PAELS_EYE";
    private const string StrikeDummyRelicId = "RELIC.STRIKE_DUMMY";
    private const string MiniatureCannonRelicId = "RELIC.MINIATURE_CANNON";
    private const string BookmarkRelicId = "RELIC.BOOKMARK";
    private const string BrilliantScarfRelicId = "RELIC.BRILLIANT_SCARF";
    private const string JuzuBraceletRelicId = "RELIC.JUZU_BRACELET";
    private const string HeftyTabletRelicId = "RELIC.HEFTY_TABLET";
    private const string VambraceRelicId = "RELIC.VAMBRACE";
    private const string GamblingChipRelicId = "RELIC.GAMBLING_CHIP";
    private const string CentennialPuzzleRelicId = "RELIC.CENTENNIAL_PUZZLE";
    private const string PrecariousShearsRelicId = "RELIC.PRECARIOUS_SHEARS";
    private const string SandCastleRelicId = "RELIC.SAND_CASTLE";

    /// <summary>
    /// Record a Bag of Marbles combat-start Vulnerable application.
    /// <paramref name="enemyCount"/> is the number of live enemies that
    /// received 1 Vulnerable stack. Called from
    /// <see cref="Patches.BagOfMarblesBeforeSideTurnStartPatch"/>.
    /// </summary>
    public static void RecordBagOfMarblesApplication(int enemyCount)
    {
        if (enemyCount <= 0) return;

        lock (_lock)
        {
            try
            {
                _pendingCombat ??= new PendingCombat();
                if (!_pendingCombat.RelicAggregates.TryGetValue(BagOfMarblesRelicId, out var agg))
                {
                    agg = new RelicAggregate();
                    _pendingCombat.RelicAggregates[BagOfMarblesRelicId] = agg;
                }
                agg.EnemiesAffected += enemyCount;
                agg.VulnerableApplied += enemyCount;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordBagOfMarblesApplication failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record a Red Mask combat-start Weak application.
    /// <paramref name="enemyCount"/> is the number of live enemies that
    /// received 1 Weak stack. Called from
    /// <see cref="Patches.RedMaskBeforeSideTurnStartPatch"/>.
    /// </summary>
    public static void RecordRedMaskApplication(int enemyCount)
    {
        if (enemyCount <= 0) return;

        lock (_lock)
        {
            try
            {
                _pendingCombat ??= new PendingCombat();
                if (!_pendingCombat.RelicAggregates.TryGetValue(RedMaskRelicId, out var agg))
                {
                    agg = new RelicAggregate();
                    _pendingCombat.RelicAggregates[RedMaskRelicId] = agg;
                }
                agg.EnemiesAffected += enemyCount;
                agg.WeakApplied += enemyCount;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordRedMaskApplication failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Arm the one-shot flag that attributes the next player block gain to
    /// Orichalcum. Called from <see cref="Patches.OrichalcumBeforeTurnEndPatch"/>
    /// when Orichalcum's end-of-turn hook fires on the player's side.
    /// </summary>
    public static void ArmOrichalcumBlockAttribution()
    {
        lock (_lock)
        {
            _pendingCombat ??= new PendingCombat();
            // One-shot; block gain resolves async many history entries later.
            _pendingCombat.Windows.Arm(OrichalcumRelicId, AttributionEventKind.PlayerBlockGain,
                CurrentHistoryCountLocked(), maxHistoryAdvance: -1);
        }
    }

    /// <summary>
    /// Clear the Orichalcum attribution flag without recording. Used as a
    /// safety reset if Orichalcum's condition was not met.
    /// </summary>
    public static void DisarmOrichalcumBlockAttribution()
    {
        lock (_lock)
        {
            _pendingCombat?.Windows.Disarm(OrichalcumRelicId, AttributionEventKind.PlayerBlockGain);
        }
    }

    /// <summary>
    /// Record Orichalcum's owner-specific end-turn check being blocked by the
    /// player already having block. Called from the relic's
    /// <c>BeforeSideTurnEndVeryEarly</c> method, where the game performs this
    /// exact condition check before arming <c>ShouldTrigger</c>.
    /// </summary>
    public static void RecordOrichalcumBlockedTrigger()
    {
        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(OrichalcumRelicId);
                agg.BlockedTriggers += 1;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordOrichalcumBlockedTrigger failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record Permafrost's first-Power trigger for this combat and arm the
    /// observed block gain. The actual block amount is observed by
    /// <see cref="Patches.HookAfterBlockGainedPatch"/>.
    /// </summary>
    public static void RecordPermafrostActivationAndArmBlockAttribution()
    {
        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(PermafrostRelicId);
                agg.Activations += 1;
                _pendingCombat!.Windows.Arm(PermafrostRelicId, AttributionEventKind.PlayerBlockGain,
                    CurrentHistoryCountLocked(), maxHistoryAdvance: -1);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordPermafrostActivationAndArmBlockAttribution failed: {e.Message}");
            }
        }
    }

    public static void DisarmPermafrostBlockAttribution()
    {
        lock (_lock)
        {
            _pendingCombat?.Windows.Disarm(PermafrostRelicId, AttributionEventKind.PlayerBlockGain);
        }
    }

    /// <summary>
    /// Record Tuning Fork's every-N-skills trigger and arm observed block
    /// attribution. The actual block amount is observed by
    /// <see cref="Patches.HookAfterBlockGainedPatch"/>.
    /// </summary>
    public static void RecordTuningForkActivationAndArmBlockAttribution()
    {
        lock (_lock)
        {
            try
            {
                _pendingCombat ??= new PendingCombat();
                var agg = GetOrCreateRelicAggregateLocked(TuningForkRelicId);
                agg.Activations += 1;
                _pendingCombat.Windows.Arm(
                    TuningForkRelicId,
                    AttributionEventKind.PlayerBlockGain,
                    CurrentHistoryCountLocked(),
                    maxHistoryAdvance: -1);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordTuningForkActivationAndArmBlockAttribution failed: {e.Message}");
            }
        }
    }

    public static void DisarmTuningForkBlockAttribution()
    {
        lock (_lock)
        {
            _pendingCombat?.Windows.Disarm(TuningForkRelicId, AttributionEventKind.PlayerBlockGain);
        }
    }

    /// <summary>
    /// Arm a one-shot attribution window for Anchor's combat-start block.
    /// The actual block amount is observed by <see cref="Patches.HookAfterBlockGainedPatch"/>.
    /// </summary>
    public static void ArmAnchorBlockAttribution()
    {
        lock (_lock)
        {
            _pendingCombat ??= new PendingCombat();
            _pendingCombat.Windows.Arm(AnchorRelicId, AttributionEventKind.PlayerBlockGain,
                CurrentHistoryCountLocked(), maxHistoryAdvance: -1);
        }
    }

    public static void DisarmAnchorBlockAttribution()
    {
        // No-op. Anchor's attribution now lives entirely in the per-combat
        // registry (see ArmAnchorBlockAttribution); the window closes on
        // consumption or the combat boundary. Routing this to
        // Windows.Disarm(...) — an explicit early close to prevent a late
        // mis-claim — changes live window-close timing, so it's a deferred,
        // live-verified follow-up (#257). Kept wired so that follow-up has a
        // seam.
    }

    /// <summary>
    /// Record Reptile Trinket's owner-specific potion-use activation. Called
    /// from <see cref="Patches.ReptileTrinketAfterPotionUsedPatch"/> after
    /// matching the game's owner/combat checks and reading the same Strength
    /// dynamic var that the relic applies.
    /// </summary>
    public static void RecordReptileTrinketActivation(decimal strengthAdded)
    {
        if (strengthAdded <= 0m) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(ReptileTrinketRelicId);
                agg.Activations += 1;
                agg.StrengthAdded += strengthAdded;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordReptileTrinketActivation failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record Gorget's owner-specific combat-room activation. Called from
    /// <see cref="Patches.GorgetAfterRoomEnteredPatch"/> after matching the
    /// game's CombatRoom check and reading the same Plating dynamic var that
    /// the relic applies.
    /// </summary>
    public static void RecordGorgetActivation(decimal platingAdded)
    {
        if (platingAdded <= 0m) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(GorgetRelicId);
                agg.Activations += 1;
                agg.PlatingAdded += platingAdded;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordGorgetActivation failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record Stone Cracker's owner-specific combat-room activation and the
    /// number of upgradeable deck cards selected for upgrade. The game filters
    /// the owner's deck by <c>IsUpgradable</c>, shuffles, then takes the relic's
    /// Cards dynamic var count; callers pass that selected count.
    /// </summary>
    public static void RecordStoneCrackerActivation(int cardsUpgraded)
    {
        if (cardsUpgraded < 0) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(StoneCrackerRelicId);
                agg.Activations += 1;
                agg.CardsUpgraded += cardsUpgraded;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordStoneCrackerActivation failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Arm the one-shot flag that attributes the next player block gain to
    /// The Abacus. Called from <see cref="Patches.TheAbacusAfterShufflePatch"/>
    /// when The Abacus's <c>AfterShuffle</c> fires.
    /// </summary>
    public static void ArmTheAbacusBlockAttribution()
    {
        lock (_lock)
        {
            _pendingCombat ??= new PendingCombat();
            _pendingCombat.Windows.Arm(TheAbacusRelicId, AttributionEventKind.PlayerBlockGain,
                CurrentHistoryCountLocked(), maxHistoryAdvance: -1);
        }
    }

    /// <summary>
    /// Record Letter Opener's every-N-skills activation. The game does not
    /// source its damage entries to the relic, so this records attempted damage
    /// from the owner callback and the live hittable enemy count at trigger time.
    /// </summary>
    public static void RecordLetterOpenerBeforeCardPlayed(
        CardPlay cardPlay,
        int skillsPlayedIncludingThis,
        int activationThreshold)
    {
        if (cardPlay?.Card == null) return;
        if (cardPlay.Card.Type != CardType.Skill) return;
        if (activationThreshold <= 0) return;
        if (skillsPlayedIncludingThis <= 0 || skillsPlayedIncludingThis % activationThreshold != 0) return;

        lock (_lock)
        {
            try
            {
                int targetCount = CountLetterOpenerTargets(cardPlay.Card.CombatState);
                if (targetCount <= 0) return;

                var agg = GetOrCreateRelicAggregateLocked(LetterOpenerRelicId);
                agg.Activations += 1;
                agg.TotalTargets += targetCount;
                agg.TotalDamageAttempted += LetterOpenerDamagePerTarget * targetCount;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordLetterOpenerBeforeCardPlayed failed: {e.Message}");
            }
        }
    }

    private static int CountLetterOpenerTargets(ICombatState? combatState)
    {
        if (combatState is not CombatState concreteCombatState) return 0;

        try
        {
            return concreteCombatState.HittableEnemies.Count(creature => creature.IsAlive && creature.IsHittable);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Record the raw per-hit damage amount doubled by Pen Nib. This is the
    /// extra base damage the relic contributed, intentionally before
    /// downstream hook multipliers such as Lethality or Vulnerable.
    /// </summary>
    public static void RecordPenNibBaseDamageAdded(decimal baseDamageAdded)
    {
        if (baseDamageAdded <= 0m) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(PenNibRelicId);
                AddPenNibBaseDamageAdded(agg, baseDamageAdded);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordPenNibBaseDamageAdded failed: {e.Message}");
            }
        }
    }

    internal static void RecordPenNibBaseDamageAddedForTest(RelicAggregate agg, decimal baseDamageAdded)
        => AddPenNibBaseDamageAdded(agg, baseDamageAdded);

    private static void AddPenNibBaseDamageAdded(RelicAggregate agg, decimal baseDamageAdded)
    {
        var added = (int)decimal.Truncate(baseDamageAdded);
        if (added <= 0) return;
        agg.TotalDamageAttempted += added;
    }

    /// <summary>
    /// Record the extra integer block Vambrace contributed to one modified
    /// block packet. Because Vambrace's multiplier is 2x, the no-Vambrace
    /// final amount would be half the observed final amount.
    /// </summary>
    public static void RecordVambraceExtraBlockGained(decimal modifiedAmount)
    {
        if (modifiedAmount <= 0m) return;

        lock (_lock)
        {
            try
            {
                var added = ComputeVambraceExtraBlock(modifiedAmount);
                if (added <= 0) return;

                var agg = GetOrCreateRelicAggregateLocked(VambraceRelicId);
                agg.AdditionalBlockGained += added;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordVambraceExtraBlockGained failed: {e.Message}");
            }
        }
    }

    public static void RecordVambraceActivation()
    {
        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(VambraceRelicId);
                agg.Activations += 1;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordVambraceActivation failed: {e.Message}");
            }
        }
    }

    internal static void RecordVambraceExtraBlockGainedForTest(RelicAggregate agg, decimal modifiedAmount)
    {
        var added = ComputeVambraceExtraBlock(modifiedAmount);
        if (added > 0) agg.AdditionalBlockGained += added;
    }

    internal static int ComputeVambraceExtraBlockForTest(decimal modifiedAmount)
        => ComputeVambraceExtraBlock(modifiedAmount);

    private static int ComputeVambraceExtraBlock(decimal modifiedAmount)
    {
        if (modifiedAmount <= 0m) return 0;

        var withVambrace = TruncateBlockAmount(modifiedAmount);
        var withoutVambrace = TruncateBlockAmount(modifiedAmount / 2m);
        return Math.Max(withVambrace - withoutVambrace, 0);
    }

    private static int TruncateBlockAmount(decimal amount)
    {
        if (amount <= 0m) return 0;
        if (amount >= int.MaxValue) return int.MaxValue;
        return (int)decimal.Truncate(amount);
    }

    /// <summary>
    /// Record a confirmed Bone Flute trigger and arm the next player block
    /// gain as its observed payload. Called from
    /// <see cref="Patches.BoneFluteAfterAttackPatch"/> only after the relic's
    /// owner-specific Osty attack condition is satisfied. (The block-gain
    /// arming this method's name refers to is currently non-functional — see
    /// <see cref="DisarmBoneFluteBlockAttribution"/> — so only the trigger
    /// count is recorded today.)
    /// </summary>
    public static void RecordBoneFluteTriggerAndArmBlockAttribution()
    {
        lock (_lock)
        {
            try
            {
                _pendingCombat ??= new PendingCombat();
                if (!_pendingCombat.RelicAggregates.TryGetValue(BoneFluteRelicId, out var agg))
                {
                    agg = new RelicAggregate();
                    _pendingCombat.RelicAggregates[BoneFluteRelicId] = agg;
                }

                agg.BoneFluteTriggers++;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordBoneFluteTriggerAndArmBlockAttribution failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// No-op safety reset kept wired for Bone Flute's async gain-block task.
    /// The block-gain consumer that once read the armed flag was already
    /// unreachable, so Bone Flute's per-block attribution is currently
    /// non-functional (a known bug: it never routed to the registry). Fixing
    /// it — arm a PlayerBlockGain window and credit AdditionalBlockGained — is
    /// a separate, live-verified change; this seam stays so it has a home.
    /// </summary>
    public static void DisarmBoneFluteBlockAttribution()
    {
    }

    /// <summary>
    /// Record additional cards drawn by Pocketwatch's turn-start bonus.
    /// <paramref name="cardsDrawn"/> is the number of extra cards drawn (normally 3).
    /// Called from <see cref="Patches.PocketwatchModifyHandDrawPatch"/>.
    /// </summary>
    public static void RecordPocketwatchDraw(int cardsDrawn)
    {
        if (cardsDrawn <= 0) return;

        lock (_lock)
        {
            try
            {
                _pendingCombat ??= new PendingCombat();
                if (!_pendingCombat.RelicAggregates.TryGetValue(PocketwatchRelicId, out var agg))
                {
                    agg = new RelicAggregate();
                    _pendingCombat.RelicAggregates[PocketwatchRelicId] = agg;
                }
                agg.AdditionalCardsDrawn += cardsDrawn;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordPocketwatchDraw failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Mark that White Beast Statue forced a potion reward. Called from the
    /// relic-owned ShouldForcePotionReward callback after the game confirms
    /// the relic returned true.
    /// </summary>
    public static void NoteWhiteBeastPotionRewardForced()
    {
        lock (_lock)
        {
            _pendingWhiteBeastPotionRewards++;
        }
    }

    /// <summary>
    /// Attach a pending White Beast force decision to the concrete potion
    /// reward object the game created from that decision.
    /// </summary>
    public static void NotePotionRewardCreated(PotionReward reward)
    {
        if (reward == null) return;

        lock (_lock)
        {
            if (_pendingWhiteBeastPotionRewards <= 0) return;

            _pendingWhiteBeastPotionRewards--;
            _whiteBeastPotionRewards.Add(reward);
        }
    }

    /// <summary>
    /// Record a White Beast potion only after the marked reward is actually
    /// selected successfully.
    /// </summary>
    public static void RecordWhiteBeastPotionRewardClaimed(PotionReward reward)
    {
        if (reward == null) return;

        lock (_lock)
        {
            try
            {
                if (!_whiteBeastPotionRewards.Remove(reward)) return;

                var potion = reward.ClaimedPotion ?? reward.Potion;
                var agg = GetOrCreateRelicAggregateForCurrentContextLocked(WhiteBeastStatueRelicId);
                agg.PotionsGained++;

                switch (potion?.Rarity)
                {
                    case PotionRarity.Common:
                        agg.CommonPotionsGained++;
                        break;
                    case PotionRarity.Uncommon:
                        agg.UncommonPotionsGained++;
                        break;
                    case PotionRarity.Rare:
                        agg.RarePotionsGained++;
                        break;
                }

                if (_pendingCombat == null)
                    SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordWhiteBeastPotionRewardClaimed failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record a White Beast potion reward skipped by the player. This only
    /// counts rewards previously marked from White Beast's force decision.
    /// </summary>
    public static void RecordWhiteBeastPotionRewardSkipped(PotionReward reward)
    {
        if (reward == null) return;

        lock (_lock)
        {
            try
            {
                if (!_whiteBeastPotionRewards.Remove(reward)) return;

                var agg = GetOrCreateRelicAggregateForCurrentContextLocked(WhiteBeastStatueRelicId);
                agg.PotionsSkipped++;

                if (_pendingCombat == null)
                    SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordWhiteBeastPotionRewardSkipped failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record relics actually added to the player by Shovel's Dig option.
    /// The patch observes the owner inventory after <c>DigRestSiteOption.OnSelect</c>
    /// succeeds, so rarity comes from the obtained relic instance.
    /// </summary>
    public static void RecordShovelRelicsAcquired(Player owner, IEnumerable<RelicModel> relics)
    {
        if (owner == null || relics == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(owner)) return;

                var acquired = relics.Where(r => r != null).ToList();
                if (acquired.Count == 0) return;

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(ShovelRelicId);
                foreach (var relic in acquired)
                    RecordShovelRelicAcquiredForTest(agg, relic.Rarity);

                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordShovelRelicsAcquired failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record a rest site where Shovel's Dig option was available but the
    /// local player left without selecting it.
    /// </summary>
    public static void RecordShovelCampfireNotDug(Player owner)
    {
        if (owner == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(owner)) return;

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(ShovelRelicId);
                RecordShovelCampfireNotDugForTest(agg);
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordShovelCampfireNotDug failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record Toolbox's owner-specific opening-hand trigger and arm the next
    /// choose-card screen as the actual offer payload to inspect.
    /// </summary>
    public static void RecordToolboxTrigger()
    {
        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(ToolboxRelicId);
                agg.Activations += 1;
                _pendingToolboxOfferScreens += 1;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordToolboxTrigger failed: {e.Message}");
            }
        }
    }

    public static bool RecordToolboxOffers(IReadOnlyList<CardModel> cards)
    {
        if (cards == null || cards.Count == 0) return false;

        lock (_lock)
        {
            try
            {
                if (_pendingToolboxOfferScreens <= 0) return false;
                _pendingToolboxOfferScreens -= 1;

                var agg = GetOrCreateRelicAggregateLocked(ToolboxRelicId);
                foreach (var card in cards)
                {
                    if (card == null) continue;
                    switch (card.Rarity)
                    {
                        case CardRarity.Uncommon:
                            agg.UncommonCardsOffered += 1;
                            break;
                        case CardRarity.Rare:
                            agg.RareCardsOffered += 1;
                            break;
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordToolboxOffers failed: {e.Message}");
                return false;
            }
        }
    }

    public static void RecordToolboxTaken(CardModel card)
    {
        if (card == null) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(ToolboxRelicId);
                switch (card.Rarity)
                {
                    case CardRarity.Uncommon:
                        agg.UncommonCardsTaken += 1;
                        break;
                    case CardRarity.Rare:
                        agg.RareCardsTaken += 1;
                        break;
                }
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordToolboxTaken failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record that Brilliant Scarf has reached its "next card discounted"
    /// threshold. The actual energy saved is filled in later by the cost
    /// modifier, because cost queries are noisy and should not count offers.
    /// </summary>
    public static void RecordBrilliantScarfDiscountOffered(CardPlay cardPlay, int cardsPlayedThisTurn, int threshold)
    {
        if (cardPlay?.Card?.Owner == null || cardPlay.IsAutoPlay) return;
        if (threshold <= 0 || cardsPlayedThisTurn != threshold - 1) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedCard(cardPlay.Card)) return;

                _pendingCombat ??= new PendingCombat();
                var player = cardPlay.Card.Owner;
                RecordBrilliantScarfCombatForPlayerLocked(player);

                int turnNumber = GetCardOwnerTurnNumber(cardPlay.Card);
                if (_pendingCombat.BrilliantScarfDiscountOffers.TryGetValue(player, out var existing)
                    && existing.TurnNumber == turnNumber)
                {
                    return;
                }

                _pendingCombat.BrilliantScarfDiscountOffers[player] = new PendingBrilliantScarfDiscount
                {
                    TurnNumber = turnNumber,
                };

                var agg = GetOrCreateRelicAggregateLocked(BrilliantScarfRelicId);
                agg.DiscountsOffered += 1;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordBrilliantScarfDiscountOffered failed: {e.Message}");
            }
        }
    }

    public static void RecordBrilliantScarfPotentialEnergySaving(CardModel card, decimal originalCost, decimal modifiedCost)
    {
        if (card?.Owner == null) return;
        if (originalCost <= modifiedCost) return;

        int energySaved = (int)Math.Floor(originalCost - modifiedCost);
        if (energySaved <= 0) return;

        lock (_lock)
        {
            try
            {
                if (_pendingCombat == null) return;
                if (!IsTrackedCard(card)) return;
                if (!_pendingCombat.BrilliantScarfDiscountOffers.TryGetValue(card.Owner, out var offer)) return;
                if (offer.TurnNumber != GetCardOwnerTurnNumber(card)) return;

                var saving = GetOrCreateBrilliantScarfCardSaving(offer, card);
                saving.EnergySaved = Math.Max(saving.EnergySaved, energySaved);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordBrilliantScarfPotentialEnergySaving failed: {e.Message}");
            }
        }
    }

    public static void RecordBrilliantScarfPotentialStarSaving(CardModel card, decimal originalCost, decimal modifiedCost)
    {
        if (card?.Owner == null) return;
        if (originalCost <= modifiedCost) return;

        int starsSaved = (int)Math.Floor(originalCost - modifiedCost);
        if (starsSaved <= 0) return;

        lock (_lock)
        {
            try
            {
                if (_pendingCombat == null) return;
                if (!IsTrackedCard(card)) return;
                if (!_pendingCombat.BrilliantScarfDiscountOffers.TryGetValue(card.Owner, out var offer)) return;
                if (offer.TurnNumber != GetCardOwnerTurnNumber(card)) return;

                var saving = GetOrCreateBrilliantScarfCardSaving(offer, card);
                saving.StarsSaved = Math.Max(saving.StarsSaved, starsSaved);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordBrilliantScarfPotentialStarSaving failed: {e.Message}");
            }
        }
    }

    public static void RecordBrilliantScarfDiscountTaken(CardPlay cardPlay)
    {
        if (cardPlay?.Card?.Owner == null || cardPlay.IsAutoPlay) return;

        lock (_lock)
        {
            try
            {
                if (_pendingCombat == null) return;
                if (!IsTrackedCard(cardPlay.Card)) return;

                var player = cardPlay.Card.Owner;
                if (!_pendingCombat.BrilliantScarfDiscountOffers.TryGetValue(player, out var offer)) return;

                _pendingCombat.BrilliantScarfDiscountOffers.Remove(player);
                if (offer.TurnNumber != GetCardOwnerTurnNumber(cardPlay.Card)) return;

                offer.SavingsByCard.TryGetValue(cardPlay.Card, out var saving);
                int energySaved = Math.Max(0, saving?.EnergySaved ?? 0);
                int starsSaved = Math.Max(0, saving?.StarsSaved ?? 0);
                var agg = GetOrCreateRelicAggregateLocked(BrilliantScarfRelicId);
                agg.DiscountsTaken += 1;
                agg.EnergySavedByDiscount += energySaved;
                AddDiscountedCardCost(
                    agg,
                    cardPlay.Resources.EnergySpent + energySaved,
                    cardPlay.Resources.StarsSpent + starsSaved);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordBrilliantScarfDiscountTaken failed: {e.Message}");
            }
        }
    }

    internal static void RecordBrilliantScarfDiscountForTest(
        RelicAggregate agg,
        int offers,
        int taken,
        int energySaved,
        int combats = 0)
    {
        if (agg == null) return;
        agg.DiscountCombats += Math.Max(0, combats);
        agg.DiscountsOffered += Math.Max(0, offers);
        agg.DiscountsTaken += Math.Max(0, taken);
        agg.EnergySavedByDiscount += Math.Max(0, energySaved);
    }

    internal static void RecordBrilliantScarfDiscountCostForTest(
        RelicAggregate agg,
        int energyCost,
        int starCost,
        int count = 1)
    {
        if (agg == null) return;
        AddDiscountedCardCost(agg, energyCost, starCost, count);
    }

    internal static string BrilliantScarfDiscountCostKeyForTest(int energyCost, int starCost)
    {
        return DiscountedCardCostKey(energyCost, starCost);
    }

    private static PendingBrilliantScarfCardSaving GetOrCreateBrilliantScarfCardSaving(
        PendingBrilliantScarfDiscount offer,
        CardModel card)
    {
        if (!offer.SavingsByCard.TryGetValue(card, out var saving))
        {
            saving = new PendingBrilliantScarfCardSaving();
            offer.SavingsByCard[card] = saving;
        }

        return saving;
    }

    private static void AddDiscountedCardCost(
        RelicAggregate agg,
        int energyCost,
        int starCost,
        int count = 1)
    {
        if (agg == null || count <= 0) return;
        agg.DiscountedCardCosts ??= new Dictionary<string, DiscountedCardCostAggregate>();

        energyCost = Math.Max(0, energyCost);
        starCost = Math.Max(0, starCost);
        var key = DiscountedCardCostKey(energyCost, starCost);
        if (!agg.DiscountedCardCosts.TryGetValue(key, out var bucket))
        {
            bucket = new DiscountedCardCostAggregate
            {
                EnergyCost = energyCost,
                StarCost = starCost,
            };
            agg.DiscountedCardCosts[key] = bucket;
        }
        else
        {
            bucket.EnergyCost = energyCost;
            bucket.StarCost = starCost;
        }

        bucket.Count += count;
    }

    private static string DiscountedCardCostKey(int energyCost, int starCost)
    {
        return $"energy:{Math.Max(0, energyCost)}|stars:{Math.Max(0, starCost)}";
    }

    /// <summary>
    /// Record a map ? site entered while Juzu Bracelet is currently held.
    /// Uses the original map point type, before the game resolves it into a
    /// concrete room type, so event-combat transitions cannot double count.
    /// </summary>
    public static void RecordJuzuQuestionSiteEntered(MapPointType pointType, bool saveGame)
    {
        if (!saveGame || pointType != MapPointType.Unknown) return;

        lock (_lock)
        {
            try
            {
                var player = GetTrackedRunPlayerLocked();
                if (player == null || !PlayerHasJuzuBracelet(player)) return;

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(JuzuBraceletRelicId);
                RecordJuzuQuestionSiteEnteredForTest(agg);
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordJuzuQuestionSiteEntered failed: {e.Message}");
            }
        }
    }

    internal static void RecordJuzuQuestionSiteEnteredForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null || count <= 0) return;
        agg.QuestionMarkSitesEntered += count;
    }

    public static void ArmHeftyTabletChoice(Player owner)
    {
        if (owner == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(owner)) return;
                _pendingHeftyTabletChoicePlayers.Add(owner);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"ArmHeftyTabletChoice failed: {e.Message}");
            }
        }
    }

    public static void DisarmHeftyTabletChoice(Player owner)
    {
        if (owner == null) return;

        lock (_lock)
        {
            try
            {
                ConsumePendingGremlinHornAttribution(_pendingHeftyTabletChoicePlayers, owner);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"DisarmHeftyTabletChoice failed: {e.Message}");
            }
        }
    }

    public static bool TryConsumeHeftyTabletChoiceScreen(Player player, IReadOnlyList<CardModel> cards, bool canSkip)
    {
        if (player == null || cards == null || cards.Count == 0 || !canSkip) return false;
        if (cards.Any(card => card == null || card.Rarity != CardRarity.Rare)) return false;

        lock (_lock)
        {
            try
            {
                return ConsumePendingGremlinHornAttribution(_pendingHeftyTabletChoicePlayers, player);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"TryConsumeHeftyTabletChoiceScreen failed: {e.Message}");
                return false;
            }
        }
    }

    public static void RecordHeftyTabletChoice(CardModel? selectedCard)
    {
        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateCurrentRunRelicAggregateLocked(HeftyTabletRelicId);
                if (selectedCard == null)
                {
                    agg.CardChoicesSkipped += 1;
                }
                else
                {
                    AddRelicCardGranted(
                        agg.CardsGranted,
                        selectedCard.Id.ToString(),
                        GetCardDisplayName(selectedCard),
                        1);
                }

                RefreshCurrentRunMetadataLocked();
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordHeftyTabletChoice failed: {e.Message}");
            }
        }
    }

    internal static void RecordHeftyTabletChoiceForTest(RelicAggregate agg, string? cardId, string? displayName)
    {
        if (agg == null) return;
        if (string.IsNullOrWhiteSpace(cardId))
        {
            agg.CardChoicesSkipped += 1;
            return;
        }

        AddRelicCardGranted(agg.CardsGranted, cardId, displayName ?? "", 1);
    }

    /// <summary>
    /// Record Gambling Chip's combat-start prompt and keep a narrow discard
    /// attribution window open until its async prompt/draw sequence completes.
    /// </summary>
    public static void ArmGamblingChipDiscardAttribution(Player player)
    {
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(player)) return;

                _pendingCombat ??= new PendingCombat();
                var agg = GetOrCreateRelicAggregateLocked(GamblingChipRelicId);
                agg.Activations += 1;
                _pendingCombat.GamblingChipDiscardAttributionPlayers.Add(player);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"ArmGamblingChipDiscardAttribution failed: {e.Message}");
            }
        }
    }

    public static void DisarmGamblingChipDiscardAttribution(Player player)
    {
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                _pendingCombat?.GamblingChipDiscardAttributionPlayers.Remove(player);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"DisarmGamblingChipDiscardAttribution failed: {e.Message}");
            }
        }
    }

    internal static void RecordGamblingChipDiscardForTest(RelicAggregate agg, int activations, int cardsDiscarded)
    {
        if (agg == null) return;
        agg.Activations += Math.Max(0, activations);
        agg.CardsDiscarded += Math.Max(0, cardsDiscarded);
    }

    private static int GetCardOwnerTurnNumber(CardModel card)
    {
        try
        {
            return card.Owner?.PlayerCombatState?.TurnNumber ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Mark one card reward where Pael's Wing added its Sacrifice alternative.
    /// The reward may be generated/refreshed more than once, so the rarity
    /// snapshot is refreshed without counting an opportunity until resolution.
    /// </summary>
    public static void NotePaelSacrificeOffered(CardReward reward)
    {
        if (reward == null) return;

        lock (_lock)
        {
            try
            {
                _paelSacrificeRewards[reward] = PendingPaelSacrificeReward.FromCards(reward.Cards);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"NotePaelSacrificeOffered failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record Pael's Wing's Sacrifice option after the player's selected
    /// reward alternative invokes the relic-owned delegate.
    /// </summary>
    public static void RecordPaelSacrificeMade(CardReward reward)
    {
        if (reward == null) return;

        lock (_lock)
        {
            try
            {
                if (!_paelSacrificeRewards.Remove(reward, out var pending))
                    pending = PendingPaelSacrificeReward.FromCards(reward.Cards);

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(PaelsWingRelicId);
                agg.SacrificesMade += 1;
                agg.CommonCardsConsumed += pending.CommonCards;
                agg.UncommonCardsConsumed += pending.UncommonCards;
                agg.RareCardsConsumed += pending.RareCards;
                RefreshCurrentRunMetadataLocked();
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordPaelSacrificeMade failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record a Pael's Wing sacrifice opportunity that resolved without
    /// selecting Sacrifice, either by taking a card or by using another
    /// alternative such as Skip.
    /// </summary>
    public static void RecordPaelSacrificeSkipped(CardReward reward)
    {
        if (reward == null) return;

        lock (_lock)
        {
            try
            {
                if (!_paelSacrificeRewards.Remove(reward, out _)) return;

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(PaelsWingRelicId);
                agg.SacrificesSkipped += 1;
                RefreshCurrentRunMetadataLocked();
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordPaelSacrificeSkipped failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record Unleash's Osty-current-HP contribution to its attack payload.
    /// This is card-specific intent metadata captured at the owner callback;
    /// observed damage still flows through DamageReceivedEntry.
    /// </summary>
    public static void RecordUnleashOstyHpAttackBonus(CardModel card, int ostyHpBonus)
    {
        if (card == null || ostyHpBonus <= 0) return;

        lock (_lock)
        {
            try
            {
                _pendingCombat ??= new PendingCombat();
                var instanceId = GetOrAssignInstanceId(card);
                var agg = GetOrCreateAggregate(_pendingCombat, instanceId);
                agg.TotalOstyHpAttackBonus += ostyHpBonus;
                agg.TimesOstyHpAttackBonusApplied++;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordUnleashOstyHpAttackBonus failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record a successful card-sourced Osty summon/revive.
    /// Called from <see cref="Patches.OstyCmdSummonPatch"/> after the async
    /// summon command returns the game-observed amount.
    /// </summary>
    public static void RecordOstySummoned(Player player, CardModel sourceCard, Creature? ostyCreature, decimal amount)
    {
        if (player == null || sourceCard == null || amount <= 0m) return;
        if (sourceCard.Owner != null && !ReferenceEquals(sourceCard.Owner, player)) return;

        lock (_lock)
        {
            try
            {
                _pendingCombat ??= new PendingCombat();
                var instanceId = GetOrAssignInstanceId(sourceCard);
                var agg = GetOrCreateAggregate(_pendingCombat, instanceId);
                agg.TimesOstySummoned++;
                agg.TotalOstyHpSummoned += amount;
                _pendingCombat.MetaStats.TotalOstyHpSummoned += amount;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordOstySummoned failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record a relic-owned Osty summon/revive after the shared summon command
    /// completes. Activation count follows the command completion; amount is
    /// the observed HP actually added.
    /// </summary>
    public static void RecordRelicOstySummon(AbstractModel sourceRelic, decimal amount)
    {
        var relicId = sourceRelic switch
        {
            MegaCrit.Sts2.Core.Models.Relics.BoundPhylactery => BoundPhylacteryRelicId,
            MegaCrit.Sts2.Core.Models.Relics.PhylacteryUnbound => PhylacteryUnboundRelicId,
            _ => null,
        };
        if (relicId == null) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(relicId);
                agg.Activations++;
                if (amount > 0m)
                    agg.TotalOstyHpSummoned += amount;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordRelicOstySummon failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record HP lost by any Osty body as a run-level meta fact.
    /// </summary>
    public static void RecordOstyHpLost(Creature creature, decimal hpLost)
    {
        if (creature == null || hpLost <= 0m) return;
        if (creature.Monster is not MegaCrit.Sts2.Core.Models.Monsters.Osty) return;

        lock (_lock)
        {
            try
            {
                _pendingCombat ??= new PendingCombat();
                _pendingCombat.MetaStats.TotalOstyDamageAbsorbed += hpLost;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordOstyHpLost failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Enter a scoped enemy status-card attribution window. Exact owner
    /// hooks, such as HauntedShip.HauntMove or PersonalHivePower's hit
    /// trigger, push this before the generated-card command runs.
    /// </summary>
    public static object? PushEnemyStatusCardSource(Creature source)
    {
        if (source?.Monster == null) return _enemyStatusSourceFrame.Value;

        var previous = _enemyStatusSourceFrame.Value;
        _enemyStatusSourceFrame.Value = new EnemyStatusSourceFrame
        {
            Source = source,
            Previous = previous,
        };
        return previous;
    }

    public static void RestoreEnemyStatusCardSource(object? previous)
    {
        _enemyStatusSourceFrame.Value = previous as EnemyStatusSourceFrame;
    }

    public static void RecordGeneratedStatusCardAdded(CardPileAddResult result, PileType pileType)
    {
        if (!result.success) return;
        var card = result.cardAdded;
        if (card == null || card.Type != CardType.Status) return;

        var source = _enemyStatusSourceFrame.Value?.Source;
        if (source?.Monster == null) return;

        var enemyId = source.Monster.Id.ToString();
        var cardId = card.Id.ToString();
        var displayName = FormatCardIdForDisplay(cardId);

        lock (_lock)
        {
            try
            {
                _pendingCombat ??= new PendingCombat();
                var agg = GetOrCreateEnemyAggregateLocked(enemyId);
                RecordEnemyStatusCardAddedLocked(agg, cardId, displayName, pileType);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordGeneratedStatusCardAdded failed: {e.Message}");
            }
        }
    }

    internal static void RecordEnemyDamageToPlayerForTest(
        EnemyAggregate agg,
        int blockedDamage,
        int unblockedDamage)
    {
        if (blockedDamage <= 0 && unblockedDamage <= 0) return;
        agg.DamageInstances++;
        agg.DamageAttempted += blockedDamage + unblockedDamage;
        agg.DamageBlocked += blockedDamage;
        agg.DamageDealt += unblockedDamage;
    }

    internal static void RecordEnemyStatusCardAddedForTest(
        EnemyAggregate agg,
        string cardId,
        string displayName,
        PileType pileType)
    {
        RecordEnemyStatusCardAddedLocked(agg, cardId, displayName, pileType);
    }

    /// <summary>
    /// Record a confirmed Book Repair Knife trigger and its Doom-death/healing
    /// payload.
    /// <paramref name="killCount"/> is counted from the game's
    /// <c>AfterDiedToDoom</c> creature list, so this tracks actual Doom
    /// deaths rather than inferred Doom applications.
    /// </summary>
    public static void RecordBookRepairKnifeTrigger(
        int killCount,
        Creature healedCreature,
        decimal attemptedHealing)
    {
        if (killCount <= 0) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(BookRepairKnifeRelicId);
                agg.DoomDeathTriggers += killCount;
                agg.DoomKills += killCount;

                if (healedCreature != null && attemptedHealing > 0m)
                {
                    agg.TotalHealingAttempted += attemptedHealing;
                    decimal initialMissingHp = Math.Max(0m, healedCreature.MaxHp - healedCreature.CurrentHp);
                    if (initialMissingHp <= 0m)
                    {
                        agg.TotalHealingLost += attemptedHealing;
                        AddHealingLostReasonLocked(agg, HealingLostFullHpReasonId, "full HP", attemptedHealing);
                        return;
                    }

                    _pendingRelicHeals.Add(new PendingRelicHealing
                    {
                        RelicId = BookRepairKnifeRelicId,
                        Creature = healedCreature,
                        Attempted = attemptedHealing,
                        InitialCurrentHp = healedCreature.CurrentHp,
                        InitialMissingHp = initialMissingHp,
                        PersistDirectlyToRun = false,
                    });
                }
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordBookRepairKnifeTrigger failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record Meal Ticket's shop-entry trigger and arm its observed healing
    /// window. Meal Ticket fires outside combat, so this writes directly to
    /// the committed run when no pending combat buffer exists.
    /// </summary>
    public static void RecordMealTicketTrigger(Creature healedCreature, decimal attemptedHealing)
    {
        RecordRelicHealingTrigger(MealTicketRelicId, healedCreature, attemptedHealing, nameof(RecordMealTicketTrigger));
    }

    /// <summary>
    /// Record Burning Blood's combat-victory trigger and arm its observed
    /// healing window. Depending on combat teardown timing, this can land in
    /// the pending combat buffer or directly in the run aggregate.
    /// </summary>
    public static void RecordBurningBloodTrigger(Creature healedCreature, decimal attemptedHealing)
    {
        RecordRelicHealingTrigger(BurningBloodRelicId, healedCreature, attemptedHealing, nameof(RecordBurningBloodTrigger));
    }

    /// <summary>
    /// Record Blood Vial's combat-start trigger and arm its observed healing
    /// window.
    /// </summary>
    public static void RecordBloodVialTrigger(Creature healedCreature, decimal attemptedHealing)
    {
        RecordRelicHealingTrigger(BloodVialRelicId, healedCreature, attemptedHealing, nameof(RecordBloodVialTrigger));
    }

    /// <summary>
    /// Record Pantograph's boss-combat-start trigger and arm its observed
    /// healing window.
    /// </summary>
    public static void RecordPantographTrigger(Creature healedCreature, decimal attemptedHealing)
    {
        RecordRelicHealingTrigger(PantographRelicId, healedCreature, attemptedHealing, nameof(RecordPantographTrigger));
    }

    /// <summary>
    /// Record Planisphere's ?-room heal and arm its observed healing window.
    /// This happens outside combat, so it normally writes directly to the
    /// committed run aggregate.
    /// </summary>
    public static void RecordPlanisphereTrigger(Creature healedCreature, decimal attemptedHealing)
    {
        RecordRelicHealingTrigger(PlanisphereRelicId, healedCreature, attemptedHealing, nameof(RecordPlanisphereTrigger));
    }

    /// <summary>
    /// Record Eternal Feather's rest-site activation and attempted heal. This
    /// happens outside combat, so the aggregate is written directly to the
    /// committed run data instead of the pending combat buffer.
    /// </summary>
    public static void RecordEternalFeatherTrigger(Creature healedCreature, decimal attemptedHealing)
    {
        RecordRelicHealingTrigger(
            EternalFeatherRelicId,
            healedCreature,
            attemptedHealing,
            nameof(RecordEternalFeatherTrigger),
            forceDirectRunPersistence: true,
            allowZeroAttempt: true);
    }

    /// <summary>
    /// Remember Regal Pillow's bonus during rest-site heal amount calculation.
    /// The game may query the modifier for UI, so stats are only committed by
    /// <see cref="CommitRegalPillowRestHeal"/> when the actual rest heal
    /// completes.
    /// </summary>
    public static void RememberRegalPillowRestHeal(Player player, decimal incomingHealAmount, decimal modifiedHealAmount)
    {
        if (player?.Creature == null) return;

        var bonusHealing = modifiedHealAmount - incomingHealAmount;
        if (bonusHealing <= 0m) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(player)) return;

                _pendingRegalPillowRestHeals[player] = new PendingRegalPillowRestHeal
                {
                    IncomingHealAmount = Math.Max(0m, incomingHealAmount),
                    AttemptedBonusHealing = bonusHealing,
                    InitialCurrentHp = player.Creature.CurrentHp,
                    InitialMissingHp = Math.Max(0m, player.Creature.MaxHp - player.Creature.CurrentHp),
                };
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RememberRegalPillowRestHeal failed: {e.Message}");
            }
        }
    }

    public static void CommitRegalPillowRestHeal(Player player)
    {
        if (player?.Creature == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(player)) return;
                if (!_pendingRegalPillowRestHeals.Remove(player, out var pending)) return;

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(RegalPillowRelicId);
                RecordRegalPillowRestHealForTest(agg, pending, player.Creature.CurrentHp);
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"CommitRegalPillowRestHeal failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Arm Precarious Shears pickup attribution. The pickup is async: the game
    /// prompts for cards, removes them from deck, then applies the max-HP cost.
    /// </summary>
    public static bool BeginPrecariousShearsPickup(RelicModel relic, out Player? player)
    {
        player = null;
        if (relic?.Owner?.Creature == null) return false;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic)) return false;
                if (!IsTrackedPlayer(relic.Owner)) return false;

                player = relic.Owner;
                _pendingPrecariousShearsPickups[player] = new PendingPrecariousShearsPickup
                {
                    StartingMaxHp = player.Creature.MaxHp,
                };
                return true;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"BeginPrecariousShearsPickup failed: {e.Message}");
                player = null;
                return false;
            }
        }
    }

    public static void CompletePrecariousShearsPickup(Player? player, bool succeeded)
    {
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                if (!_pendingPrecariousShearsPickups.Remove(player, out var pending)) return;
                if (!succeeded) return;

                decimal resultingMaxHp = player.Creature?.MaxHp ?? pending.StartingMaxHp;
                var agg = GetOrCreateCurrentRunRelicAggregateLocked(PrecariousShearsRelicId);
                RecordPrecariousShearsPickupForTest(
                    agg,
                    pending.CardsRemoved,
                    pending.StartingMaxHp,
                    resultingMaxHp);
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"CompletePrecariousShearsPickup failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Arm Sand Castle pickup attribution. Actual upgraded cards are observed
    /// from <see cref="RecordUpgrade"/> while the pickup task resolves.
    /// </summary>
    public static bool BeginSandCastlePickup(RelicModel relic, out Player? player)
    {
        player = null;
        if (relic?.Owner == null) return false;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic)) return false;
                if (!IsTrackedPlayer(relic.Owner)) return false;

                player = relic.Owner;
                _pendingSandCastlePickups[player] = new PendingSandCastlePickup();
                return true;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"BeginSandCastlePickup failed: {e.Message}");
                player = null;
                return false;
            }
        }
    }

    public static void CompleteSandCastlePickup(Player? player, bool succeeded)
    {
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                if (!_pendingSandCastlePickups.Remove(player, out var pending)) return;
                if (!succeeded) return;

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(SandCastleRelicId);
                RecordSandCastleUpgradesForTest(agg, pending.UpgradedCards);
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"CompleteSandCastlePickup failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Arm Whetstone pickup attribution. Actual upgraded cards are observed
    /// from <see cref="RecordUpgrade"/> while the pickup task resolves.
    /// </summary>
    public static bool BeginWhetstonePickup(RelicModel relic, out Player? player)
    {
        player = null;
        if (relic?.Owner == null) return false;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic)) return false;
                if (!IsTrackedPlayer(relic.Owner)) return false;

                player = relic.Owner;
                _pendingWhetstonePickups[player] = new PendingWhetstonePickup();
                return true;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"BeginWhetstonePickup failed: {e.Message}");
                player = null;
                return false;
            }
        }
    }

    public static void CompleteWhetstonePickup(Player? player, bool succeeded)
    {
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                if (!_pendingWhetstonePickups.Remove(player, out var pending)) return;
                if (!succeeded) return;

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(WhetstoneRelicId);
                RecordWhetstoneUpgradesForTest(agg, pending.UpgradedCards);
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"CompleteWhetstonePickup failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Arm War Paint pickup attribution. Actual upgraded cards are observed
    /// from <see cref="RecordUpgrade"/> while the pickup task resolves.
    /// </summary>
    public static bool BeginWarPaintPickup(RelicModel relic, out Player? player)
    {
        player = null;
        if (relic?.Owner == null) return false;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic)) return false;
                if (!IsTrackedPlayer(relic.Owner)) return false;

                player = relic.Owner;
                _pendingWarPaintPickups[player] = new PendingWarPaintPickup();
                return true;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"BeginWarPaintPickup failed: {e.Message}");
                player = null;
                return false;
            }
        }
    }

    public static void CompleteWarPaintPickup(Player? player, bool succeeded)
    {
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                if (!_pendingWarPaintPickups.Remove(player, out var pending)) return;
                if (!succeeded) return;

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(WarPaintRelicId);
                RecordWarPaintUpgradesForTest(agg, pending.UpgradedCards);
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"CompleteWarPaintPickup failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record Lee's Waffle's observed pickup HP gain. The relic first grants
    /// max HP, which itself heals, then heals to full; the full pickup delta is
    /// clearer than splitting those two game commands into separate attempts.
    /// </summary>
    public static void RecordLeesWafflePickupHpGained(
        decimal hpGained,
        decimal? originalMaxHp = null,
        decimal? newMaxHp = null)
    {
        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateCurrentRunRelicAggregateLocked(LeesWaffleRelicId);
                RecordLeesWafflePickupHpGainedForTest(agg, hpGained, originalMaxHp, newMaxHp);
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordLeesWafflePickupHpGained failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record Strawberry's observed pickup max-HP gain after its async pickup
    /// effect resolves.
    /// </summary>
    public static void RecordStrawberryMaxHpGained(
        Creature creature,
        decimal maxHpGained,
        decimal? originalMaxHp = null,
        decimal? newMaxHp = null)
    {
        if (creature?.Player == null || maxHpGained < 0m) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(creature.Player)) return;
                var agg = GetOrCreateCurrentRunRelicAggregateLocked(StrawberryRelicId);
                RecordStrawberryMaxHpGainedForTest(agg, maxHpGained, originalMaxHp, newMaxHp);
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordStrawberryMaxHpGained failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record Pear's observed pickup max-HP gain after its async pickup
    /// effect resolves.
    /// </summary>
    public static void RecordPearMaxHpGained(
        Creature creature,
        decimal maxHpGained,
        decimal? originalMaxHp = null,
        decimal? newMaxHp = null)
    {
        if (creature?.Player == null || maxHpGained < 0m) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(creature.Player)) return;
                var agg = GetOrCreateCurrentRunRelicAggregateLocked(PearRelicId);
                RecordPearMaxHpGainedForTest(agg, maxHpGained, originalMaxHp, newMaxHp);
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordPearMaxHpGained failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record Chosen Cheese's max HP at the pickup boundary. This value is
    /// displayed as the relic's starting max HP and is kept separate from later
    /// combat-end gains because unrelated max-HP changes can happen in between.
    /// </summary>
    public static void RecordChosenCheeseObtained(RelicModel relic, Player player, decimal startingMaxHp)
    {
        if (!IsChosenCheeseStatsRelic(relic) || player == null || startingMaxHp < 0m) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(player)) return;
                var agg = GetOrCreateCurrentRunRelicAggregateLocked(ChosenCheeseRelicId);
                RecordChosenCheeseStartingMaxHpForTest(agg, startingMaxHp);
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordChosenCheeseObtained failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record Chosen Cheese's observed max-HP gain after its combat-end
    /// callback completes. The callback can finish around combat promotion, so
    /// route the gained amount to pending combat when it still exists and
    /// otherwise save directly.
    /// </summary>
    public static void RecordChosenCheeseMaxHpGained(Creature creature, decimal maxHpGained)
    {
        if (creature?.Player == null || maxHpGained < 0m) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(creature.Player)) return;
                if (_currentRun == null && _pendingCombat == null) return;

                bool persistDirectlyToRun = _pendingCombat == null;
                var agg = GetOrCreateRelicAggregateForCurrentContextLocked(ChosenCheeseRelicId);
                RecordChosenCheeseMaxHpGainedForTest(agg, maxHpGained);
                if (persistDirectlyToRun)
                    SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordChosenCheeseMaxHpGained failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record Darkstone Periapt reacting to a successfully acquired curse.
    /// The curse count comes from the relic's own post-pile-change condition;
    /// max HP gained is the observed delta after the game's GainMaxHp command.
    /// </summary>
    public static void RecordDarkstonePeriaptCurseAcquired(
        int maxHpGained,
        decimal? originalMaxHp = null,
        decimal? newMaxHp = null)
    {
        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateCurrentRunRelicAggregateLocked(DarkstonePeriaptRelicId);
                RecordDarkstonePeriaptCurseAcquiredForTest(agg, maxHpGained, originalMaxHp, newMaxHp);
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordDarkstonePeriaptCurseAcquired failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Arm Leafy Poultice pickup attribution. The pickup loses max HP and then
    /// transforms up to two basic cards through <c>CardCmd.Transform</c>.
    /// </summary>
    public static bool BeginLeafyPoulticePickup(RelicModel relic, out Player? player)
    {
        player = null;
        if (relic?.Owner?.Creature == null) return false;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic)) return false;
                if (!IsTrackedPlayer(relic.Owner)) return false;

                player = relic.Owner;
                _pendingLeafyPoulticePickups.Add(player);
                return true;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"BeginLeafyPoulticePickup failed: {e.Message}");
                player = null;
                return false;
            }
        }
    }

    public static void CompleteLeafyPoulticePickup(
        Player? player,
        bool succeeded,
        decimal originalMaxHp,
        decimal newMaxHp)
    {
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                _pendingLeafyPoulticePickups.Remove(player);
                if (!succeeded) return;

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(LeafyPoulticeRelicId);
                RecordLeafyPoulticeMaxHpChangedForTest(agg, originalMaxHp, newMaxHp);
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"CompleteLeafyPoulticePickup failed: {e.Message}");
            }
        }
    }

    public static bool TryCaptureLeafyPoulticeTransformSources(
        ref IEnumerable<CardTransformation> transformations,
        out IReadOnlyList<CardModel>? orderedSources)
    {
        orderedSources = null;

        lock (_lock)
        {
            try
            {
                var transformationsArray = transformations?.ToArray() ?? Array.Empty<CardTransformation>();
                transformations = transformationsArray;
                if (transformationsArray.Length == 0) return false;

                var owner = transformationsArray
                    .Select(t => t.Original?.Owner)
                    .FirstOrDefault(p => p != null);
                if (owner == null || !_pendingLeafyPoulticePickups.Contains(owner)) return false;

                orderedSources = transformationsArray
                    .Where(t => t.Original?.Owner != null && ReferenceEquals(t.Original.Owner, owner))
                    .Select((t, index) => new
                    {
                        Source = t.Original,
                        InputIndex = index,
                        PileType = TryGetPileTypeSortValue(t.Original),
                        PileIndex = TryGetPileIndex(t.Original),
                    })
                    .OrderBy(t => t.PileType)
                    .ThenBy(t => t.PileIndex)
                    .ThenBy(t => t.InputIndex)
                    .Select(t => t.Source)
                    .ToList();
                return orderedSources.Count > 0;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"TryCaptureLeafyPoulticeTransformSources failed: {e.Message}");
                orderedSources = null;
                return false;
            }
        }
    }

    public static void RecordLeafyPoulticeTransformResults(
        IReadOnlyList<CardModel>? orderedSources,
        IEnumerable<CardPileAddResult>? results)
    {
        if (orderedSources == null || orderedSources.Count == 0 || results == null) return;

        lock (_lock)
        {
            try
            {
                var owner = orderedSources
                    .Select(c => c?.Owner)
                    .FirstOrDefault(p => p != null);
                if (owner == null || !_pendingLeafyPoulticePickups.Contains(owner)) return;

                var resultList = results.ToList();
                var count = Math.Min(orderedSources.Count, resultList.Count);
                if (count <= 0) return;

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(LeafyPoulticeRelicId);
                var recorded = 0;
                for (var i = 0; i < count; i++)
                {
                    var result = resultList[i];
                    if (!result.success || result.cardAdded == null) continue;

                    RecordRelicCardTransformationForTest(
                        agg,
                        GetCardIdForStats(orderedSources[i]),
                        GetCardDisplayNameForStats(orderedSources[i]),
                        GetCardIdForStats(result.cardAdded),
                        GetCardDisplayNameForStats(result.cardAdded));
                    recorded++;
                }

                if (recorded > 0)
                    SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordLeafyPoulticeTransformResults failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record Leafy Poultice's observed pickup max-HP loss after its async
    /// pickup effect resolves.
    /// </summary>
    public static void RecordLeafyPoulticeMaxHpChanged(decimal originalMaxHp, decimal newMaxHp)
    {
        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateCurrentRunRelicAggregateLocked(LeafyPoulticeRelicId);
                RecordLeafyPoulticeMaxHpChangedForTest(agg, originalMaxHp, newMaxHp);
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordLeafyPoulticeMaxHpChanged failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record that Strike Dummy was obtained, stamping the current permanent
    /// deck split between base Strikes and other Strike-tagged cards.
    /// </summary>
    public static void RecordStrikeDummyObtained(RelicModel relic, Player player)
    {
        if (!IsStrikeDummyStatsRelic(relic) || player == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(player)) return;
                var agg = GetOrCreateCurrentRunRelicAggregateLocked(StrikeDummyRelicId);
                RefreshStrikeDummyDeckCountsLocked(agg, player);
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordStrikeDummyObtained failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record that Miniature Cannon was obtained, stamping the current
    /// permanent-deck count of upgraded attacks.
    /// </summary>
    public static void RecordMiniatureCannonObtained(RelicModel relic, Player player)
    {
        if (!IsMiniatureCannonStatsRelic(relic) || player == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(player)) return;
                var agg = GetOrCreateCurrentRunRelicAggregateLocked(MiniatureCannonRelicId);
                RefreshMiniatureCannonDeckCountsLocked(agg, player);
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordMiniatureCannonObtained failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Return Strike Dummy stats after refreshing the current permanent-deck
    /// composition. Used by the relic hover tooltip so hot reload/continue
    /// state catches up even if the pickup happened before this build.
    /// </summary>
    public static RelicAggregate GetStrikeDummyAggregate()
    {
        lock (_lock)
        {
            try
            {
                RefreshStrikeDummyDeckCountsIfOwnedLocked();

                RelicAggregate? result = null;
                if (_currentRun != null && _currentRun.RelicAggregates.TryGetValue(StrikeDummyRelicId, out var committed))
                {
                    result = new RelicAggregate();
                    MergeRelicAggregateInto(result, committed);
                }

                if (_pendingCombat != null && _pendingCombat.RelicAggregates.TryGetValue(StrikeDummyRelicId, out var pending))
                {
                    result ??= new RelicAggregate();
                    MergeRelicAggregateInto(result, pending);
                }

                return result ?? new RelicAggregate();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"GetStrikeDummyAggregate failed: {e.Message}");
                return new RelicAggregate();
            }
        }
    }

    /// <summary>
    /// Return Miniature Cannon stats after refreshing the current permanent-
    /// deck upgraded-attack count.
    /// </summary>
    public static RelicAggregate GetMiniatureCannonAggregate()
    {
        lock (_lock)
        {
            try
            {
                RefreshMiniatureCannonDeckCountsIfOwnedLocked();

                RelicAggregate? result = null;
                if (_currentRun != null && _currentRun.RelicAggregates.TryGetValue(MiniatureCannonRelicId, out var committed))
                {
                    result = new RelicAggregate();
                    MergeRelicAggregateInto(result, committed);
                }

                if (_pendingCombat != null && _pendingCombat.RelicAggregates.TryGetValue(MiniatureCannonRelicId, out var pending))
                {
                    result ??= new RelicAggregate();
                    MergeRelicAggregateInto(result, pending);
                }

                return result ?? new RelicAggregate();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"GetMiniatureCannonAggregate failed: {e.Message}");
                return new RelicAggregate();
            }
        }
    }

    public static void RecordBookmarkActivations(IEnumerable<CardRarity> rarities)
    {
        if (rarities == null) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(BookmarkRelicId);
                foreach (var rarity in rarities)
                    RecordBookmarkActivation(agg, rarity);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordBookmarkActivations failed: {e.Message}");
            }
        }
    }

    public static void RecordPaelsEyeActivation(int statusesExhausted, int cursesExhausted)
    {
        lock (_lock)
        {
            try
            {
                var persistDirectlyToRun = _pendingCombat == null;
                var agg = GetOrCreateRelicAggregateForCurrentContextLocked(PaelsEyeRelicId);
                RecordPaelsEyeActivationForTest(agg, statusesExhausted, cursesExhausted);
                if (persistDirectlyToRun)
                    SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordPaelsEyeActivation failed: {e.Message}");
            }
        }
    }

    public static void NotePaelsEyeActivationStarted(Player? player)
    {
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                if (_pendingCombat == null) return;
                if (!IsTrackedPlayer(player)) return;

                _pendingCombat.PaelsEyeCombatCountedPlayers.Add(player);
                _pendingCombat.PaelsEyeActivationStartedPlayers.Add(player);
                GetOrCreatePendingRelicAggregateLocked(PaelsEyeRelicId);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"NotePaelsEyeActivationStarted failed: {e.Message}");
            }
        }
    }

    private static void RecordBookmarkActivation(RelicAggregate agg, CardRarity rarity)
    {
        agg.Activations += 1;

        switch (rarity)
        {
            case CardRarity.Common:
                agg.BookmarkCommonActivations += 1;
                break;
            case CardRarity.Uncommon:
                agg.BookmarkUncommonActivations += 1;
                break;
            case CardRarity.Rare:
                agg.BookmarkRareActivations += 1;
                break;
        }
    }

    internal static void RecordPaelsEyeActivationForTest(
        RelicAggregate agg,
        int statusesExhausted,
        int cursesExhausted)
    {
        if (agg == null) return;

        agg.Activations += 1;
        agg.StatusCardsExhausted += Math.Max(0, statusesExhausted);
        agg.CurseCardsExhausted += Math.Max(0, cursesExhausted);
    }

    internal static void RecordPaelsEyeCombatWithoutActivationForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;

        agg.CombatsWithoutActivation += Math.Max(0, count);
    }

    internal static void RecordLeesWafflePickupHpGainedForTest(
        RelicAggregate agg,
        decimal hpGained,
        decimal? originalMaxHp = null,
        decimal? newMaxHp = null)
    {
        if (agg == null || hpGained < 0m) return;

        agg.Activations++;
        agg.TotalHealingRestored += hpGained;
        RecordRelicMaxHpChangeForTest(agg, originalMaxHp, newMaxHp);
    }

    internal static void RecordStrawberryMaxHpGainedForTest(
        RelicAggregate agg,
        decimal maxHpGained,
        decimal? originalMaxHp = null,
        decimal? newMaxHp = null)
    {
        if (agg == null || maxHpGained < 0m) return;

        agg.Activations++;
        agg.MaxHpGained += maxHpGained;
        RecordRelicMaxHpChangeForTest(agg, originalMaxHp, newMaxHp);
    }

    internal static void RecordPearMaxHpGainedForTest(
        RelicAggregate agg,
        decimal maxHpGained,
        decimal? originalMaxHp = null,
        decimal? newMaxHp = null)
    {
        if (agg == null || maxHpGained < 0m) return;

        agg.Activations++;
        agg.MaxHpGained += maxHpGained;
        RecordRelicMaxHpChangeForTest(agg, originalMaxHp, newMaxHp);
    }

    internal static void RecordRegalPillowRestHealForTest(
        RelicAggregate agg,
        decimal incomingHealAmount,
        decimal attemptedBonusHealing,
        decimal initialCurrentHp,
        decimal initialMissingHp,
        decimal finalCurrentHp)
    {
        if (agg == null) return;
        if (attemptedBonusHealing <= 0m) return;

        RecordRegalPillowRestHealForTest(
            agg,
            new PendingRegalPillowRestHeal
            {
                IncomingHealAmount = Math.Max(0m, incomingHealAmount),
                AttemptedBonusHealing = attemptedBonusHealing,
                InitialCurrentHp = initialCurrentHp,
                InitialMissingHp = Math.Max(0m, initialMissingHp),
            },
            finalCurrentHp);
    }

    private static void RecordRegalPillowRestHealForTest(
        RelicAggregate agg,
        PendingRegalPillowRestHeal pending,
        decimal finalCurrentHp)
    {
        if (agg == null || pending.AttemptedBonusHealing <= 0m) return;

        decimal observedTotalRestored = Math.Max(0m, finalCurrentHp - pending.InitialCurrentHp);
        decimal baselineRestored = Math.Min(pending.InitialMissingHp, pending.IncomingHealAmount);
        decimal bonusRestored = Math.Min(
            pending.AttemptedBonusHealing,
            Math.Max(0m, observedTotalRestored - baselineRestored));
        decimal bonusLost = Math.Max(0m, pending.AttemptedBonusHealing - bonusRestored);

        agg.Activations++;
        agg.TotalHealingAttempted += pending.AttemptedBonusHealing;
        agg.TotalHealingRestored += bonusRestored;
        agg.TotalHealingLost += bonusLost;

        decimal missingAfterBaseline = Math.Max(0m, pending.InitialMissingHp - pending.IncomingHealAmount);
        decimal fullHpLost = Math.Min(bonusLost, Math.Max(0m, pending.AttemptedBonusHealing - missingAfterBaseline));
        if (fullHpLost > 0m)
            AddHealingLostReasonLocked(agg, HealingLostFullHpReasonId, "full HP", fullHpLost);

        decimal otherLost = Math.Max(0m, bonusLost - fullHpLost);
        if (otherLost > 0m)
            AddHealingLostReasonLocked(agg, HealingLostOtherReasonId, "other/prevented", otherLost);
    }

    internal static void RecordPrecariousShearsPickupForTest(
        RelicAggregate agg,
        IEnumerable<string>? cardsRemoved,
        decimal startingMaxHp,
        decimal resultingMaxHp)
    {
        if (agg == null) return;

        agg.CardsRemoved ??= new List<string>();
        if (cardsRemoved != null)
        {
            foreach (var card in cardsRemoved)
            {
                if (!string.IsNullOrWhiteSpace(card))
                    agg.CardsRemoved.Add(card);
            }
        }

        agg.StartingMaxHp = Math.Max(0m, startingMaxHp);
        agg.ResultingMaxHp = Math.Max(0m, resultingMaxHp);
        RecordRelicMaxHpChangeForTest(agg, startingMaxHp, resultingMaxHp);
    }

    internal static void RecordSandCastleUpgradesForTest(
        RelicAggregate agg,
        IEnumerable<string>? upgradedCards)
        => RecordRelicUpgradedCards(agg, upgradedCards);

    internal static void RecordWhetstoneUpgradesForTest(
        RelicAggregate agg,
        IEnumerable<string>? upgradedCards)
        => RecordRelicUpgradedCards(agg, upgradedCards);

    internal static void RecordWarPaintUpgradesForTest(
        RelicAggregate agg,
        IEnumerable<string>? upgradedCards)
        => RecordRelicUpgradedCards(agg, upgradedCards);

    private static void RecordRelicUpgradedCards(
        RelicAggregate agg,
        IEnumerable<string>? upgradedCards)
    {
        if (agg == null) return;

        agg.UpgradedCards ??= new List<string>();
        var added = 0;
        if (upgradedCards != null)
        {
            foreach (var card in upgradedCards)
            {
                if (!string.IsNullOrWhiteSpace(card))
                {
                    agg.UpgradedCards.Add(card);
                    added++;
                }
            }
        }

        agg.CardsUpgraded += added;
    }

    internal static void RecordDarkstonePeriaptCurseAcquiredForTest(
        RelicAggregate agg,
        int maxHpGained,
        decimal? originalMaxHp = null,
        decimal? newMaxHp = null)
    {
        if (agg == null) return;

        agg.Activations++;
        agg.CursesAcquired++;
        agg.TotalMaxHpGained += Math.Max(0, maxHpGained);
        RecordRelicMaxHpChangeForTest(agg, originalMaxHp, newMaxHp);
    }

    internal static void RecordChosenCheeseMaxHpGainedForTest(
        RelicAggregate agg,
        decimal maxHpGained)
    {
        if (agg == null || maxHpGained < 0m) return;

        agg.MaxHpGained += maxHpGained;
    }

    internal static void RecordChosenCheeseStartingMaxHpForTest(RelicAggregate agg, decimal startingMaxHp)
    {
        if (agg == null || startingMaxHp < 0m) return;

        agg.OriginalMaxHp ??= startingMaxHp;
    }

    internal static void RecordLeafyPoulticeMaxHpChangedForTest(
        RelicAggregate agg,
        decimal originalMaxHp,
        decimal newMaxHp)
    {
        if (agg == null) return;

        agg.Activations++;
        RecordRelicMaxHpChangeForTest(agg, originalMaxHp, newMaxHp);
    }

    internal static void RecordRelicCardTransformationForTest(
        RelicAggregate agg,
        string? sourceCardId,
        string? sourceDisplayName,
        string? resultCardId,
        string? resultDisplayName)
    {
        if (agg == null) return;

        sourceCardId = sourceCardId ?? "";
        resultCardId = resultCardId ?? "";
        if (string.IsNullOrWhiteSpace(sourceCardId)
            && string.IsNullOrWhiteSpace(sourceDisplayName)
            && string.IsNullOrWhiteSpace(resultCardId)
            && string.IsNullOrWhiteSpace(resultDisplayName))
            return;

        sourceDisplayName = string.IsNullOrWhiteSpace(sourceDisplayName)
            ? FormatCardIdForDisplay(sourceCardId)
            : sourceDisplayName;
        resultDisplayName = string.IsNullOrWhiteSpace(resultDisplayName)
            ? FormatCardIdForDisplay(resultCardId)
            : resultDisplayName;

        agg.CardTransformations ??= new List<RelicCardTransformationAggregate>();
        agg.CardTransformations.Add(new RelicCardTransformationAggregate
        {
            SourceCardId = sourceCardId,
            SourceDisplayName = sourceDisplayName ?? "",
            ResultCardId = resultCardId,
            ResultDisplayName = resultDisplayName ?? "",
        });
    }

    internal static void RecordRelicMaxHpChangeForTest(
        RelicAggregate agg,
        decimal? originalMaxHp,
        decimal? newMaxHp)
    {
        if (agg == null || !originalMaxHp.HasValue || !newMaxHp.HasValue) return;

        var original = Math.Max(0m, originalMaxHp.Value);
        var current = Math.Max(0m, newMaxHp.Value);
        agg.OriginalMaxHp ??= original;
        agg.NewMaxHp = current;
    }

    internal static void RecordStrikeDummyStrikePlayedForTest(RelicAggregate agg)
    {
        if (agg == null) return;
        agg.StrikeDummyStrikesPlayed += 1;
    }

    internal static void SetStrikeDummyDeckCountsForTest(
        RelicAggregate agg,
        int baseStrikesInDeck,
        int nonBaseStrikeCardsInDeck)
    {
        if (agg == null) return;
        agg.StrikeDummyBaseStrikesInDeck = Math.Max(0, baseStrikesInDeck);
        agg.StrikeDummyNonBaseStrikeCardsInDeck = Math.Max(0, nonBaseStrikeCardsInDeck);
    }

    internal static void RecordMiniatureCannonUpgradedAttackPlayedForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.MiniatureCannonUpgradedAttackPlays += Math.Max(0, count);
    }

    internal static void RecordMiniatureCannonUpgradedAttackHitForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.MiniatureCannonUpgradedAttackHits += Math.Max(0, count);
    }

    internal static void SetMiniatureCannonDeckCountForTest(RelicAggregate agg, int upgradedAttacksInDeck)
    {
        if (agg == null) return;
        agg.MiniatureCannonUpgradedAttacksInDeck = Math.Max(0, upgradedAttacksInDeck);
    }

    internal static void RecordBookmarkCombatForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.BookmarkCombats += Math.Max(0, count);
    }

    internal static void RecordBookmarkActivationForTest(RelicAggregate agg, CardRarity rarity)
    {
        if (agg == null) return;
        RecordBookmarkActivation(agg, rarity);
    }

    internal static void RecordShovelRelicAcquiredForTest(RelicAggregate agg, RelicRarity rarity)
    {
        if (agg == null) return;

        agg.Activations++;
        agg.RelicsAcquired++;

        switch (rarity)
        {
            case RelicRarity.Common:
                agg.CommonRelicsAcquired++;
                break;
            case RelicRarity.Uncommon:
                agg.UncommonRelicsAcquired++;
                break;
            case RelicRarity.Rare:
                agg.RareRelicsAcquired++;
                break;
        }
    }

    internal static void RecordShovelCampfireNotDugForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.CampfiresNotDug += Math.Max(0, count);
    }

    internal static bool IsStrikeDummyStatsRelic(RelicModel? relic)
    {
        try
        {
            return relic is StrikeDummy or FakeStrikeDummy;
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsMiniatureCannonStatsRelic(RelicModel? relic)
    {
        try
        {
            return relic is MiniatureCannon
                || string.Equals(
                    relic?.GetType().FullName,
                    "MegaCrit.Sts2.Core.Models.Relics.MiniatureCannon",
                    StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsChosenCheeseStatsRelic(RelicModel? relic)
    {
        try
        {
            return relic is ChosenCheese
                || string.Equals(
                    relic?.GetType().FullName,
                    "MegaCrit.Sts2.Core.Models.Relics.ChosenCheese",
                    StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static void RecordRelicHealingTrigger(
        string relicId,
        Creature healedCreature,
        decimal attemptedHealing,
        string callerName,
        bool forceDirectRunPersistence = false,
        bool allowZeroAttempt = false)
    {
        if (healedCreature == null) return;
        if (attemptedHealing < 0m || (!allowZeroAttempt && attemptedHealing <= 0m)) return;

        lock (_lock)
        {
            try
            {
                bool persistDirectlyToRun = forceDirectRunPersistence || _pendingCombat == null;
                var agg = persistDirectlyToRun
                    ? GetOrCreateCurrentRunRelicAggregateLocked(relicId)
                    : GetOrCreateRelicAggregateLocked(relicId);
                agg.Activations++;

                if (attemptedHealing <= 0m)
                {
                    if (persistDirectlyToRun)
                        SaveCurrentRun();
                    return;
                }

                agg.TotalHealingAttempted += attemptedHealing;
                decimal initialMissingHp = Math.Max(0m, healedCreature.MaxHp - healedCreature.CurrentHp);
                if (initialMissingHp <= 0m)
                {
                    agg.TotalHealingLost += attemptedHealing;
                    AddHealingLostReasonLocked(agg, HealingLostFullHpReasonId, "full HP", attemptedHealing);
                    if (persistDirectlyToRun)
                        SaveCurrentRun();
                    return;
                }

                _pendingRelicHeals.Add(new PendingRelicHealing
                {
                    RelicId = relicId,
                    Creature = healedCreature,
                    Attempted = attemptedHealing,
                    InitialCurrentHp = healedCreature.CurrentHp,
                    InitialMissingHp = initialMissingHp,
                    PersistDirectlyToRun = persistDirectlyToRun,
                });
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"{callerName} failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record observed HP restored while an owner-specific relic healing window
    /// is armed. Called from <see cref="Patches.HookAfterCurrentHpChangedPatch"/>.
    /// </summary>
    public static void RecordRelicHealingHpChanged(Creature creature, decimal delta)
    {
        if (creature == null || delta <= 0m) return;

        lock (_lock)
        {
            for (int i = _pendingRelicHeals.Count - 1; i >= 0; i--)
            {
                var pending = _pendingRelicHeals[i];
                if (!ReferenceEquals(pending.Creature, creature)) continue;

                decimal remaining = Math.Max(0m, pending.Attempted - pending.ActualRestored);
                if (remaining <= 0m) return;

                decimal restored = Math.Min(delta, remaining);
                pending.ActualRestored += restored;
                var agg = GetOrCreateRelicAggregateForHealingLocked(pending);
                agg.TotalHealingRestored += restored;
                return;
            }
        }
    }

    /// <summary>
    /// Finalize lost healing for a relic healing window after the game's heal
    /// task has completed. Records full-HP overfill separately from any other
    /// prevention or modification gap.
    /// </summary>
    public static void FinalizeRelicHealing(Creature creature, string relicId)
    {
        if (creature == null || string.IsNullOrWhiteSpace(relicId)) return;

        lock (_lock)
        {
            for (int i = _pendingRelicHeals.Count - 1; i >= 0; i--)
            {
                var pending = _pendingRelicHeals[i];
                if (!ReferenceEquals(pending.Creature, creature)) continue;
                if (!string.Equals(pending.RelicId, relicId, StringComparison.Ordinal)) continue;

                _pendingRelicHeals.RemoveAt(i);
                decimal observedRestored = CalculateRelicHealingActualRestored(
                    pending.Attempted,
                    pending.ActualRestored,
                    pending.InitialCurrentHp,
                    creature.CurrentHp);
                var observedRestoredDelta = Math.Max(0m, observedRestored - pending.ActualRestored);
                if (observedRestoredDelta > 0m)
                {
                    var restoredAgg = GetOrCreateRelicAggregateForHealingLocked(pending);
                    restoredAgg.TotalHealingRestored += observedRestoredDelta;
                }

                decimal lost = Math.Max(0m, pending.Attempted - observedRestored);
                if (lost <= 0m)
                {
                    if (_pendingCombat == null)
                        SaveCurrentRun();
                    return;
                }

                var agg = GetOrCreateRelicAggregateForHealingLocked(pending);
                agg.TotalHealingLost += lost;

                decimal fullHpLost = Math.Max(0m, pending.Attempted - pending.InitialMissingHp);
                fullHpLost = Math.Min(fullHpLost, lost);
                if (fullHpLost > 0m)
                    AddHealingLostReasonLocked(agg, HealingLostFullHpReasonId, "full HP", fullHpLost);

                decimal otherLost = Math.Max(0m, lost - fullHpLost);
                if (otherLost > 0m)
                    AddHealingLostReasonLocked(agg, HealingLostOtherReasonId, "other/prevented", otherLost);

                // Persist a late finalize that landed directly in the committed
                // run (no live pending combat to carry it to CombatEnded).
                if (pending.PersistDirectlyToRun || _pendingCombat == null)
                    SaveCurrentRun();
                return;
            }
        }
    }

    internal static decimal CalculateRelicHealingActualRestored(
        decimal attempted,
        decimal hookRecordedRestored,
        decimal initialCurrentHp,
        decimal finalCurrentHp)
    {
        if (attempted <= 0m) return 0m;

        var observedHpGain = Math.Max(0m, finalCurrentHp - initialCurrentHp);
        return Math.Min(attempted, Math.Max(hookRecordedRestored, observedHpGain));
    }

    /// <summary>
    /// Arm the one-shot flag that attributes the next player energy gain to
    /// Happy Flower. Called from
    /// <see cref="Patches.HappyFlowerAfterSideTurnStartPatch"/> when Happy
    /// Flower's <c>AfterSideTurnStart</c> fires on the player's side.
    /// </summary>
    public static void ArmHappyFlowerEnergyAttribution()
    {
        lock (_lock)
        {
            _pendingCombat ??= new PendingCombat();
            // Bug fix (#250): the energy grant is synchronous within
            // AfterSideTurnStart, so maxHistoryAdvance=0 keeps it tight and the
            // window self-expires by history-count rather than depending on the
            // fragile cross-hook AfterPlayerTurnStart disarm ordering.
            _pendingCombat.Windows.Arm(HappyFlowerRelicId, AttributionEventKind.PlayerEnergyGain,
                CurrentHistoryCountLocked(), maxHistoryAdvance: 0);
        }
    }

    /// <summary>
    /// No-op safety reset kept wired at <c>Hook.AfterSideTurnStart</c>. Happy
    /// Flower's window is armed with maxHistoryAdvance=0 (see
    /// <see cref="ArmHappyFlowerEnergyAttribution"/>), so it self-expires by
    /// history-count and needs no explicit disarm — a Windows.Disarm call
    /// here would find nothing armed.
    /// </summary>
    public static void DisarmHappyFlowerEnergyAttribution()
    {
    }

    internal static void RecordHappyFlowerEnergyGeneratedForTest(RelicAggregate agg, int amount, int combats)
    {
        if (agg == null) return;
        agg.EnergyGenerated += Math.Max(0, amount);
        agg.EnergyGeneratedCombats += Math.Max(0, combats);
    }

    /// <summary>
    /// Record a Nunchaku-owned attack play and arm energy attribution if the
    /// relic's live counter is one attack away from its energy trigger.
    /// </summary>
    public static void RecordNunchakuAttackPlayedAndArmEnergyAttribution(Nunchaku relic, CardPlay cardPlay)
    {
        if (relic?.Owner == null || cardPlay?.Card == null) return;
        if (cardPlay.Card.Type != CardType.Attack) return;

        var owner = relic.Owner;
        if (cardPlay.Card.Owner != null && !ReferenceEquals(cardPlay.Card.Owner, owner)) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic)) return;
                if (!IsTrackedPlayer(owner)) return;

                _pendingCombat ??= new PendingCombat();
                RecordNunchakuCombatForPlayerLocked(owner);

                var agg = GetOrCreatePendingRelicAggregateLocked(NunchakuRelicId);
                RecordNunchakuAttackPlayedForTest(agg);

                if (relic.AttacksPlayed >= 9)
                {
                    _pendingCombat.Windows.Arm(
                        NunchakuRelicId,
                        AttributionEventKind.PlayerEnergyGain,
                        CurrentHistoryCountLocked(),
                        ownerId: owner,
                        maxHistoryAdvance: 0);
                }
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordNunchakuAttackPlayedAndArmEnergyAttribution failed: {e.Message}");
            }
        }
    }

    internal static void RecordNunchakuAttackPlayedForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.NunchakuAttacksPlayed += Math.Max(0, count);
    }

    internal static void RecordNunchakuCombatEndChargeForTest(RelicAggregate agg, int charge)
    {
        if (agg == null) return;

        charge = Math.Max(0, charge);
        agg.NunchakuCombatEndChargeTotal += charge;
        if (charge == 8)
            agg.NunchakuCombatsEndedOn8Charges += 1;
        else if (charge == 9)
            agg.NunchakuCombatsEndedOn9Charges += 1;
    }

    /// <summary>
    /// Record Candelabra's owner-specific turn-2 activation and arm observed
    /// energy attribution for its immediate gain.
    /// </summary>
    public static void RecordCandelabraActivationAndArmEnergyAttribution(Player? owner)
    {
        if (owner == null || !IsTrackedPlayer(owner)) return;

        lock (_lock)
        {
            try
            {
                _pendingCombat ??= new PendingCombat();
                var agg = GetOrCreateRelicAggregateLocked(CandelabraRelicId);
                agg.Activations += 1;
                _pendingCombat.Windows.Arm(
                    CandelabraRelicId,
                    AttributionEventKind.PlayerEnergyGain,
                    CurrentHistoryCountLocked(),
                    ownerId: owner,
                    maxHistoryAdvance: 0);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordCandelabraActivationAndArmEnergyAttribution failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Count player turn 2 ending with unspent energy while Candelabra is held.
    /// Called from the global before-turn-end hook so the energy pool has not
    /// been cleared yet.
    /// </summary>
    public static void RecordCandelabraSecondTurnEndedWithExcessEnergy(IEnumerable<Creature>? participants)
    {
        if (participants == null) return;

        lock (_lock)
        {
            try
            {
                foreach (var creature in participants)
                {
                    var player = creature?.Player;
                    if (player == null || !IsTrackedPlayer(player)) continue;
                    var combatState = player.PlayerCombatState;
                    if (combatState == null || combatState.TurnNumber != 2) continue;
                    if (combatState.Energy <= 0) continue;
                    if (!PlayerHasCandelabra(player)) continue;

                    _pendingCombat ??= new PendingCombat();
                    if (!_pendingCombat.CandelabraSecondTurnExcessRecordedPlayers.Add(player)) continue;

                    var agg = GetOrCreateRelicAggregateLocked(CandelabraRelicId);
                    agg.SecondTurnsEndedWithExcessEnergy += 1;
                }
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordCandelabraSecondTurnEndedWithExcessEnergy failed: {e.Message}");
            }
        }
    }

    private static bool PlayerHasCandelabra(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is Candelabra);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// No-op arm/disarm pair kept wired for Booming Conch's Elite combat-start
    /// energy. The energy-gain consumer that once read the armed flag was
    /// already unreachable, so Booming Conch's energy attribution is currently
    /// non-functional (a known bug: it never routed to the registry). Fixing
    /// it — arm a PlayerEnergyGain window and credit EnergyGenerated, mirroring
    /// Happy Flower — is a separate, live-verified change; these seams stay so
    /// it has a home. (Its card-draw stat via RecordBoomingConchDraw still
    /// works.)
    /// </summary>
    public static void ArmBoomingConchEnergyAttribution()
    {
    }

    public static void DisarmBoomingConchEnergyAttribution()
    {
    }

    public static void RecordBoomingConchDraw(int cardsDrawn)
    {
        if (cardsDrawn <= 0) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(BoomingConchRelicId);
                agg.AdditionalCardsDrawn += cardsDrawn;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordBoomingConchDraw failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Arm the one-shot flag that attributes the next player Vigor gain to
    /// Akabeko's combat-start effect.
    /// </summary>
    public static void ArmAkabekoVigorAttribution()
    {
        lock (_lock)
        {
            _pendingAkabekoVigorAttribution = true;
        }
    }

    public static void DisarmAkabekoVigorAttribution()
    {
        lock (_lock)
        {
            _pendingAkabekoVigorAttribution = false;
        }
    }

    public static void RecordAkabekoVigorGained(int amount)
    {
        if (amount <= 0) return;

        lock (_lock)
        {
            try
            {
                if (!_pendingAkabekoVigorAttribution) return;
                _pendingAkabekoVigorAttribution = false;

                var agg = GetOrCreateRelicAggregateLocked(AkabekoRelicId);
                agg.VigorGained += amount;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordAkabekoVigorGained failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record Gremlin Horn's owner-specific enemy-death activation and arm
    /// one-shot attribution windows for the resource effects it immediately
    /// performs. Energy is measured at the player energy mutation point; cards
    /// drawn are measured from <c>CardPileCmd.Draw</c>'s returned cards.
    /// </summary>
    public static void ArmGremlinHornAttribution(Player owner)
    {
        if (owner == null) return;

        lock (_lock)
        {
            try
            {
                _pendingCombat ??= new PendingCombat();
                var agg = GetOrCreateRelicAggregateLocked(GremlinHornRelicId);
                agg.Activations += 1;
                int hc = CurrentHistoryCountLocked();
                // Owner-keyed one-shot energy + draw windows (resolve async after
                // AfterDeath returns, so maxHistoryAdvance=-1).
                _pendingCombat.Windows.Arm(GremlinHornRelicId, AttributionEventKind.PlayerEnergyGain,
                    hc, ownerId: owner, maxHistoryAdvance: -1);
                _pendingCombat.Windows.Arm(GremlinHornRelicId, AttributionEventKind.CardDraw,
                    hc, ownerId: owner, maxHistoryAdvance: -1);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"ArmGremlinHornAttribution failed: {e.Message}");
            }
        }
    }

    /// <summary>Single arbitration point for player energy gains at
    /// PlayerCombatState.GainEnergy. If a relic energy window (Gremlin Horn /
    /// Happy Flower / Booming Conch) is live for this player it claims the
    /// delta; otherwise the resolving card play is credited. Replaces the
    /// un-arbitrated fan-out and closes the Gremlin-Horn card+relic double count.</summary>
    public static void DispatchPlayerEnergyGain(PlayerCombatState combatState, int amount)
    {
        if (amount <= 0 || combatState == null) return;
        lock (_lock)
        {
            try
            {
                // Co-op: a partner's energy gain is not ours.
                if (!IsTrackedPlayer(combatState._player)) return;
                if (_pendingCombat == null) { RecordEnergyGained(combatState, amount); return; }
                var owner = combatState._player;
                var key = _pendingCombat.Windows.TryConsume(
                    AttributionEventKind.PlayerEnergyGain, CurrentHistoryCountLocked(), ownerId: owner);
                if (key != null)
                {
                    var agg = GetOrCreateRelicAggregateLocked(key);
                    // Relic energy windows all use EnergyGenerated; the window
                    // key identifies the specific relic aggregate.
                    agg.EnergyGenerated += amount;
                    return;
                }
                // No relic window: credit the resolving card play as before.
                RecordEnergyGained(combatState, amount);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"DispatchPlayerEnergyGain failed: {e.Message}");
            }
        }
    }

    public static bool TryConsumeGremlinHornDrawAttribution(Player player)
    {
        if (player == null) return false;

        lock (_lock)
        {
            try
            {
                if (_pendingCombat == null) return false;
                bool claimed = _pendingCombat.Windows.TryConsume(
                    AttributionEventKind.CardDraw, CurrentHistoryCountLocked(), ownerId: player) == GremlinHornRelicId;
                if (claimed)
                    _pendingCombat.Windows.Disarm(GremlinHornRelicId, AttributionEventKind.PlayerEnergyGain, ownerId: player);
                return claimed;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"TryConsumeGremlinHornDrawAttribution failed: {e.Message}");
                return false;
            }
        }
    }

    public static void RecordGremlinHornCardsDrawn(int cardsDrawn)
    {
        if (cardsDrawn <= 0) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(GremlinHornRelicId);
                agg.AdditionalCardsDrawn += cardsDrawn;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordGremlinHornCardsDrawn failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record Pendulum's owner-specific every-N-turns activation. The actual
    /// number of cards drawn is observed from the draw command result.
    /// </summary>
    public static void ArmPendulumAttribution(Player owner)
    {
        if (owner == null) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(PendulumRelicId);
                agg.Activations += 1;
                _pendingPendulumDrawAttributions.Add(owner);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"ArmPendulumAttribution failed: {e.Message}");
            }
        }
    }

    public static bool TryConsumePendulumDrawAttribution(Player player)
    {
        if (player == null) return false;

        lock (_lock)
        {
            try
            {
                return ConsumePendingGremlinHornAttribution(_pendingPendulumDrawAttributions, player);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"TryConsumePendulumDrawAttribution failed: {e.Message}");
                return false;
            }
        }
    }

    public static void RecordPendulumCardsDrawn(int cardsDrawn)
    {
        if (cardsDrawn <= 0) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(PendulumRelicId);
                agg.AdditionalCardsDrawn += cardsDrawn;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordPendulumCardsDrawn failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record Centennial Puzzle's once-per-combat HP-loss activation. The
    /// actual cards drawn are observed from the single-card draw command.
    /// </summary>
    public static void ArmCentennialPuzzleAttribution(Player owner, int expectedDraws)
    {
        if (owner == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(owner)) return;

                _pendingCombat ??= new PendingCombat();
                var agg = GetOrCreateRelicAggregateLocked(CentennialPuzzleRelicId);
                agg.Activations += 1;

                if (expectedDraws <= 0) return;
                _pendingCombat.CentennialPuzzleDrawsRemaining[owner] =
                    _pendingCombat.CentennialPuzzleDrawsRemaining.TryGetValue(owner, out var existing)
                        ? existing + expectedDraws
                        : expectedDraws;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"ArmCentennialPuzzleAttribution failed: {e.Message}");
            }
        }
    }

    public static bool TryConsumeCentennialPuzzleDrawAttribution(Player player)
    {
        if (player == null) return false;

        lock (_lock)
        {
            try
            {
                if (_pendingCombat == null) return false;
                if (!_pendingCombat.CentennialPuzzleDrawsRemaining.TryGetValue(player, out var remaining)
                    || remaining <= 0)
                {
                    return false;
                }

                remaining -= 1;
                if (remaining <= 0)
                    _pendingCombat.CentennialPuzzleDrawsRemaining.Remove(player);
                else
                    _pendingCombat.CentennialPuzzleDrawsRemaining[player] = remaining;
                return true;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"TryConsumeCentennialPuzzleDrawAttribution failed: {e.Message}");
                return false;
            }
        }
    }

    public static void DisarmCentennialPuzzleAttribution(Player player)
    {
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                _pendingCombat?.CentennialPuzzleDrawsRemaining.Remove(player);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"DisarmCentennialPuzzleAttribution failed: {e.Message}");
            }
        }
    }

    public static void RecordCentennialPuzzleCardsDrawn(int cardsDrawn)
    {
        if (cardsDrawn <= 0) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(CentennialPuzzleRelicId);
                agg.AdditionalCardsDrawn += cardsDrawn;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordCentennialPuzzleCardsDrawn failed: {e.Message}");
            }
        }
    }

    internal static void RecordCentennialPuzzleStatsForTest(
        RelicAggregate agg,
        int activations,
        int cardsDrawn)
    {
        if (agg == null) return;
        agg.Activations += Math.Max(0, activations);
        agg.AdditionalCardsDrawn += Math.Max(0, cardsDrawn);
    }

    /// <summary>
    /// Record Parrying Shield's owner-specific end-of-turn activation. The
    /// damage split is observed from the damage command result.
    /// </summary>
    public static void ArmParryingShieldAttribution(Creature dealer)
    {
        if (dealer == null) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(ParryingShieldRelicId);
                agg.Activations += 1;
                _pendingParryingShieldDamageAttributions.Add(dealer);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"ArmParryingShieldAttribution failed: {e.Message}");
            }
        }
    }

    public static bool TryConsumeParryingShieldDamageAttribution(Creature dealer)
    {
        if (dealer == null) return false;

        lock (_lock)
        {
            try
            {
                return ConsumePendingCreatureAttribution(_pendingParryingShieldDamageAttributions, dealer);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"TryConsumeParryingShieldDamageAttribution failed: {e.Message}");
                return false;
            }
        }
    }

    public static void RecordParryingShieldDamage(IEnumerable<DamageResult>? results)
    {
        if (results == null) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(ParryingShieldRelicId);
                AddRelicDamageResultsLocked(agg, results);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordParryingShieldDamage failed: {e.Message}");
            }
        }
    }

    internal static void RecordParryingShieldDamageForTest(RelicAggregate agg, IEnumerable<DamageResult> results)
    {
        AddRelicDamageResultsLocked(agg, results);
    }

    /// <summary>
    /// Record Festive Popper's owner-specific first-turn activation. The
    /// damage split is observed from the damage command result.
    /// </summary>
    public static void ArmFestivePopperAttribution(Creature dealer)
    {
        if (dealer == null) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(FestivePopperRelicId);
                agg.Activations += 1;
                _pendingFestivePopperDamageAttributions.Add(dealer);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"ArmFestivePopperAttribution failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record Mercury Hourglass's owner-specific turn-start activation. The
    /// damage split is observed from the damage command result. Activations are
    /// counted once per combat so damage-per-combat has the expected denominator.
    /// </summary>
    public static void ArmMercuryHourglassAttribution(Creature dealer)
    {
        if (dealer == null) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(MercuryHourglassRelicId);
                if (agg.Activations <= 0) agg.Activations = 1;
                _pendingMercuryHourglassDamageAttributions.Add(dealer);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"ArmMercuryHourglassAttribution failed: {e.Message}");
            }
        }
    }

    public static bool TryConsumeFestivePopperDamageAttribution(Creature dealer)
    {
        if (dealer == null) return false;

        lock (_lock)
        {
            try
            {
                return ConsumePendingCreatureAttribution(_pendingFestivePopperDamageAttributions, dealer);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"TryConsumeFestivePopperDamageAttribution failed: {e.Message}");
                return false;
            }
        }
    }

    public static bool TryConsumeMercuryHourglassDamageAttribution(Creature dealer)
    {
        if (dealer == null) return false;

        lock (_lock)
        {
            try
            {
                return ConsumePendingCreatureAttribution(_pendingMercuryHourglassDamageAttributions, dealer);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"TryConsumeMercuryHourglassDamageAttribution failed: {e.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Record Mr. Struggles's owner-specific turn-start activation. The damage
    /// amount scales with turn number; the result split still comes from the
    /// resolved damage command.
    /// </summary>
    public static void ArmMrStrugglesAttribution(Creature dealer)
    {
        if (dealer == null) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(MrStrugglesRelicId);
                agg.Activations += 1;
                _pendingMrStrugglesDamageAttributions.Add(dealer);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"ArmMrStrugglesAttribution failed: {e.Message}");
            }
        }
    }

    public static bool TryConsumeMrStrugglesDamageAttribution(Creature dealer)
    {
        if (dealer == null) return false;

        lock (_lock)
        {
            try
            {
                return ConsumePendingCreatureAttribution(_pendingMrStrugglesDamageAttributions, dealer);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"TryConsumeMrStrugglesDamageAttribution failed: {e.Message}");
                return false;
            }
        }
    }

    public static void RecordFestivePopperDamage(IEnumerable<DamageResult>? results)
    {
        if (results == null) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(FestivePopperRelicId);
                AddRelicDamageResultsLocked(agg, results);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordFestivePopperDamage failed: {e.Message}");
            }
        }
    }

    public static void RecordMrStrugglesDamage(IEnumerable<DamageResult>? results)
    {
        if (results == null) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(MrStrugglesRelicId);
                AddRelicDamageResultsLocked(agg, results);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordMrStrugglesDamage failed: {e.Message}");
            }
        }
    }

    public static void RecordMercuryHourglassDamage(IEnumerable<DamageResult>? results)
    {
        if (results == null) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(MercuryHourglassRelicId);
                AddRelicDamageResultsLocked(agg, results);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordMercuryHourglassDamage failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record the observed Thorns amount contributed by Bronze Scales after
    /// the relic's room-entry application completes. The later Thorns damage
    /// command reports one combined amount, so this per-power contribution is
    /// used to credit only Bronze Scales' share if other Thorns sources stack.
    /// </summary>
    public static void RecordBronzeScalesThornsContribution(ThornsPower? thornsPower, int amount)
    {
        if (thornsPower == null || amount <= 0) return;

        lock (_lock)
        {
            try
            {
                if (!_bronzeScalesThornsContributions.TryAdd(thornsPower, amount))
                    _bronzeScalesThornsContributions[thornsPower] += amount;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordBronzeScalesThornsContribution failed: {e.Message}");
            }
        }
    }

    public static void ArmBronzeScalesThornsDamageAttribution(
        ThornsPower? thornsPower,
        Creature? thornsOwner,
        Creature? damageTarget)
    {
        if (thornsPower == null || thornsOwner == null || damageTarget == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayerCreature(thornsOwner)) return;
                if (!_bronzeScalesThornsContributions.TryGetValue(thornsPower, out int bronzeAmount)) return;
                if (bronzeAmount <= 0 || thornsPower.Amount <= 0) return;

                _pendingBronzeScalesDamageAttributions.Add(new PendingBronzeScalesDamageAttribution(
                    thornsOwner,
                    damageTarget,
                    thornsPower.Amount,
                    Math.Min(bronzeAmount, thornsPower.Amount)));
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"ArmBronzeScalesThornsDamageAttribution failed: {e.Message}");
            }
        }
    }

    internal static bool TryConsumeBronzeScalesThornsDamageAttribution(
        Creature? damageTarget,
        decimal totalAmount,
        Creature? thornsOwner,
        out decimal attributedAmount)
    {
        attributedAmount = 0m;
        if (damageTarget == null || thornsOwner == null || totalAmount <= 0m) return false;

        lock (_lock)
        {
            try
            {
                for (int i = 0; i < _pendingBronzeScalesDamageAttributions.Count; i++)
                {
                    var pending = _pendingBronzeScalesDamageAttributions[i];
                    if (!ReferenceEquals(pending.DamageTarget, damageTarget)) continue;
                    if (!ReferenceEquals(pending.ThornsOwner, thornsOwner)) continue;
                    if (!AreClose(pending.TotalAmount, totalAmount)) continue;

                    _pendingBronzeScalesDamageAttributions.RemoveAt(i);
                    attributedAmount = pending.AttributedAmount;
                    return attributedAmount > 0m;
                }
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"TryConsumeBronzeScalesThornsDamageAttribution failed: {e.Message}");
            }
        }

        return false;
    }

    public static void RecordBronzeScalesDamage(
        IEnumerable<DamageResult>? results,
        decimal totalAmount,
        decimal attributedAmount)
    {
        if (results == null || totalAmount <= 0m || attributedAmount <= 0m) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(BronzeScalesRelicId);
                agg.Activations += 1;
                AddAttributedRelicDamageResultsLocked(agg, results, totalAmount, attributedAmount);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordBronzeScalesDamage failed: {e.Message}");
            }
        }
    }

    internal static void RecordFestivePopperDamageForTest(
        RelicAggregate agg,
        IEnumerable<(int BlockedDamage, int UnblockedDamage, int OverkillDamage, bool WasTargetKilled)> results)
    {
        foreach (var result in results)
        {
            AddRelicDamageResultPartsLocked(
                agg,
                result.BlockedDamage,
                result.UnblockedDamage,
                result.OverkillDamage,
                result.WasTargetKilled);
        }
    }

    internal static void RecordMercuryHourglassDamageForTest(
        RelicAggregate agg,
        IEnumerable<(int BlockedDamage, int UnblockedDamage, int OverkillDamage, bool WasTargetKilled)> results)
    {
        foreach (var result in results)
        {
            AddRelicDamageResultPartsLocked(
                agg,
                result.BlockedDamage,
                result.UnblockedDamage,
                result.OverkillDamage,
                result.WasTargetKilled);
        }
    }

    internal static void RecordMrStrugglesDamageForTest(
        RelicAggregate agg,
        IEnumerable<(int BlockedDamage, int UnblockedDamage, int OverkillDamage, bool WasTargetKilled)> results)
    {
        foreach (var result in results)
        {
            AddRelicDamageResultPartsLocked(
                agg,
                result.BlockedDamage,
                result.UnblockedDamage,
                result.OverkillDamage,
                result.WasTargetKilled);
        }
    }

    internal static void RecordBronzeScalesDamageForTest(
        RelicAggregate agg,
        IEnumerable<(int BlockedDamage, int UnblockedDamage, int OverkillDamage, bool WasTargetKilled)> results,
        decimal totalAmount,
        decimal attributedAmount)
    {
        AddAttributedRelicDamageResultPartsLocked(agg, results, totalAmount, attributedAmount);
    }

    private static void AddRelicDamageResultsLocked(RelicAggregate agg, IEnumerable<DamageResult> results)
    {
        foreach (var result in results)
        {
            if (result == null) continue;
            AddRelicDamageResultPartsLocked(
                agg,
                result.BlockedDamage,
                result.UnblockedDamage,
                result.OverkillDamage,
                result.WasTargetKilled);
        }
    }

    private static void AddAttributedRelicDamageResultsLocked(
        RelicAggregate agg,
        IEnumerable<DamageResult> results,
        decimal totalAmount,
        decimal attributedAmount)
    {
        foreach (var result in results)
        {
            if (result == null) continue;
            AddAttributedRelicDamageResultPartsLocked(
                agg,
                result.BlockedDamage,
                result.UnblockedDamage,
                result.OverkillDamage,
                result.WasTargetKilled,
                totalAmount,
                attributedAmount);
        }
    }

    private static void AddAttributedRelicDamageResultPartsLocked(
        RelicAggregate agg,
        IEnumerable<(int BlockedDamage, int UnblockedDamage, int OverkillDamage, bool WasTargetKilled)> results,
        decimal totalAmount,
        decimal attributedAmount)
    {
        foreach (var result in results)
        {
            AddAttributedRelicDamageResultPartsLocked(
                agg,
                result.BlockedDamage,
                result.UnblockedDamage,
                result.OverkillDamage,
                result.WasTargetKilled,
                totalAmount,
                attributedAmount);
        }
    }

    private static void AddAttributedRelicDamageResultPartsLocked(
        RelicAggregate agg,
        int blockedDamage,
        int unblockedDamage,
        int overkillDamage,
        bool wasTargetKilled,
        decimal totalAmount,
        decimal attributedAmount)
    {
        if (totalAmount <= 0m || attributedAmount <= 0m) return;
        if (attributedAmount >= totalAmount)
        {
            AddRelicDamageResultPartsLocked(agg, blockedDamage, unblockedDamage, overkillDamage, wasTargetKilled);
            return;
        }

        decimal ratio = attributedAmount / totalAmount;
        AddRelicDamageResultPartsLocked(
            agg,
            ScaleDamageComponent(blockedDamage, ratio),
            ScaleDamageComponent(unblockedDamage, ratio),
            ScaleDamageComponent(overkillDamage, ratio),
            wasTargetKilled);
    }

    private static int ScaleDamageComponent(int amount, decimal ratio)
    {
        if (amount <= 0 || ratio <= 0m) return 0;
        return (int)Math.Round(amount * ratio, MidpointRounding.AwayFromZero);
    }

    private static void AddRelicDamageResultPartsLocked(
        RelicAggregate agg,
        int blockedDamage,
        int unblockedDamage,
        int overkillDamage,
        bool wasTargetKilled)
    {
        var damageTotals = ComputeEnemyDamageTotals(blockedDamage, unblockedDamage, overkillDamage);
        agg.TotalDamageAttempted += damageTotals.IntendedDamage;
        agg.TotalDamageDealt += damageTotals.EffectiveDamage;
        agg.TotalDamageBlocked += blockedDamage;
        agg.TotalDamageOverkill += overkillDamage;
        agg.TotalTargets += 1;
        if (wasTargetKilled) agg.Kills += 1;
    }

    /// <summary>
    /// Record Horn Cleat's owner-specific second-turn block-clear trigger.
    /// The actual block gained is observed from the gain-block command result.
    /// </summary>
    public static void ArmHornCleatAttribution(Creature creature)
    {
        if (creature == null) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(HornCleatRelicId);
                agg.Activations += 1;
                _pendingHornCleatBlockAttributions.Add(creature);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"ArmHornCleatAttribution failed: {e.Message}");
            }
        }
    }

    public static bool TryConsumeHornCleatBlockAttribution(Creature creature)
    {
        if (creature == null) return false;

        lock (_lock)
        {
            try
            {
                return ConsumePendingCreatureAttribution(_pendingHornCleatBlockAttributions, creature);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"TryConsumeHornCleatBlockAttribution failed: {e.Message}");
                return false;
            }
        }
    }

    public static void RecordHornCleatBlockGained(decimal amount)
    {
        if (amount <= 0m) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(HornCleatRelicId);
                agg.AdditionalBlockGained += (int)amount;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordHornCleatBlockGained failed: {e.Message}");
            }
        }
    }

    private static bool ConsumePendingGremlinHornAttribution(List<Player> pendingPlayers, Player? player)
    {
        if (player == null) return false;

        for (int i = 0; i < pendingPlayers.Count; i++)
        {
            if (!ReferenceEquals(pendingPlayers[i], player)) continue;
            pendingPlayers.RemoveAt(i);
            return true;
        }

        return false;
    }

    private static bool ConsumePendingCreatureAttribution(List<Creature> pending, Creature creature)
    {
        for (int i = 0; i < pending.Count; i++)
        {
            if (!ReferenceEquals(pending[i], creature)) continue;
            pending.RemoveAt(i);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Record Prismatic Gem's +1 max-energy contribution once per player
    /// energy reset. The relic modifies max energy whenever the game queries
    /// it, so this is tied to the actual reset hook instead of the modifier
    /// method to avoid counting UI/query calls as generated energy.
    /// </summary>
    public static void RecordPrismaticGemEnergyGenerated(
        MegaCrit.Sts2.Core.Combat.ICombatState combatState,
        Player player,
        int amount)
    {
        RecordEnergyResetRelicEnergyGenerated(
            PrismaticGemRelicId,
            combatState,
            player,
            amount,
            countRoundOneCombat: false);
    }

    public static void RecordBloodSoakedRoseEnergyGenerated(
        MegaCrit.Sts2.Core.Combat.ICombatState combatState,
        Player player,
        string? relicId,
        int amount)
    {
        RecordEnergyResetRelicEnergyGenerated(
            string.IsNullOrWhiteSpace(relicId) ? BloodSoakedRoseRelicId : relicId!,
            combatState,
            player,
            amount,
            countRoundOneCombat: true);
    }

    private static void RecordEnergyResetRelicEnergyGenerated(
        string relicId,
        MegaCrit.Sts2.Core.Combat.ICombatState combatState,
        Player player,
        int amount,
        bool countRoundOneCombat)
    {
        if (amount <= 0 || combatState == null || player == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(player)) return;

                int roundNumber = combatState.RoundNumber;
                var dedupeKey = $"{relicId}|{player.NetId}";
                if (_lastEnergyResetRoundByRelicAndPlayer.TryGetValue(dedupeKey, out var lastRoundNumber)
                    && lastRoundNumber == roundNumber)
                {
                    return;
                }

                _lastEnergyResetRoundByRelicAndPlayer[dedupeKey] = roundNumber;

                var agg = GetOrCreateRelicAggregateLocked(relicId);
                agg.EnergyGenerated += amount;
                if (countRoundOneCombat && roundNumber == 1)
                    agg.Activations += 1;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordEnergyResetRelicEnergyGenerated failed: {e.Message}");
            }
        }
    }

    internal static void RecordEnergyResetRelicEnergyGeneratedForTest(
        RelicAggregate agg,
        int amount,
        bool countCombat)
    {
        if (agg == null || amount <= 0) return;

        agg.EnergyGenerated += amount;
        if (countCombat)
            agg.Activations += 1;
    }

    /// <summary>
    /// Record that Prismatic Gem modified one card reward's creation options.
    /// Card rewards are created outside combat, so persist directly to the
    /// committed run instead of waiting for a combat boundary.
    /// </summary>
    public static void RecordPrismaticGemCardRewardAffected()
    {
        lock (_lock)
        {
            try
            {
                EnsureLazyCurrentRunLocked();

                if (!_currentRun.RelicAggregates.TryGetValue(PrismaticGemRelicId, out var agg))
                {
                    agg = new RelicAggregate();
                    _currentRun.RelicAggregates[PrismaticGemRelicId] = agg;
                }

                agg.CardRewardsAffected += 1;
                _currentRun.UpdatedAt = Now();
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordPrismaticGemCardRewardAffected failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record observed card reward options by pool/category while Prismatic Gem
    /// is owned. This is meta rather than sole relic attribution: another relic
    /// may also have participated in producing the final visible options.
    /// </summary>
    public static void RecordPrismaticGemObservedCardRewardCategories(IEnumerable<CardRewardCategoryObservation> categories)
    {
        lock (_lock)
        {
            try
            {
                EnsureLazyCurrentRunLocked();

                if (!_currentRun.RelicAggregates.TryGetValue(PrismaticGemRelicId, out var agg))
                {
                    agg = new RelicAggregate();
                    _currentRun.RelicAggregates[PrismaticGemRelicId] = agg;
                }

                foreach (var category in categories)
                {
                    AddCardRewardCategory(agg.CardRewardCategories, category.Key, category.DisplayName, 1);
                }

                _currentRun.UpdatedAt = Now();
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordPrismaticGemObservedCardRewardCategories failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Arm the flag that attributes player block gains to Cloak Clasp.
    /// Called from <see cref="Patches.CloakClaspBeforeTurnEndPatch"/> when
    /// Cloak Clasp's end-of-turn hook fires on the player's side. The registry
    /// window closes on consumption or the combat boundary.
    /// </summary>
    public static void ArmCloakClaspBlockAttribution()
    {
        lock (_lock)
        {
            _pendingCombat ??= new PendingCombat();
            // Bug fix (#250): Cloak Clasp does exactly ONE GainBlock of size
            // (cards-in-hand * 1) (verified CloakClasp.BeforeSideTurnEnd), NOT
            // one-per-card. One-shot window; no hand-count gate (a zero-hand
            // end-of-turn simply never fires AfterBlockGained, and the window
            // self-clears at the combat boundary).
            _pendingCombat.Windows.Arm(CloakClaspRelicId, AttributionEventKind.PlayerBlockGain,
                CurrentHistoryCountLocked(), maxHistoryAdvance: -1);
        }
    }

    /// <summary>
    /// No-op safety reset kept wired at <c>Hook.AfterSideTurnEnd</c>. Cloak Clasp's
    /// attribution now lives entirely in the per-combat registry (see
    /// <see cref="ArmCloakClaspBlockAttribution"/>); the window closes on
    /// consumption or the combat boundary. Routing this to Windows.Disarm(...)
    /// — an explicit early close after the relic's block grants resolve —
    /// changes live window-close timing, so it's a deferred, live-verified
    /// follow-up (#257). Kept wired so that follow-up has a seam.
    /// </summary>
    public static void DisarmCloakClaspBlockAttribution()
    {
    }

    /// <summary>Single arbitration point for player block gains at
    /// Hook.AfterBlockGained. The oldest fresh armed PlayerBlockGain window
    /// claims the gain; that relic's AdditionalBlockGained is credited.
    /// Two distinct gains in one turn (Orichalcum + Cloak Clasp) are two
    /// AfterBlockGained events, each claiming its own window in FIFO order.</summary>
    public static void DispatchPlayerBlockGain(int amount)
    {
        if (amount <= 0) return;
        lock (_lock)
        {
            try
            {
                if (_pendingCombat == null) return;
                var key = _pendingCombat.Windows.TryConsume(
                    AttributionEventKind.PlayerBlockGain, CurrentHistoryCountLocked());
                if (key == null) return;
                var agg = GetOrCreateRelicAggregateLocked(key);
                if (key == AnchorRelicId) agg.Activations += 1; // preserve Anchor activation count
                agg.AdditionalBlockGained += amount;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"DispatchPlayerBlockGain failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Return the committed relic aggregate for a relic id, merged with any
    /// pending combat data. Used by the relic tooltip to show current-run stats.
    /// </summary>
    public static RelicAggregate? GetRelicAggregate(string relicId)
    {
        lock (_lock)
        {
            RecordHeldCombatRelicBaselinesForTrackedPlayerLocked();

            RelicAggregate? result = null;

            if (_currentRun != null && _currentRun.RelicAggregates.TryGetValue(relicId, out var committed))
            {
                // new + Merge instead of a parallel clone — one RelicAggregate
                // field list lives in MergeRelicAggregateInto.
                result = new RelicAggregate();
                MergeRelicAggregateInto(result, committed);
            }

            if (_pendingCombat != null && _pendingCombat.RelicAggregates.TryGetValue(relicId, out var pending))
            {
                result ??= new RelicAggregate();
                MergeRelicAggregateInto(result, pending);
            }

            return result;
        }
    }

    internal static RelicAggregate? GetLastEndedRelicAggregate(string relicId)
    {
        lock (_lock)
        {
            if (_lastEndedRun == null) return null;
            if (!_lastEndedRun.RelicAggregates.TryGetValue(relicId, out var saved)) return null;

            var result = new RelicAggregate();
            MergeRelicAggregateInto(result, saved);
            return result;
        }
    }

    internal static CardAggregate? GetLastEndedPooledCardAggregateByDefinition(string definitionId)
    {
        lock (_lock)
        {
            if (_lastEndedRun == null) return null;
            return CardAggregatePooler.PoolByDefinition(_lastEndedRun.Aggregates, definitionId);
        }
    }

    internal static int? GetLastEndedFloorForRateStats()
    {
        lock (_lock)
        {
            if (_lastEndedRun?.FloorReached == null) return null;
            return Math.Max(1, _lastEndedRun.FloorReached.Value);
        }
    }

    internal static bool TryLoadLastEndedRunForCurrentGameStartTime()
    {
        long gameStartTime;
        try { gameStartTime = RunManager.Instance._startTime; }
        catch (Exception e)
        {
            CoreMain.LogDebug($"TryLoadLastEndedRunForCurrentGameStartTime: couldn't read _startTime: {e.Message}");
            return false;
        }

        if (gameStartTime == 0) return false;

        lock (_lock)
        {
            if (_lastEndedRun?.GameStartTime == gameStartTime
                && _lastEndedRun.Outcome != "in_progress")
                return true;
        }

        var loaded = RunStorage.FindHistoricalByGameStartTime(gameStartTime);
        if (loaded?.Data == null || loaded.Data.Outcome == "in_progress")
            return false;

        lock (_lock)
        {
            _lastEndedRun = loaded.Data;
        }

        CoreMain.LogDebug(
            $"TryLoadLastEndedRunForCurrentGameStartTime: loaded ended run '{loaded.Data.RunId}' for game_start_time={gameStartTime}");
        return true;
    }

    internal static void SetLastEndedRunForTest(RunData? run)
    {
        lock (_lock)
        {
            _lastEndedRun = run;
        }
    }

    public static int GetCurrentFloorForRateStats()
    {
        lock (_lock)
        {
            return Math.Max(1, CurrentRunFloorLocked() ?? 1);
        }
    }

    public static EnemyAggregate? GetEnemyAggregate(string enemyId)
    {
        lock (_lock)
        {
            EnemyAggregate? result = null;

            if (_currentRun != null && _currentRun.EnemyAggregates.TryGetValue(enemyId, out var committed))
            {
                result = CloneEnemyAggregate(committed);
            }

            if (_pendingCombat != null && _pendingCombat.EnemyAggregates.TryGetValue(enemyId, out var pending))
            {
                result ??= new EnemyAggregate();
                MergeEnemyAggregateInto(result, pending);
            }

            return result;
        }
    }

    private static RelicAggregate GetOrCreateRelicAggregateLocked(string relicId)
    {
        _pendingCombat ??= new PendingCombat();
        RecordHeldCombatRelicBaselinesForTrackedPlayerLocked();
        return GetOrCreatePendingRelicAggregateLocked(relicId);
    }

    private static RelicAggregate GetOrCreatePendingRelicAggregateLocked(string relicId)
    {
        _pendingCombat ??= new PendingCombat();
        if (!_pendingCombat.RelicAggregates.TryGetValue(relicId, out var agg))
        {
            agg = new RelicAggregate();
            _pendingCombat.RelicAggregates[relicId] = agg;
        }

        return agg;
    }

    private static EnemyAggregate GetOrCreateEnemyAggregateLocked(string enemyId)
    {
        _pendingCombat ??= new PendingCombat();
        if (!_pendingCombat.EnemyAggregates.TryGetValue(enemyId, out var agg))
        {
            agg = new EnemyAggregate { EnemyId = enemyId };
            _pendingCombat.EnemyAggregates[enemyId] = agg;
        }

        return agg;
    }

    private static void RecordEnemyStatusCardAddedLocked(
        EnemyAggregate agg,
        string cardId,
        string displayName,
        PileType pileType)
    {
        agg.StatusCardsAdded++;
        switch (pileType)
        {
            case PileType.Hand:
                agg.StatusCardsAddedToHand++;
                break;
            case PileType.Draw:
                agg.StatusCardsAddedToDraw++;
                break;
            case PileType.Discard:
                agg.StatusCardsAddedToDiscard++;
                break;
            case PileType.Deck:
                agg.StatusCardsAddedToDeck++;
                break;
        }

        if (!agg.StatusCardsById.TryGetValue(cardId, out var cardAgg))
        {
            cardAgg = new EnemyStatusCardAggregate
            {
                CardId = cardId,
                DisplayName = displayName,
            };
            agg.StatusCardsById[cardId] = cardAgg;
        }

        cardAgg.Count++;
    }

    // new + Merge (target starts empty, so Merge copies identity and
    // accumulates every stat) — one EnemyAggregate field list to maintain.
    internal static EnemyAggregate CloneEnemyAggregate(EnemyAggregate source)
    {
        var clone = new EnemyAggregate();
        MergeEnemyAggregateInto(clone, source);
        return clone;
    }

    internal static void MergeEnemyAggregateInto(EnemyAggregate target, EnemyAggregate source)
    {
        if (string.IsNullOrWhiteSpace(target.EnemyId))
            target.EnemyId = source.EnemyId;
        if (string.IsNullOrWhiteSpace(target.DisplayName))
            target.DisplayName = source.DisplayName;
        target.DamageInstances += source.DamageInstances;
        target.DamageAttempted += source.DamageAttempted;
        target.DamageDealt += source.DamageDealt;
        target.DamageBlocked += source.DamageBlocked;
        target.StatusCardsAdded += source.StatusCardsAdded;
        target.StatusCardsAddedToHand += source.StatusCardsAddedToHand;
        target.StatusCardsAddedToDraw += source.StatusCardsAddedToDraw;
        target.StatusCardsAddedToDiscard += source.StatusCardsAddedToDiscard;
        target.StatusCardsAddedToDeck += source.StatusCardsAddedToDeck;
        MergeEnemyStatusCardBreakdownInto(target, source);
    }

    private static void MergeEnemyStatusCardBreakdownInto(EnemyAggregate target, EnemyAggregate source)
    {
        if (source.StatusCardsById == null || source.StatusCardsById.Count == 0) return;

        foreach (var sourceCard in source.StatusCardsById.Values)
        {
            if (sourceCard.Count <= 0 || string.IsNullOrWhiteSpace(sourceCard.CardId)) continue;
            if (!target.StatusCardsById.TryGetValue(sourceCard.CardId, out var targetCard))
            {
                targetCard = new EnemyStatusCardAggregate
                {
                    CardId = sourceCard.CardId,
                    DisplayName = sourceCard.DisplayName,
                };
                target.StatusCardsById[sourceCard.CardId] = targetCard;
            }

            targetCard.Count += sourceCard.Count;
            if (string.IsNullOrWhiteSpace(targetCard.DisplayName))
                targetCard.DisplayName = sourceCard.DisplayName;
        }
    }

    internal static string FormatCardIdForDisplay(string cardId)
    {
        var value = cardId;
        const string prefix = "CARD.";
        if (value.StartsWith(prefix, StringComparison.Ordinal))
            value = value[prefix.Length..];

        return string.Join(" ", value
            .Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Length == 0
                ? part
                : char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));
    }

    private static RelicAggregate GetOrCreateRelicAggregateForCurrentContextLocked(string relicId)
    {
        if (_pendingCombat != null)
        {
            RecordHeldCombatRelicBaselinesForTrackedPlayerLocked();
            return GetOrCreatePendingRelicAggregateLocked(relicId);
        }

        return GetOrCreateCurrentRunRelicAggregateLocked(relicId);
    }

    private static RelicAggregate GetOrCreateRelicAggregateForHealingLocked(PendingRelicHealing pending)
    {
        // NOT GetOrCreateRelicAggregateLocked: that does `_pendingCombat ??= new`,
        // which resurrects an orphan buffer when this healing finalize runs on a
        // pool thread AFTER OnCombatEnded already promoted and nulled the buffer.
        // The orphan is never promoted, so the lost-healing tail was silently
        // dropped on a run's final victorious combat. ForCurrentContext routes to
        // the committed run aggregate when there is no live pending combat.
        return pending.PersistDirectlyToRun
            ? GetOrCreateCurrentRunRelicAggregateLocked(pending.RelicId)
            : GetOrCreateRelicAggregateForCurrentContextLocked(pending.RelicId);
    }

    private static RelicAggregate GetOrCreateCurrentRunRelicAggregateLocked(string relicId)
    {
        EnsureLazyCurrentRunLocked();

        if (!_currentRun.RelicAggregates.TryGetValue(relicId, out var agg))
        {
            agg = new RelicAggregate();
            _currentRun.RelicAggregates[relicId] = agg;
        }

        _currentRun.UpdatedAt = Now();
        return agg;
    }

    private static void RefreshCurrentRunMetadataLocked()
    {
        if (_currentRun == null) return;

        try
        {
            var runState = RunManager.Instance?.State;
            if (runState == null) return;

            _currentRun.FloorReached = runState.TotalFloor;
            _currentRun.Ascension ??= runState.AscensionLevel;
            _currentRun.Character ??= runState.Players.FirstOrDefault()?.Character?.Id.ToString();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RefreshCurrentRunMetadataLocked failed: {e.Message}");
        }
    }

    private static int? CurrentRunFloorLocked()
    {
        try
        {
            var runState = RunManager.Instance?.State;
            if (runState?.TotalFloor > 0) return runState.TotalFloor;
        }
        catch
        {
            // Fall back to the persisted run metadata below.
        }

        return _currentRun?.FloorReached;
    }

    private static void RecordStrikeDummyStrikePlayedIfOwnedLocked(CardModel card)
    {
        if (!IsStrikeDummyStrikeCard(card)) return;

        var owner = card.Owner;
        if (owner == null || !IsTrackedPlayer(owner) || !PlayerHasStrikeDummy(owner)) return;

        var agg = GetOrCreateRelicAggregateForCurrentContextLocked(StrikeDummyRelicId);
        agg.StrikeDummyStrikesPlayed += 1;
    }

    private static void RecordMiniatureCannonUpgradedAttackPlayedIfOwnedLocked(CardModel card)
    {
        if (!IsMiniatureCannonUpgradedAttackCard(card)) return;

        var owner = card.Owner;
        if (owner == null || !IsTrackedPlayer(owner) || !PlayerHasMiniatureCannon(owner)) return;

        var agg = GetOrCreateRelicAggregateForCurrentContextLocked(MiniatureCannonRelicId);
        agg.MiniatureCannonUpgradedAttackPlays += 1;
    }

    private static void RecordMiniatureCannonUpgradedAttackHitIfOwnedLocked(CardModel card)
    {
        if (!IsMiniatureCannonUpgradedAttackCard(card)) return;

        var owner = card.Owner;
        if (owner == null || !IsTrackedPlayer(owner) || !PlayerHasMiniatureCannon(owner)) return;

        var agg = GetOrCreateRelicAggregateLocked(MiniatureCannonRelicId);
        agg.MiniatureCannonUpgradedAttackHits += 1;
    }

    private static bool RefreshStrikeDummyDeckCountsIfOwnedLocked()
    {
        if (_currentRun == null) return false;

        var player = GetTrackedRunPlayerLocked();
        if (player == null || !PlayerHasStrikeDummy(player)) return false;

        bool created = false;
        if (!_currentRun.RelicAggregates.TryGetValue(StrikeDummyRelicId, out var agg) || agg == null)
        {
            agg = new RelicAggregate();
            _currentRun.RelicAggregates[StrikeDummyRelicId] = agg;
            created = true;
        }
        return RefreshStrikeDummyDeckCountsLocked(agg) || created;
    }

    private static bool RefreshStrikeDummyDeckCountsLocked(RelicAggregate agg, Player? player = null)
    {
        player ??= GetTrackedRunPlayerLocked();
        if (player?.Deck?.Cards == null) return false;

        int baseStrikes = 0;
        int nonBaseStrikeCards = 0;
        foreach (var deckCard in player.Deck.Cards)
        {
            if (deckCard == null) continue;
            if (!IsStrikeDummyStrikeCard(deckCard)) continue;

            if (IsBaseStrikeForStrikeDummy(deckCard))
                baseStrikes++;
            else
                nonBaseStrikeCards++;
        }

        bool changed =
            agg.StrikeDummyBaseStrikesInDeck != baseStrikes ||
            agg.StrikeDummyNonBaseStrikeCardsInDeck != nonBaseStrikeCards;

        agg.StrikeDummyBaseStrikesInDeck = baseStrikes;
        agg.StrikeDummyNonBaseStrikeCardsInDeck = nonBaseStrikeCards;
        return changed;
    }

    private static bool RefreshMiniatureCannonDeckCountsIfOwnedLocked()
    {
        if (_currentRun == null) return false;

        var player = GetTrackedRunPlayerLocked();
        if (player == null || !PlayerHasMiniatureCannon(player)) return false;

        bool created = false;
        if (!_currentRun.RelicAggregates.TryGetValue(MiniatureCannonRelicId, out var agg) || agg == null)
        {
            agg = new RelicAggregate();
            _currentRun.RelicAggregates[MiniatureCannonRelicId] = agg;
            created = true;
        }

        return RefreshMiniatureCannonDeckCountsLocked(agg, player) || created;
    }

    private static bool RefreshMiniatureCannonDeckCountsLocked(RelicAggregate agg, Player? player = null)
    {
        player ??= GetTrackedRunPlayerLocked();
        if (player?.Deck?.Cards == null) return false;

        int upgradedAttacks = 0;
        foreach (var deckCard in player.Deck.Cards)
        {
            if (IsMiniatureCannonUpgradedAttackCard(deckCard))
                upgradedAttacks++;
        }

        bool changed = agg.MiniatureCannonUpgradedAttacksInDeck != upgradedAttacks;
        agg.MiniatureCannonUpgradedAttacksInDeck = upgradedAttacks;
        return changed;
    }

    private static Player? GetTrackedRunPlayerLocked()
    {
        try
        {
            var players = RunManager.Instance?.State?.Players;
            if (players == null || players.Count == 0) return null;

            if (_trackedNetId.HasValue)
                return players.FirstOrDefault(p => p.NetId == _trackedNetId.Value);

            return players.Count == 1 ? players[0] : players.FirstOrDefault();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"GetTrackedRunPlayerLocked failed: {e.Message}");
            return null;
        }
    }

    private static bool PlayerHasStrikeDummy(Player player)
    {
        try
        {
            return player.Relics.Any(IsStrikeDummyStatsRelic);
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayerHasMiniatureCannon(Player player)
    {
        try
        {
            return player.Relics.Any(IsMiniatureCannonStatsRelic);
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayerHasJuzuBracelet(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is JuzuBracelet);
        }
        catch
        {
            return false;
        }
    }

    private static void RecordHeldCombatRelicBaselinesForTrackedPlayerLocked(
        bool requireActiveCombat = true,
        bool createPendingIfNeeded = true)
    {
        try
        {
            if (requireActiveCombat && CombatManager.Instance?.IsInProgress != true) return;
            if (_pendingCombat == null)
            {
                if (!createPendingIfNeeded) return;
                _pendingCombat = new PendingCombat();
            }

            var player = GetTrackedRunPlayerLocked();
            if (player == null) return;

            RecordHappyFlowerCombatForPlayerLocked(player);
            RecordNunchakuCombatForPlayerLocked(player);
            RecordBrilliantScarfCombatForPlayerLocked(player);
            RecordMiniatureCannonCombatForPlayerLocked(player);
            RecordBookmarkCombatForPlayerLocked(player);
            RecordPaelsEyeCombatForPlayerLocked(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordHeldCombatRelicBaselinesForTrackedPlayerLocked failed: {e.Message}");
        }
    }

    private static void RecordMiniatureCannonCombatForTrackedPlayerLocked()
    {
        try
        {
            var player = GetTrackedRunPlayerLocked();
            if (player == null) return;
            RecordMiniatureCannonCombatForPlayerLocked(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordMiniatureCannonCombatForTrackedPlayerLocked failed: {e.Message}");
        }
    }

    private static void RecordMiniatureCannonCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasMiniatureCannon(player)) return;
        if (!_pendingCombat.MiniatureCannonCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(MiniatureCannonRelicId);
        agg.Activations += 1;
        RefreshMiniatureCannonDeckCountsIfOwnedLocked();
    }

    private static void RecordHappyFlowerCombatForTrackedPlayerLocked()
    {
        try
        {
            var player = GetTrackedRunPlayerLocked();
            if (player == null) return;
            RecordHappyFlowerCombatForPlayerLocked(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordHappyFlowerCombatForTrackedPlayerLocked failed: {e.Message}");
        }
    }

    private static void RecordHappyFlowerCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasHappyFlower(player)) return;
        if (!_pendingCombat.HappyFlowerCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(HappyFlowerRelicId);
        agg.EnergyGeneratedCombats += 1;
    }

    private static void RecordNunchakuCombatForTrackedPlayerLocked()
    {
        try
        {
            var player = GetTrackedRunPlayerLocked();
            if (player == null) return;
            RecordNunchakuCombatForPlayerLocked(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordNunchakuCombatForTrackedPlayerLocked failed: {e.Message}");
        }
    }

    private static void RecordNunchakuCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasNunchaku(player)) return;
        if (!_pendingCombat.NunchakuCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(NunchakuRelicId);
        agg.EnergyGeneratedCombats += 1;
    }

    private static void RecordNunchakuCombatEndChargeForTrackedPlayerLocked()
    {
        try
        {
            var player = GetTrackedRunPlayerLocked();
            if (player == null) return;
            RecordNunchakuCombatEndChargeForPlayerLocked(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordNunchakuCombatEndChargeForTrackedPlayerLocked failed: {e.Message}");
        }
    }

    private static void RecordNunchakuCombatEndChargeForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!TryGetNunchaku(player, out var nunchaku) || nunchaku == null) return;
        if (!_pendingCombat.NunchakuCombatEndChargeRecordedPlayers.Add(player)) return;

        RecordNunchakuCombatForPlayerLocked(player);
        var agg = GetOrCreatePendingRelicAggregateLocked(NunchakuRelicId);
        RecordNunchakuCombatEndChargeForTest(agg, nunchaku.AttacksPlayed);
    }

    private static void RecordBookmarkCombatForTrackedPlayerLocked()
    {
        try
        {
            var player = GetTrackedRunPlayerLocked();
            if (player == null) return;
            RecordBookmarkCombatForPlayerLocked(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordBookmarkCombatForTrackedPlayerLocked failed: {e.Message}");
        }
    }

    private static void RecordBookmarkCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasBookmark(player)) return;
        if (!_pendingCombat.BookmarkCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(BookmarkRelicId);
        agg.BookmarkCombats += 1;
    }

    private static void RecordPaelsEyeCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasPaelsEye(player)) return;
        if (!_pendingCombat.PaelsEyeCombatCountedPlayers.Add(player)) return;

        GetOrCreatePendingRelicAggregateLocked(PaelsEyeRelicId);
    }

    private static void RecordPaelsEyeCombatsWithoutActivationForTrackedPlayerLocked()
    {
        try
        {
            if (_pendingCombat == null) return;

            var player = GetTrackedRunPlayerLocked();
            if (player == null) return;

            RecordPaelsEyeCombatForPlayerLocked(player);
            if (!_pendingCombat.PaelsEyeCombatCountedPlayers.Contains(player)) return;
            if (_pendingCombat.PaelsEyeActivationStartedPlayers.Contains(player)) return;

            var agg = GetOrCreatePendingRelicAggregateLocked(PaelsEyeRelicId);
            if (agg.Activations > 0) return;

            RecordPaelsEyeCombatWithoutActivationForTest(agg);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordPaelsEyeCombatsWithoutActivationForTrackedPlayerLocked failed: {e.Message}");
        }
    }

    private static void RecordBrilliantScarfCombatForTrackedPlayerLocked()
    {
        try
        {
            var player = GetTrackedRunPlayerLocked();
            if (player == null) return;
            RecordBrilliantScarfCombatForPlayerLocked(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordBrilliantScarfCombatForTrackedPlayerLocked failed: {e.Message}");
        }
    }

    private static void RecordBrilliantScarfCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasBrilliantScarf(player)) return;
        if (!_pendingCombat.BrilliantScarfCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(BrilliantScarfRelicId);
        agg.DiscountCombats += 1;
    }

    private static bool PlayerHasBrilliantScarf(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is BrilliantScarf);
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayerHasPaelsEye(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is PaelsEye);
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayerHasBookmark(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is Bookmark);
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayerHasHappyFlower(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is HappyFlower);
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayerHasNunchaku(Player player)
    {
        return TryGetNunchaku(player, out _);
    }

    private static bool TryGetNunchaku(Player player, out Nunchaku? nunchaku)
    {
        nunchaku = null;

        try
        {
            nunchaku = player?.Relics?.OfType<Nunchaku>().FirstOrDefault();
            return nunchaku != null;
        }
        catch
        {
            nunchaku = null;
            return false;
        }
    }

    private static bool IsStrikeDummyStrikeCard(CardModel? card)
    {
        try
        {
            return card?.Tags?.Contains(CardTag.Strike) == true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsMiniatureCannonUpgradedAttackCard(CardModel? card)
    {
        try
        {
            if (card == null || card.Type != CardType.Attack) return false;
            return Canonical(card).CurrentUpgradeLevel > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsEtherealCard(CardModel? card)
    {
        try
        {
            return card?.Keywords?.Contains(CardKeyword.Ethereal) == true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsBaseStrikeForStrikeDummy(CardModel? card)
    {
        return card != null && IsStrikeDummyStrikeCard(card) && card.IsBasicStrikeOrDefend;
    }

    private static void RecordCombatsInDeckForCurrentDeckLocked()
    {
        try
        {
            var player = RunManager.Instance?.State?.Players.FirstOrDefault();
            if (player?.Deck?.Cards == null) return;

            foreach (var card in player.Deck.Cards)
            {
                if (card == null) continue;
                var instanceId = GetOrAssignInstanceId(card);
                var agg = GetOrCreateAggregate(_pendingCombat!, instanceId);
                agg.CombatsInDeck += 1;
            }
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordCombatsInDeckForCurrentDeckLocked failed: {e.Message}");
        }
    }

    private static void AddHealingLostReasonLocked(
        RelicAggregate agg,
        string reasonId,
        string displayName,
        decimal amount)
    {
        if (amount <= 0m) return;

        if (!agg.HealingLostReasons.TryGetValue(reasonId, out var reason))
        {
            reason = new HealingLostReasonAggregate
            {
                ReasonId = reasonId,
                DisplayName = displayName,
            };
            agg.HealingLostReasons[reasonId] = reason;
        }

        reason.Amount += amount;
    }

    private static void MergeHealingLostReasonsInto(RelicAggregate target, RelicAggregate source)
    {
        if (source.HealingLostReasons == null || source.HealingLostReasons.Count == 0) return;

        foreach (var reason in source.HealingLostReasons.Values)
        {
            if (reason.Amount <= 0m) continue;
            AddHealingLostReasonLocked(
                target,
                string.IsNullOrWhiteSpace(reason.ReasonId) ? reason.DisplayName : reason.ReasonId,
                reason.DisplayName,
                reason.Amount);
        }
    }

    private static void MergeCardsRemovedInto(RelicAggregate target, RelicAggregate source)
    {
        if (source.CardsRemoved == null || source.CardsRemoved.Count == 0) return;

        target.CardsRemoved ??= new List<string>();
        target.CardsRemoved.AddRange(source.CardsRemoved.Where(card => !string.IsNullOrWhiteSpace(card)));
    }

    private static void MergeUpgradedCardsInto(RelicAggregate target, RelicAggregate source)
    {
        if (source.UpgradedCards == null || source.UpgradedCards.Count == 0) return;

        target.UpgradedCards ??= new List<string>();
        target.UpgradedCards.AddRange(source.UpgradedCards.Where(card => !string.IsNullOrWhiteSpace(card)));
    }

    private static void MergeCardRewardCategories(
        Dictionary<string, CardRewardCategoryAggregate> target,
        Dictionary<string, CardRewardCategoryAggregate>? source)
    {
        if (source == null || source.Count == 0) return;

        foreach (var kvp in source)
        {
            if (kvp.Value.Count <= 0) continue;
            AddCardRewardCategory(target, kvp.Key, kvp.Value.DisplayName, kvp.Value.Count);
        }
    }

    private static void AddCardRewardCategory(
        Dictionary<string, CardRewardCategoryAggregate> categories,
        string key,
        string displayName,
        int count)
    {
        if (count <= 0 || string.IsNullOrWhiteSpace(key)) return;

        if (!categories.TryGetValue(key, out var agg))
        {
            agg = new CardRewardCategoryAggregate
            {
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? ToDisplayName(key) : displayName,
            };
            categories[key] = agg;
        }

        if (string.IsNullOrWhiteSpace(agg.DisplayName))
        {
            agg.DisplayName = string.IsNullOrWhiteSpace(displayName) ? ToDisplayName(key) : displayName;
        }

        agg.Count += count;
    }

    private static void MergeRelicCardsGranted(
        Dictionary<string, RelicCardAggregate> target,
        Dictionary<string, RelicCardAggregate>? source)
    {
        if (source == null || source.Count == 0) return;

        foreach (var kvp in source)
        {
            var card = kvp.Value;
            if (card.Count <= 0) continue;
            var cardId = string.IsNullOrWhiteSpace(card.CardId) ? kvp.Key : card.CardId;
            AddRelicCardGranted(target, cardId, card.DisplayName, card.Count);
        }
    }

    private static void MergeRelicCardTransformations(RelicAggregate target, RelicAggregate source)
    {
        if (source.CardTransformations == null || source.CardTransformations.Count == 0) return;

        target.CardTransformations ??= new List<RelicCardTransformationAggregate>();
        foreach (var transformation in source.CardTransformations)
        {
            if (transformation == null) continue;
            if (string.IsNullOrWhiteSpace(transformation.SourceCardId)
                && string.IsNullOrWhiteSpace(transformation.SourceDisplayName)
                && string.IsNullOrWhiteSpace(transformation.ResultCardId)
                && string.IsNullOrWhiteSpace(transformation.ResultDisplayName))
                continue;

            target.CardTransformations.Add(new RelicCardTransformationAggregate
            {
                SourceCardId = transformation.SourceCardId,
                SourceDisplayName = transformation.SourceDisplayName,
                ResultCardId = transformation.ResultCardId,
                ResultDisplayName = transformation.ResultDisplayName,
            });
        }
    }

    private static void AddRelicCardGranted(
        Dictionary<string, RelicCardAggregate> cards,
        string cardId,
        string displayName,
        int count)
    {
        if (count <= 0 || string.IsNullOrWhiteSpace(cardId)) return;

        if (!cards.TryGetValue(cardId, out var agg))
        {
            agg = new RelicCardAggregate
            {
                CardId = cardId,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? FormatCardIdForDisplay(cardId) : displayName,
            };
            cards[cardId] = agg;
        }

        if (string.IsNullOrWhiteSpace(agg.CardId))
            agg.CardId = cardId;
        if (string.IsNullOrWhiteSpace(agg.DisplayName))
            agg.DisplayName = string.IsNullOrWhiteSpace(displayName) ? FormatCardIdForDisplay(cardId) : displayName;

        agg.Count += count;
    }

    private static string GetCardDisplayName(CardModel card)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(card.Title))
                return card.Title;
        }
        catch
        {
        }

        return FormatCardIdForDisplay(card.Id.ToString());
    }

    private static string ToDisplayName(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "Unknown";

        var words = key
            .Replace('-', '_')
            .Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return key;

        return string.Join(" ", words.Select(word =>
            char.ToUpperInvariant(word[0]) + (word.Length > 1 ? word[1..].ToLowerInvariant() : "")));
    }

    /// <summary>
    /// Arm a one-shot marker when Make It So is about to try recurring itself
    /// to Hand. The actual count increments only after the game confirms the
    /// pile change, so hand-full redirects to Discard do not count.
    /// </summary>
    public static void NoteMakeItSoSummonAttempt(MakeItSo makeItSo, CardPlay cardPlay)
    {
        lock (_lock)
        {
            try
            {
                if (makeItSo.Owner == null || cardPlay?.Card == null) return;
                if (!ReferenceEquals(cardPlay.Card.Owner, makeItSo.Owner)) return;
                if (cardPlay.Card.Type != CardType.Skill) return;
                if (makeItSo.Pile?.Type == PileType.Hand) return;

                int threshold = GetMakeItSoThreshold(makeItSo);
                if (threshold <= 0) return;

                int skillsPlayedThisTurn = CountSkillsPlayedThisTurnLocked(
                    makeItSo.Owner,
                    makeItSo.CombatState ?? cardPlay.Card.CombatState);
                if (skillsPlayedThisTurn <= 0 || skillsPlayedThisTurn % threshold != 0)
                    return;

                _pendingMakeItSoSummons.Add(Canonical(makeItSo));
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"NoteMakeItSoSummonAttempt failed: {e.Message}");
            }
        }
    }

    public static bool TryGetMakeItSoSkillCounter(CardModel card, out int currentCount, out int threshold)
    {
        lock (_lock)
        {
            currentCount = 0;
            threshold = 0;

            try
            {
                threshold = GetMakeItSoThreshold(card);
                if (threshold <= 0) return false;
                if (card.Owner == null || card.CombatState == null) return false;
                if (CombatManager.Instance == null || !CombatManager.Instance.IsInProgress) return false;

                int skillsPlayedThisTurn = CountSkillsPlayedThisTurnLocked(card.Owner, card.CombatState);
                currentCount = skillsPlayedThisTurn % threshold;
                return true;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"TryGetMakeItSoSkillCounter failed: {e.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Log a card upgrade to the run's event stream. Called from the
    /// <see cref="Patches.CardUpgradePatch"/> Harmony postfix — fires for
    /// every upgrade path (rest site, Armaments in combat, events that
    /// grant upgrades, Apotheosis, etc.).
    ///
    /// Events go into <c>_currentRun.Events</c> directly, not the pending
    /// combat buffer, because upgrades can happen outside combat (rest
    /// sites, events). They'd be lost if routed through <c>_pendingCombat</c>
    /// when there's no active combat to commit from.
    /// </summary>
    /// <summary>
    /// Log a card removal from the deck. Called from the
    /// <see cref="Patches.CardRemoveFromDeckPatch"/> prefix so we see the
    /// card BEFORE its pile transitions — cleaner state to read.
    ///
    /// Marks the aggregate's Removed flag and stamps the floor. The card
    /// stays in <c>_currentRun.Aggregates</c> with its accumulated stats;
    /// the UI filters/displays it separately based on the Removed flag.
    /// </summary>
    public static void RecordRemoval(CardModel card)
    {
        lock (_lock)
        {
            RecordPrecariousShearsCardRemovedLocked(card);
            if (_currentRun == null) return;

            // Non-assigning: if we haven't seen this card enter the deck,
            // don't create a number just to mark it removed. Removing an
            // untracked card is a no-op for our data model (nothing to
            // update). Shouldn't happen in practice — every card that gets
            // removed must have entered the deck at some point.
            if (!TryGetInstanceId(card, out var instanceId)) return;
            var floor = RunManager.Instance.State?.TotalFloor;

            if (_currentRun.Aggregates.TryGetValue(instanceId, out var agg))
            {
                agg.Removed = true;
                agg.RemovedAtFloor = floor;

                // Snapshot the card's full state (upgrade, enchantment,
                // props, floor_added) so we can reconstruct a matching
                // CardModel ref on hot reload. The game's own
                // ToSerializable() handles this cleanly.
                try { agg.RemovedSnapshot = Canonical(card).ToSerializable(); }
                catch (Exception e) { CoreMain.LogDebug($"RecordRemoval: ToSerializable failed: {e.Message}"); }
            }

            _currentRun.Events.Add(new CardEvent
            {
                T = Now(),
                Type = "card_removed",
                CardId = instanceId,
                Floor = floor,
            });

            CoreMain.Logger.Info($"card_removed: {instanceId} floor={floor}");

            // Save immediately — removals happen OUTSIDE combat (Smith, events,
            // rest-site interactions, curse dispose). Without saving here, the
            // flag lives only in memory and would be lost on F5 between the
            // removal and the next CombatEnded. Removals are infrequent so
            // the I/O cost is negligible.
            RefreshStrikeDummyDeckCountsIfOwnedLocked();
            RefreshMiniatureCannonDeckCountsIfOwnedLocked();
            SaveCurrentRun();
        }
    }

    private static void RecordPrecariousShearsCardRemovedLocked(CardModel card)
    {
        try
        {
            if (card == null) return;
            var owner = card.Owner;
            if (owner == null) return;
            if (!_pendingPrecariousShearsPickups.TryGetValue(owner, out var pending)) return;

            pending.CardsRemoved.Add(GetCardDisplayNameForStats(card));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordPrecariousShearsCardRemovedLocked failed: {e.Message}");
        }
    }

    private static string GetCardDisplayNameForStats(CardModel card)
    {
        try
        {
            var canonical = Canonical(card);
            if (!string.IsNullOrWhiteSpace(canonical.Title))
                return canonical.Title;

            return FormatCardIdForDisplay(canonical.Id.ToString());
        }
        catch
        {
            try { return FormatCardIdForDisplay(card.Id.ToString()); }
            catch { return "Unknown card"; }
        }
    }

    private static string GetCardIdForStats(CardModel card)
    {
        try { return Canonical(card).Id.ToString(); }
        catch
        {
            try { return card.Id.ToString(); }
            catch { return ""; }
        }
    }

    private static int TryGetPileTypeSortValue(CardModel card)
    {
        try { return card.Pile == null ? int.MaxValue : (int)card.Pile.Type; }
        catch { return int.MaxValue; }
    }

    private static int TryGetPileIndex(CardModel card)
    {
        try
        {
            var cards = card.Pile?.Cards;
            if (cards == null) return int.MaxValue;

            for (var i = 0; i < cards.Count; i++)
            {
                if (ReferenceEquals(cards[i], card))
                    return i;
            }

            return int.MaxValue;
        }
        catch { return int.MaxValue; }
    }

    public static void RecordUpgrade(CardModel card)
    {
        lock (_lock)
        {
            RecordSandCastleCardUpgradedLocked(card);
            RecordWhetstoneCardUpgradedLocked(card);
            RecordWarPaintCardUpgradedLocked(card);

            // Non-assigning: skip upgrades on cards we haven't seen enter
            // the deck. This is what fixes the "starters begin at #5" bug
            // — the game fires UpgradeInternal on template/preview cards
            // at run init, and we'd previously assign them fresh numbers,
            // burning the counter before real starters arrived. Now we
            // silently ignore those.
            //
            // Gate BEFORE the lazy run mint: the game also fires
            // UpgradeInternal on template cards during save deserialization,
            // when _currentRun may be null. Minting first created a phantom
            // in_progress run that OnRunStarted then saved. Return first so an
            // untracked card never mints anything.
            if (!TryGetInstanceId(card, out var instanceId)) return;

            // Lazy run-creation guard — upgrade could fire before RunStarted
            // if the mod hot-loaded mid-run and missed the signal. We still
            // want to record the event.
            EnsureLazyCurrentRunLocked();
            var canonical = Canonical(card);
            var newLevel = canonical.CurrentUpgradeLevel;
            var floor = RunManager.Instance.State?.TotalFloor;

            _currentRun.Events.Add(new CardEvent
            {
                T = Now(),
                Type = "card_upgraded",
                CardId = instanceId,
                Floor = floor,
                UpgradeLevel = newLevel,
            });

            // Diagnostic for card-transform-on-upgrade investigation. Logs:
            //   - raw and canonical hashes so we can tell if the upgraded ref
            //     is the same object we saw pre-upgrade (in-place) or a
            //     different object (ref swap / transformation).
            //   - whether the ref is currently in player.Deck.Cards — a
            //     transformation would replace the deck member, so the
            //     POST-upgrade ref should be the one in the deck.
            //   - the card's FloorAddedToDeck, to see if transformed cards
            //     inherit it or start fresh.
            var deckCardCount = -1;
            bool inDeck = false;
            try
            {
                var player = RunManager.Instance.State?.Players.FirstOrDefault();
                if (player?.Deck?.Cards != null)
                {
                    deckCardCount = player.Deck.Cards.Count;
                    foreach (var dc in player.Deck.Cards)
                    {
                        if (ReferenceEquals(Canonical(dc), canonical)) { inDeck = true; break; }
                    }
                }
            }
            catch { }

            CoreMain.Logger.Info(
                $"card_upgraded: {instanceId} level={newLevel} floor={floor} " +
                $"rawHash={card.GetHashCode()} canonicalHash={canonical.GetHashCode()} " +
                $"deckVerNull={card.DeckVersion == null} inDeck={inDeck} " +
                $"floorAddedToDeck={canonical.FloorAddedToDeck}");

            // Save immediately — upgrades mostly happen at campfires,
            // OUTSIDE combat. Without saving here, the upgrade event lives
            // only in memory and is lost on F5 before the next CombatEnded.
            RefreshMiniatureCannonDeckCountsIfOwnedLocked();
            SaveCurrentRun();
        }
    }

    private static void RecordSandCastleCardUpgradedLocked(CardModel card)
    {
        try
        {
            if (card == null) return;
            var owner = card.Owner;
            if (owner == null) return;
            if (!_pendingSandCastlePickups.TryGetValue(owner, out var pending)) return;

            pending.UpgradedCards.Add(GetCardDisplayNameForStats(card));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordSandCastleCardUpgradedLocked failed: {e.Message}");
        }
    }

    private static void RecordWhetstoneCardUpgradedLocked(CardModel card)
    {
        try
        {
            if (card == null) return;
            var owner = card.Owner;
            if (owner == null) return;
            if (!_pendingWhetstonePickups.TryGetValue(owner, out var pending)) return;

            pending.UpgradedCards.Add(GetCardDisplayNameForStats(card));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordWhetstoneCardUpgradedLocked failed: {e.Message}");
        }
    }

    private static void RecordWarPaintCardUpgradedLocked(CardModel card)
    {
        try
        {
            if (card == null) return;
            var owner = card.Owner;
            if (owner == null) return;
            if (!_pendingWarPaintPickups.TryGetValue(owner, out var pending)) return;

            pending.UpgradedCards.Add(GetCardDisplayNameForStats(card));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordWarPaintCardUpgradedLocked failed: {e.Message}");
        }
    }

    /// <summary>
    /// Return the list of CardModel refs that have been marked Removed
    /// this run. Used by the deck-view injection to surface removed cards
    /// alongside current deck cards. Refs remain valid after removal —
    /// CardModel.RemoveFromState only sets a flag, doesn't free the object.
    /// </summary>
    public static IReadOnlyList<CardModel> GetRemovedCards()
    {
        lock (_lock)
        {
            return GetRemovedCardsLocked();
        }
    }

    private static List<CardModel> GetRemovedCardsLocked()
    {
        if (_currentRun == null) return new List<CardModel>();

        var result = new List<CardModel>();
        foreach (var kv in _instanceNumbers)
        {
            var instanceId = $"{kv.Key.Id}#{kv.Value}";
            if (_currentRun.Aggregates.TryGetValue(instanceId, out var agg) && agg.Removed)
            {
                result.Add(kv.Key);
            }
        }

        return result;
    }

    /// <summary>
    /// Additional cards to surface in the full-deck screen when ViewStats is
    /// enabled. Today that includes removed cards plus pooled synthetic
    /// deck-level meta cards for Shiv and Sovereign Blade once the run has
    /// generated them.
    /// </summary>
    public static IReadOnlyList<CardModel> GetSupplementalDeckViewCards()
    {
        lock (_lock)
        {
            RefreshShivAvailabilityLocked();
            RefreshSovereignBladeAvailabilityLocked();

            var result = GetRemovedCardsLocked();

            var shiv = GetShivDeckViewCardLocked();
            if (shiv != null && !result.Contains(shiv))
                result.Add(shiv);

            var sovereignBlade = GetSovereignBladeDeckViewCardLocked();
            if (sovereignBlade != null && !result.Contains(sovereignBlade))
                result.Add(sovereignBlade);

            return result;
        }
    }

    /// <summary>
    /// Return all upgrade events for a given card instance, in chronological
    /// order (oldest first). Used by the tooltip to render the lineage:
    /// "Received: floor 3 → Upgraded: floor 6 → +1".
    /// Returns empty if the card has no upgrade events or isn't tracked.
    /// </summary>
    public static IReadOnlyList<CardEvent> GetUpgradeEvents(CardModel card)
    {
        lock (_lock)
        {
            if (_currentRun == null) return Array.Empty<CardEvent>();
            var key = Canonical(card);
            if (!_instanceNumbers.TryGetValue(key, out var n)) return Array.Empty<CardEvent>();
            var instanceId = $"{key.Id}#{n}";

            var result = new List<CardEvent>();
            foreach (var e in _currentRun.Events)
            {
                if (e.Type == "card_upgraded" && e.CardId == instanceId) result.Add(e);
            }
            return result;
        }
    }

    private static void RecordCardDrawn(CardDrawnEntry entry)
    {
        lock (_lock)
        {
            _pendingCombat ??= new PendingCombat();
            var instanceId = GetOrAssignInstanceId(entry.Card);
            var agg = GetOrCreateAggregate(_pendingCombat, instanceId);
            agg.TimesDrawn++;

            // Don't bloat the events log with a draw entry per card draw —
            // every combat draws ~5 cards/turn × ~5-10 turns so we'd emit
            // 25-50 events just for draws. Aggregate counter is enough.
            // If per-draw forensics becomes useful later, add it here.
        }
    }

    /// <summary>
    /// Direct-path draw attribution, called from
    /// <see cref="Patches.HookAfterCardDrawnPatch"/>. The generic
    /// <c>CombatHistory.Add</c> hook misses draws because
    /// <c>CombatHistory.CardDrawn</c> gets JIT-inlined at the
    /// <c>CardPileCmd.Draw</c> call site, which bypasses the Harmony patch.
    /// Hooking <c>Hook.AfterCardDrawn</c> (a larger method that isn't
    /// inlined) gives us a reliable attribution point; this method does
    /// the same work as <see cref="RecordCardDrawn"/> but takes the bare
    /// <c>CardModel</c> since there's no <c>CardDrawnEntry</c> on the
    /// <c>AfterCardDrawn</c> code path.
    /// </summary>
    /// <summary>
    /// Record a card being placed on top of the draw pile from Hand or
    /// Discard. Fired from <see cref="Patches.CardPlacedOnTopPatch"/>.
    /// </summary>
    public static void RecordPlacedOnTopOfDraw(CardModel card, MegaCrit.Sts2.Core.Entities.Cards.PileType sourcePile)
    {
        lock (_lock)
        {
            _pendingCombat ??= new PendingCombat();
            var instanceId = GetOrAssignInstanceId(card);
            var agg = GetOrCreateAggregate(_pendingCombat, instanceId);
            if (sourcePile == MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand)
                agg.TimesPlacedOnTopFromHand++;
            else if (sourcePile == MegaCrit.Sts2.Core.Entities.Cards.PileType.Discard)
                agg.TimesPlacedOnTopFromDiscard++;
        }
    }

    private static void RecordCardDiscarded(CardModel card)
    {
        lock (_lock)
        {
            _pendingCombat ??= new PendingCombat();
            var instanceId = GetOrAssignInstanceId(card);
            var agg = GetOrCreateAggregate(_pendingCombat, instanceId);
            agg.TimesDiscarded++;

            if (card.Owner is Player player
                && IsTrackedPlayer(player)
                && _pendingCombat.GamblingChipDiscardAttributionPlayers.Contains(player))
            {
                var relicAgg = GetOrCreateRelicAggregateLocked(GamblingChipRelicId);
                relicAgg.CardsDiscarded += 1;
            }
        }
    }

    /// <summary>
    /// When a card is exhausted, find the currently-resolving player card
    /// play (if any) and attribute the exhaust to that play's card —
    /// unless it's a self-exhaust (card exhausting itself post-play),
    /// which we deliberately don't count. Useful for cards like Havoc,
    /// Fiend Fire, Second Wind that exhaust OTHER cards.
    /// </summary>
    private static void RecordCardExhausted(CardModel exhaustedCard)
    {
        lock (_lock)
        {
            _pendingCombat ??= new PendingCombat();
            var exhaustedId = GetOrAssignInstanceId(exhaustedCard);
            var exhaustedAgg = GetOrCreateAggregate(_pendingCombat, exhaustedId);
            exhaustedAgg.TimesExhausted++;

            try
            {
                var causingPlay = FindCurrentlyResolvingCardPlay();
                if (causingPlay?.Card == null) return;

                // Skip self-exhaust — "exhausted OTHER cards" is the stat.
                if (ReferenceEquals(Canonical(causingPlay.Card), Canonical(exhaustedCard))) return;
                var instanceId = GetOrAssignInstanceId(causingPlay.Card);
                var agg = GetOrCreateAggregate(_pendingCombat, instanceId);
                agg.TimesExhaustedOtherCards++;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordCardExhausted failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Walk combat history backwards to find the latest CardPlayStartedEntry
    /// whose matching CardPlayFinishedEntry hasn't fired yet — i.e. the play
    /// currently mid-resolution. Returns null if no play is active.
    /// Used for attributing side-effect events (exhausts, draws) to the
    /// card that caused them, since the game's entries for those effects
    /// don't include a CardPlay reference.
    /// </summary>
    // Current CombatHistory entry count for window staleness. Single source
    // used both when arming (ArmedAtHistoryCount) and consuming.
    private static int CurrentHistoryCountLocked()
        => CombatManager.Instance?.History?.Entries?.Count() ?? 0;

    private static CardPlay? FindCurrentlyResolvingCardPlay()
    {
        if (_currentPlayerCardPlay?.Card != null) return _currentPlayerCardPlay;

        var history = CombatManager.Instance?.History;
        if (history == null) return null;
        CardPlay? result = null;
        foreach (var e in history.Entries.Reverse())
        {
            if (e is CardPlayFinishedEntry) return null;  // nothing in progress
            if (e is CardPlayStartedEntry cps) { result = cps.CardPlay; break; }
        }
        return result;
    }

    public static void NoteDrawAttempt(Player player, bool fromHandDraw)
    {
        lock (_lock)
        {
            if (fromHandDraw)
            {
                _pendingDrawSourceCard = null;
                return;
            }

            try
            {
                _pendingDrawSourceCard = FindLikelyDrawSourceCard(player);
                if (_pendingDrawSourceCard != null)
                {
                    _pendingCombat ??= new PendingCombat();
                    var sourceId = GetOrAssignInstanceId(_pendingDrawSourceCard);
                    var sourceAgg = GetOrCreateAggregate(_pendingCombat, sourceId);
                    sourceAgg.TimesCardsDrawAttempted++;
                    _pendingDrawAttempts.Add(new PendingDrawAttempt
                    {
                        Player = player,
                        SourceCard = _pendingDrawSourceCard,
                    });
                }
            }
            catch (Exception e)
            {
                _pendingDrawSourceCard = null;
                _pendingDrawAttempts.Clear();
                CoreMain.LogDebug($"NoteDrawAttempt failed: {e.Message}");
            }
        }
    }

    public static void NoteEffectSource(AbstractModel? source)
    {
        lock (_lock)
        {
            if (source is CardModel sourceCard)
            {
                _pendingEffectSourceCard = Canonical(sourceCard);
                _pendingEffectSourceHistoryCount = CombatManager.Instance?.History?.Entries?.Count() ?? 0;
            }
        }
    }

    public static void RecordShivGenerated(CardModel? card)
    {
        if (card == null) return;

        lock (_lock)
        {
            if (!string.Equals(Canonical(card).Id.ToString(), ShivDefinitionId, StringComparison.Ordinal))
                return;

            _shivAvailableThisRun = true;

            EnsureLazyCurrentRunLocked();
            _pendingCombat ??= new PendingCombat();

            bool alreadyRecorded =
                _currentRun.Events.Any(e => e.Type == ShivGeneratedEventType) ||
                _pendingCombat.CombatEvents.Any(e => e.Type == ShivGeneratedEventType);
            if (alreadyRecorded) return;

            _pendingCombat.CombatEvents.Add(new CardEvent
            {
                T = Now(),
                Type = ShivGeneratedEventType,
                CardId = ShivDefinitionId,
                Floor = RunManager.Instance.State?.TotalFloor,
            });
        }
    }

    public static void NotePoisonTickStarting(object poisonPower, IReadOnlyList<Creature>? participants = null)
    {
        lock (_lock)
        {
            try
            {
                var target = TryResolvePoisonPowerTarget(poisonPower);
                if (target == null || target.IsPlayer) return;

                // Mirror PoisonPower.cs line 57: base.Owner == our resolved
                // target, so if it is not a participant no tick fires and we
                // must NOT arm (a stale window would mis-claim a later
                // null-dealer hit). participants==null (defensive) falls back
                // to arming (old behavior).
                if (participants != null && !ParticipantsContain(participants, target))
                    return;

                _pendingCombat ??= new PendingCombat();
                _pendingCombat.PendingPoisonTicks[target] = new PendingPoisonTick
                {
                    ArmedAtHistoryCount = CombatManager.Instance?.History?.Entries?.Count() ?? 0,
                };
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"NotePoisonTickStarting failed: {e.Message}");
            }
        }
    }

    public static void NoteNoxiousFumesTick(object noxiousFumesPower, IReadOnlyList<Creature>? participants = null)
    {
        lock (_lock)
        {
            try
            {
                if (noxiousFumesPower is not PowerModel power) return;

                var owner = GetPowerReceiverCreature(power);
                if (owner == null) return;

                // Mirror NoxiousFumesPower.cs line 28: only arm when owner
                // (== base.Owner) is a participant of the starting side.
                if (participants != null && !ParticipantsContain(participants, owner))
                    return;

                _pendingCombat ??= new PendingCombat();
                if (!_pendingCombat.NoxiousFumesContributionsByPower.TryGetValue(power, out var contributions)
                    || contributions.Count == 0)
                {
                    CoreMain.LogDebug(
                        $"Noxious Fumes tick missing contribution ledger owner={DescribeCreature(owner)} amount={power.Amount}");
                    return;
                }

                int recipients = CountLikelyNoxiousFumesRecipients(power, owner);
                if (recipients <= 0) return;

                var snapshot = contributions.Values
                    .Where(share => share.Amount > PoisonOwnershipEpsilon)
                    .Select(share => new NoxiousFumesContributionShare
                    {
                        CardInstanceId = share.CardInstanceId,
                        Amount = share.Amount,
                    })
                    .ToList();
                if (snapshot.Count == 0)
                {
                    CoreMain.LogDebug(
                        $"Noxious Fumes tick had empty contribution snapshot owner={DescribeCreature(owner)} amount={power.Amount}");
                    return;
                }

                decimal trackedTotal = snapshot.Sum(share => share.Amount);
                if (!AreClose(trackedTotal, power.Amount))
                {
                    CoreMain.LogDebug(
                        $"Noxious Fumes contribution mismatch owner={DescribeCreature(owner)} powerAmount={power.Amount} tracked={trackedTotal}");
                }

                var window = new PendingNoxiousFumesApplicationWindow
                {
                    RemainingApplications = recipients,
                    ExpectedAmount = power.Amount,
                };
                foreach (var share in snapshot)
                    window.Contributions.Add(share);
                _pendingCombat.PendingNoxiousFumesApplications[owner] = window;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"NoteNoxiousFumesTick failed: {e.Message}");
            }
        }
    }

    public static void RecordSovereignBladeGenerated(CardModel? card)
    {
        if (card == null) return;
        if (!IsSovereignBladeCard(card)) return;

        lock (_lock)
        {
            var canonical = Canonical(card);
            var definitionId = canonical.Id.ToString();

            _sovereignBladeAvailableThisRun = true;
            _sovereignBladeDefinitionIdThisRun = definitionId;

            try
            {
                _sovereignBladeDeckViewCard = canonical.ToMutable();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordSovereignBladeGenerated clone failed: {e.Message}");
            }

            EnsureLazyCurrentRunLocked();
            _pendingCombat ??= new PendingCombat();

            var existingEvent = _pendingCombat.CombatEvents
                .LastOrDefault(e => e.Type == SovereignBladeForgedEventType);
            existingEvent ??= _currentRun.Events
                .LastOrDefault(e => e.Type == SovereignBladeForgedEventType);

            if (existingEvent != null)
            {
                existingEvent.CardId = definitionId;
                existingEvent.Floor ??= RunManager.Instance.State?.TotalFloor;
                return;
            }

            _pendingCombat.CombatEvents.Add(new CardEvent
            {
                T = Now(),
                Type = SovereignBladeForgedEventType,
                CardId = definitionId,
                Floor = RunManager.Instance.State?.TotalFloor,
            });
        }
    }

    public static void RecordSovereignBladeForged()
    {
        lock (_lock)
        {
            _sovereignBladeAvailableThisRun = true;

            EnsureLazyCurrentRunLocked();
            _pendingCombat ??= new PendingCombat();

            bool alreadyRecorded =
                _currentRun.Events.Any(e => e.Type == SovereignBladeForgedEventType) ||
                _pendingCombat.CombatEvents.Any(e => e.Type == SovereignBladeForgedEventType);
            if (alreadyRecorded) return;

            _pendingCombat.CombatEvents.Add(new CardEvent
            {
                T = Now(),
                Type = SovereignBladeForgedEventType,
                CardId = _sovereignBladeDefinitionIdThisRun ?? "",
                Floor = RunManager.Instance.State?.TotalFloor,
            });
        }
    }

    public static void NotePowerAmountChangeAttempt(
        PowerModel power,
        decimal amount,
        Creature target,
        Creature? applier,
        CardModel? cardSource)
    {
        lock (_lock)
        {
            _pendingPowerChangeAttempts.Add(new PendingPowerChangeAttempt
            {
                Power = power,
                Target = target,
                Applier = applier,
                RequestedAmount = amount,
                CardSource = cardSource != null ? Canonical(cardSource) : null,
            });
        }
    }

    public static void RecordArtifactBlockedDebuffAttempt(
        PowerModel canonicalPower,
        Creature target,
        decimal requestedAmount,
        Creature? applier,
        IEnumerable<AbstractModel>? modifiers,
        decimal modifiedAmount)
    {
        lock (_lock)
        {
            var attempt = TakePendingPowerChangeAttemptLocked(canonicalPower, target, applier, requestedAmount);

            if (modifiedAmount != 0m) return;
            if (canonicalPower.GetTypeForAmount(requestedAmount) != PowerType.Debuff) return;
            if (!WasArtifactBlock(target, modifiers)) return;

            var sourceCard = ResolvePowerChangeSourceCardLocked(attempt, applier);
            if (sourceCard == null)
            {
                if (IsPoisonPower(canonicalPower)
                    && TryRecordNoxiousFumesPoisonArtifactBlockLocked(canonicalPower, target, applier, requestedAmount))
                    return;

                CoreMain.LogDebug(
                    $"Artifact-blocked debuff unattributed power={canonicalPower.Id} amount={requestedAmount} " +
                    $"target={DescribeCreature(target)} applier={DescribeCreature(applier)}");
                return;
            }

            _pendingCombat ??= new PendingCombat();
            var instanceId = GetOrAssignInstanceId(sourceCard);
            RecordArtifactBlockedEffectLocked(instanceId, canonicalPower, requestedAmount);
        }
    }

    private static PendingPowerChangeAttempt? TakePendingPowerChangeAttemptLocked(
        PowerModel power,
        Creature target,
        Creature? applier,
        decimal requestedAmount)
    {
        for (int i = _pendingPowerChangeAttempts.Count - 1; i >= 0; i--)
        {
            var attempt = _pendingPowerChangeAttempts[i];
            if (!ReferenceEquals(attempt.Power, power)) continue;
            if (!ReferenceEquals(attempt.Target, target)) continue;
            if (!ReferenceEquals(attempt.Applier, applier)) continue;
            if (attempt.RequestedAmount != requestedAmount) continue;

            _pendingPowerChangeAttempts.RemoveAt(i);
            return attempt;
        }

        for (int i = _pendingPowerChangeAttempts.Count - 1; i >= 0; i--)
        {
            var attempt = _pendingPowerChangeAttempts[i];
            if (!ReferenceEquals(attempt.Power, power)) continue;
            if (!ReferenceEquals(attempt.Target, target)) continue;

            _pendingPowerChangeAttempts.RemoveAt(i);
            return attempt;
        }

        return null;
    }

    private static CardModel? ResolvePowerChangeSourceCardLocked(
        PendingPowerChangeAttempt? attempt,
        Creature? applier)
    {
        if (attempt?.CardSource != null) return attempt.CardSource;

        var applierPlayer = applier?.Player;
        if (applierPlayer != null && _pendingEffectSourceCard != null && IsOwnedBy(_pendingEffectSourceCard, applierPlayer))
        {
            int historyCount = CombatManager.Instance?.History?.Entries?.Count() ?? 0;
            if (historyCount == _pendingEffectSourceHistoryCount)
                return _pendingEffectSourceCard;
        }

        var causingPlay = FindCurrentlyResolvingCardPlay();
        if (causingPlay?.Card != null) return Canonical(causingPlay.Card);

        if (_recentCompletedPlayerCardPlay?.Card != null)
        {
            int historyCount = CombatManager.Instance?.History?.Entries?.Count() ?? 0;
            if (historyCount == _recentCompletedPlayerCardPlayHistoryCount)
                return Canonical(_recentCompletedPlayerCardPlay.Card);
        }

        return null;
    }

    private static bool WasArtifactModifier(IEnumerable<AbstractModel>? modifiers)
    {
        if (modifiers == null) return false;
        foreach (var modifier in modifiers)
        {
            if (modifier is ArtifactPower) return true;
        }
        return false;
    }

    private static bool WasArtifactBlock(Creature target, IEnumerable<AbstractModel>? modifiers)
    {
        if (WasArtifactModifier(modifiers)) return true;

        try
        {
            return target.HasPower(ModelDb.GetId(typeof(ArtifactPower)));
        }
        catch
        {
            return false;
        }
    }

    private static BlockedDrawReasonAggregate GetOrCreateBlockedDrawReason(
        CardAggregate agg,
        string reasonId,
        string displayName)
    {
        if (!agg.BlockedDrawReasons.TryGetValue(reasonId, out var reason))
        {
            reason = new BlockedDrawReasonAggregate
            {
                ReasonId = reasonId,
                DisplayName = displayName,
            };
            agg.BlockedDrawReasons[reasonId] = reason;
        }
        else if (string.IsNullOrWhiteSpace(reason.DisplayName) && !string.IsNullOrWhiteSpace(displayName))
        {
            reason.DisplayName = displayName;
        }

        return reason;
    }

    private static void RecordBlockedDrawReason(
        CardAggregate agg,
        string reasonId,
        string displayName)
    {
        var reason = GetOrCreateBlockedDrawReason(agg, reasonId, displayName);
        reason.Count++;
    }

    private static void TrackPlayerPowerOwnershipLocked(
        PowerModel power,
        string instanceId,
        AppliedEffectAggregate effect)
    {
        if (_pendingCombat == null) return;

        _pendingCombat.PlayerPowerOwnershipByModifier[power] = new PlayerPowerOwnershipShare
        {
            CardInstanceId = instanceId,
            EffectId = effect.EffectId,
            DisplayName = effect.DisplayName,
            IconPath = effect.IconPath,
        };
    }

    private static bool TryResolvePlayerPowerOwnershipLocked(
        AbstractModel modifier,
        out PlayerPowerOwnershipShare? ownership)
    {
        ownership = null;
        if (_pendingCombat == null) return false;

        if (_pendingCombat.PlayerPowerOwnershipByModifier.TryGetValue(modifier, out ownership))
            return ownership != null;

        if (modifier is not PowerModel power)
            return false;

        PlayerPowerOwnershipShare? match = null;
        string effectId = power.Id.ToString();
        foreach (var candidate in _pendingCombat.PlayerPowerOwnershipByModifier.Values)
        {
            if (!string.Equals(candidate.EffectId, effectId, StringComparison.Ordinal))
                continue;

            if (match != null &&
                (!string.Equals(match.CardInstanceId, candidate.CardInstanceId, StringComparison.Ordinal)
                 || !string.Equals(match.DisplayName, candidate.DisplayName, StringComparison.Ordinal)
                 || !string.Equals(match.IconPath, candidate.IconPath, StringComparison.Ordinal)))
            {
                return false;
            }

            match = candidate;
        }

        ownership = match;
        return ownership != null;
    }

    private static (string ReasonId, string DisplayName) ResolveBlockedDrawReasonLocked(
        Player player,
        AbstractModel? modifier,
        PlayerPowerOwnershipShare? ownership)
    {
        if (ownership != null)
            return ($"effect:{ownership.EffectId}", ownership.DisplayName);

        if (modifier is PowerModel power)
            return ($"effect:{power.Id}", GetPowerDisplayName(power));

        if (IsLikelyHandFull(player))
            return ("full_hand", "hand full");

        if (modifier != null)
            return ($"modifier:{modifier.GetType().FullName}", GetModifierDisplayName(modifier));

        return ("other", "other");
    }

    private static string GetModifierDisplayName(AbstractModel modifier)
    {
        if (modifier is PowerModel power)
            return GetPowerDisplayName(power);

        var typeName = modifier.GetType().Name;
        if (typeName.EndsWith("Power", StringComparison.OrdinalIgnoreCase))
            typeName = typeName.Substring(0, typeName.Length - "Power".Length);

        return string.IsNullOrWhiteSpace(typeName) ? "Other" : typeName;
    }

    private static bool IsLikelyHandFull(Player player)
    {
        const int defaultHandLimit = 10;

        try
        {
            if (player == null) return false;

            object? handObject = TryReadMemberValue(player, ["Hand", "HandPile", "CardsInHand"]);
            if (handObject == null) return false;

            int? handCount = TryReadCollectionCount(handObject);
            if (!handCount.HasValue) return false;

            int handLimit =
                TryReadIntMember(player, ["MaxHandSize", "HandLimit", "MaxCardsInHand"])
                ?? TryReadIntMember(handObject, ["MaxSize", "MaxCards", "Limit", "Capacity"])
                ?? defaultHandLimit;

            return handLimit > 0 && handCount.Value >= handLimit;
        }
        catch
        {
            return false;
        }
    }

    private static object? TryReadMemberValue(object source, IReadOnlyList<string> memberNames)
    {
        var type = source.GetType();
        foreach (var memberName in memberNames)
        {
            var prop = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null && prop.CanRead)
            {
                try
                {
                    var value = prop.GetValue(source);
                    if (value != null) return value;
                }
                catch { }
            }

            var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                try
                {
                    var value = field.GetValue(source);
                    if (value != null) return value;
                }
                catch { }
            }
        }

        return null;
    }

    private static int? TryReadIntMember(object source, IReadOnlyList<string> memberNames)
    {
        var value = TryReadMemberValue(source, memberNames);
        if (value == null) return null;

        try
        {
            return Convert.ToInt32(value);
        }
        catch
        {
            return null;
        }
    }

    private static int? TryReadCollectionCount(object source)
    {
        var count = TryReadIntMember(source, ["Count"]);
        if (count.HasValue) return count;

        var cards = TryReadMemberValue(source, ["Cards", "_cards"]);
        if (cards == null) return null;

        count = TryReadIntMember(cards, ["Count", "Length"]);
        if (count.HasValue) return count;

        if (cards is System.Collections.ICollection collection)
            return collection.Count;

        return null;
    }

    // Reference-equality membership test matching the game's
    // participants.Contains(base.Owner) (PoisonPower.cs line 57 /
    // NoxiousFumesPower.cs line 28). Creatures are compared by identity.
    private static bool ParticipantsContain(IReadOnlyList<Creature> participants, Creature creature)
    {
        if (participants == null || creature == null) return false;
        for (int i = 0; i < participants.Count; i++)
            if (ReferenceEquals(participants[i], creature)) return true;
        return false;
    }

    private static Creature? GetPowerReceiverCreature(PowerModel power)
    {
        try
        {
            return power.Owner ?? power.Target;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsNoxiousFumesPower(PowerModel power)
    {
        try
        {
            var effectId = power.Id.ToString();
            if (effectId.Contains("NOXIOUS_FUMES", StringComparison.OrdinalIgnoreCase))
                return true;

            if (power.GetType().Name.Contains("NoxiousFumes", StringComparison.OrdinalIgnoreCase))
                return true;

            return string.Equals(GetPowerDisplayName(power), "Noxious Fumes", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static int CountLikelyNoxiousFumesRecipients(PowerModel power, Creature owner)
    {
        try
        {
            return power.CombatState
                .GetOpponentsOf(owner)
                .Count(creature => creature.IsAlive && creature.CanReceivePowers);
        }
        catch
        {
            return 0;
        }
    }

    private static void RecordAppliedEffectLocked(string instanceId, PowerModel power, decimal amount)
    {
        if (_pendingCombat == null || amount <= 0m) return;

        var agg = GetOrCreateAggregate(_pendingCombat, instanceId);
        var effect = GetOrCreateAppliedEffect(agg, power);
        effect.TimesApplied++;
        effect.TotalAmountApplied += amount;
    }

    private static void RecordArtifactBlockedEffectLocked(string instanceId, PowerModel power, decimal amount)
    {
        if (_pendingCombat == null || amount <= 0m) return;

        var agg = GetOrCreateAggregate(_pendingCombat, instanceId);
        var effect = GetOrCreateAppliedEffect(agg, power);
        effect.TimesBlockedByArtifact++;
        effect.TotalAmountBlockedByArtifact += amount;
    }

    private static void RecordPoisonApplicationLocked(Creature target, string instanceId, PowerModel power, decimal amount)
    {
        if (target.IsPlayer || amount <= 0m) return;

        _pendingCombat ??= new PendingCombat();
        RecordAppliedEffectLocked(instanceId, power, amount);
        AddPoisonOwnershipLocked(target, instanceId, power, amount);
    }

    private static bool TryRecordNoxiousFumesPoisonApplicationLocked(
        PowerModel poisonPower,
        Creature target,
        Creature? applier,
        decimal amount)
    {
        if (target.IsPlayer || amount <= 0m) return false;
        if (!TryTakePendingNoxiousFumesApplicationWindowLocked(applier, out var window)) return false;
        if (!TryAllocateNoxiousFumesContributions(window, amount, out var allocations, out var unattributed))
            return false;

        foreach (var allocation in allocations)
            RecordPoisonApplicationLocked(target, allocation.CardInstanceId, poisonPower, allocation.Amount);

        if (unattributed > PoisonOwnershipEpsilon)
        {
            CoreMain.LogDebug(
                $"Noxious Fumes poison application under-attributed target={DescribeCreature(target)} " +
                $"requested={amount} tracked={amount - unattributed} remainder={unattributed}");
        }

        return true;
    }

    private static bool TryRecordNoxiousFumesPoisonArtifactBlockLocked(
        PowerModel poisonPower,
        Creature target,
        Creature? applier,
        decimal requestedAmount)
    {
        if (target.IsPlayer || requestedAmount <= 0m) return false;
        if (!TryTakePendingNoxiousFumesApplicationWindowLocked(applier, out var window)) return false;
        if (!TryAllocateNoxiousFumesContributions(window, requestedAmount, out var allocations, out var unattributed))
            return false;

        _pendingCombat ??= new PendingCombat();
        foreach (var allocation in allocations)
            RecordArtifactBlockedEffectLocked(allocation.CardInstanceId, poisonPower, allocation.Amount);

        if (unattributed > PoisonOwnershipEpsilon)
        {
            CoreMain.LogDebug(
                $"Noxious Fumes Artifact block under-attributed target={DescribeCreature(target)} " +
                $"requested={requestedAmount} tracked={requestedAmount - unattributed} remainder={unattributed}");
        }

        return true;
    }

    private static bool TryTakePendingNoxiousFumesApplicationWindowLocked(
        Creature? applier,
        out PendingNoxiousFumesApplicationWindow window)
    {
        window = null!;
        if (_pendingCombat == null || applier == null) return false;
        if (!_pendingCombat.PendingNoxiousFumesApplications.TryGetValue(applier, out var pendingWindow))
            return false;

        window = pendingWindow;
        pendingWindow.RemainingApplications--;
        if (pendingWindow.RemainingApplications <= 0)
            _pendingCombat.PendingNoxiousFumesApplications.Remove(applier);

        return true;
    }

    private static bool TryAllocateNoxiousFumesContributions(
        PendingNoxiousFumesApplicationWindow window,
        decimal requestedAmount,
        out List<NoxiousFumesContributionAllocation> allocations,
        out decimal unattributed)
    {
        allocations = new List<NoxiousFumesContributionAllocation>();
        unattributed = 0m;

        if (requestedAmount <= 0m) return false;

        var contributors = window.Contributions
            .Where(share => share.Amount > PoisonOwnershipEpsilon)
            .ToList();
        if (contributors.Count == 0) return false;

        decimal trackedTotal = contributors.Sum(share => share.Amount);
        if (trackedTotal <= PoisonOwnershipEpsilon) return false;

        decimal attributableAmount = Math.Min(requestedAmount, trackedTotal);
        decimal remainingAttributable = attributableAmount;
        for (int i = 0; i < contributors.Count; i++)
        {
            var contributor = contributors[i];
            decimal amount = i == contributors.Count - 1
                ? remainingAttributable
                : attributableAmount * contributor.Amount / trackedTotal;
            remainingAttributable -= amount;
            if (amount <= PoisonOwnershipEpsilon) continue;

            allocations.Add(new NoxiousFumesContributionAllocation
            {
                CardInstanceId = contributor.CardInstanceId,
                Amount = amount,
            });
        }

        if (remainingAttributable > PoisonOwnershipEpsilon && allocations.Count > 0)
            allocations[^1].Amount += remainingAttributable;

        unattributed = Math.Max(0m, requestedAmount - attributableAmount);
        return allocations.Count > 0;
    }

    private static void TrackNoxiousFumesContributionLocked(
        PowerModel power,
        string sourceCardInstanceId,
        decimal amount)
    {
        if (string.IsNullOrWhiteSpace(sourceCardInstanceId) || amount <= 0m) return;

        _pendingCombat ??= new PendingCombat();
        if (!_pendingCombat.NoxiousFumesContributionsByPower.TryGetValue(power, out var contributions))
        {
            contributions = new Dictionary<string, NoxiousFumesContributionShare>(StringComparer.Ordinal);
            _pendingCombat.NoxiousFumesContributionsByPower[power] = contributions;
        }

        if (!contributions.TryGetValue(sourceCardInstanceId, out var share))
        {
            share = new NoxiousFumesContributionShare
            {
                CardInstanceId = sourceCardInstanceId,
            };
            contributions[sourceCardInstanceId] = share;
        }

        share.Amount += amount;
    }

    private static CardModel? FindLikelyBlockSourceCard(Creature receiver)
    {
        var targetPlayer = receiver.Player;
        if (targetPlayer == null) return null;

        var causingPlay = FindCurrentlyResolvingCardPlay();
        if (causingPlay?.Card != null && IsOwnedBy(causingPlay.Card, targetPlayer))
            return Canonical(causingPlay.Card);

        if (_recentCompletedPlayerCardPlay?.Card != null && IsOwnedBy(_recentCompletedPlayerCardPlay.Card, targetPlayer))
        {
            int historyCount = CombatManager.Instance?.History?.Entries?.Count() ?? 0;
            if (historyCount <= _recentCompletedPlayerCardPlayHistoryCount + 1)
                return Canonical(_recentCompletedPlayerCardPlay.Card);
        }

        return null;
    }

    private static CardModel? FindLikelyDrawSourceCard(Player targetPlayer)
    {
        if (_pendingEffectSourceCard != null && IsOwnedBy(_pendingEffectSourceCard, targetPlayer))
        {
            int historyCount = CombatManager.Instance?.History?.Entries?.Count() ?? 0;
            if (historyCount == _pendingEffectSourceHistoryCount)
                return _pendingEffectSourceCard;
        }

        var causingPlay = FindCurrentlyResolvingCardPlay();
        if (causingPlay?.Card != null && IsOwnedBy(causingPlay.Card, targetPlayer))
            return Canonical(causingPlay.Card);

        if (_recentCompletedPlayerCardPlay?.Card != null && IsOwnedBy(_recentCompletedPlayerCardPlay.Card, targetPlayer))
        {
            int historyCount = CombatManager.Instance?.History?.Entries?.Count() ?? 0;
            if (historyCount == _recentCompletedPlayerCardPlayHistoryCount)
                return Canonical(_recentCompletedPlayerCardPlay.Card);
        }

        return null;
    }

    private static bool IsOwnedBy(CardModel card, Player targetPlayer)
    {
        if (targetPlayer == null) return true;
        if (card.Owner == null) return true;
        return ReferenceEquals(card.Owner, targetPlayer);
    }

    private static bool TryConsumePendingDrawAttempt(Player? player, out PendingDrawAttempt? attempt)
    {
        attempt = null;
        if (_pendingDrawAttempts.Count == 0) return false;

        int index = -1;
        if (player != null)
        {
            for (int i = 0; i < _pendingDrawAttempts.Count; i++)
            {
                if (ReferenceEquals(_pendingDrawAttempts[i].Player, player))
                {
                    index = i;
                    break;
                }
            }
        }

        if (index < 0)
            index = 0;

        attempt = _pendingDrawAttempts[index];
        _pendingDrawAttempts.RemoveAt(index);
        return true;
    }

    public static void RecordDrawFromCard(CardModel card, bool fromHandDraw)
    {
        lock (_lock)
        {
            _pendingCombat ??= new PendingCombat();
            var instanceId = GetOrAssignInstanceId(card);
            var agg = GetOrCreateAggregate(_pendingCombat, instanceId);
            agg.TimesDrawn++;

            // If the draw is NOT a turn-start hand-draw (fromHandDraw=true
            // means turn-start), it was caused by some card's play effect.
            // Attribute to the currently-resolving play so that card can
            // show "drew N cards this run" in its stats. Skip self-draw
            // (drawing a card that happens to be the one being played)
            // since that's uncommon and introduces noise.
            if (!fromHandDraw)
            {
                try
                {
                    CardModel? sourceCard = null;
                    if (TryConsumePendingDrawAttempt(card.Owner, out var pendingAttempt))
                        sourceCard = pendingAttempt!.SourceCard;

                    sourceCard ??= _pendingDrawSourceCard;
                    if (sourceCard == null)
                    {
                        var causingPlay = FindCurrentlyResolvingCardPlay();
                        sourceCard = causingPlay?.Card;
                    }

                    if (sourceCard != null
                        && !ReferenceEquals(Canonical(sourceCard), Canonical(card)))
                    {
                        var causerId = GetOrAssignInstanceId(sourceCard);
                        var causerAgg = GetOrCreateAggregate(_pendingCombat, causerId);
                        causerAgg.TimesCardsDrawn++;
                    }
                }
                catch (Exception e)
                {
                    CoreMain.LogDebug($"RecordDrawFromCard attribution failed: {e.Message}");
                }
            }
        }
    }

    public static void RecordBlockedDrawAttempt(Player player, bool fromHandDraw, AbstractModel? modifier)
    {
        lock (_lock)
        {
            if (fromHandDraw) return;

            _pendingCombat ??= new PendingCombat();

            try
            {
                CardModel? sourceCard = null;
                if (TryConsumePendingDrawAttempt(player, out var pendingAttempt))
                    sourceCard = pendingAttempt!.SourceCard;

                sourceCard ??= _pendingDrawSourceCard ?? FindLikelyDrawSourceCard(player);
                RecordBlockedDrawAttemptLocked(player, sourceCard, modifier);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordBlockedDrawAttempt failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Catch draw-pile exits that never arrive in Hand. Full-hand redirects
    /// can bypass Hook.ShouldDraw's false path, so the draw-card still needs
    /// a blocked-attempt attribution even though no No Draw-like modifier
    /// actually vetoed the draw.
    /// </summary>
    public static void RecordCardChangedPiles(CardModel card, PileType oldPile)
    {
        lock (_lock)
        {
            try
            {
                if (_pendingMakeItSoSummons.Count > 0
                    && card is MakeItSo
                    && oldPile != PileType.Hand)
                {
                    var key = Canonical(card);
                    if (_pendingMakeItSoSummons.Remove(key) && card.Pile?.Type == PileType.Hand)
                    {
                        _pendingCombat ??= new PendingCombat();
                        var instanceId = GetOrAssignInstanceId(card);
                        var agg = GetOrCreateAggregate(_pendingCombat, instanceId);
                        agg.TimesSummonedToHand++;
                    }
                }

                if (oldPile != PileType.Draw) return;
                if (card?.Pile?.Type == PileType.Hand) return;
                if (card?.Owner is not Player player) return;
                if (!IsLikelyHandFull(player)) return;
                if (!TryConsumePendingDrawAttempt(player, out var pendingAttempt)) return;

                RecordBlockedDrawAttemptLocked(
                    player,
                    pendingAttempt!.SourceCard,
                    modifier: null,
                    forcedReasonId: "full_hand",
                    forcedDisplayName: "hand full",
                    suppressBlockingEffect: true);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordCardChangedPiles failed: {e.Message}");
            }
        }
    }

    private static void RecordBlockedDrawAttemptLocked(
        Player player,
        CardModel? sourceCard,
        AbstractModel? modifier,
        string? forcedReasonId = null,
        string? forcedDisplayName = null,
        bool suppressBlockingEffect = false)
    {
        bool recordedSourceBlockedDraw = false;
        CardAggregate? sourceAgg = null;
        if (sourceCard != null)
        {
            var sourceId = GetOrAssignInstanceId(sourceCard);
            sourceAgg = GetOrCreateAggregate(_pendingCombat!, sourceId);
            sourceAgg.TimesCardsDrawBlocked++;
            recordedSourceBlockedDraw = true;
        }

        bool recordedBlockingEffect = false;
        PlayerPowerOwnershipShare? ownership = null;
        if (!suppressBlockingEffect && modifier != null && TryResolvePlayerPowerOwnershipLocked(modifier, out ownership))
        {
            var blockerAgg = GetOrCreateAggregate(_pendingCombat!, ownership!.CardInstanceId);
            var blockerEffect = GetOrCreateAppliedEffect(
                blockerAgg,
                ownership.EffectId,
                ownership.DisplayName,
                ownership.IconPath);
            blockerEffect.TotalTriggeredCardsDrawBlocked++;
            recordedBlockingEffect = true;
        }

        if (sourceAgg != null)
        {
            var reason = forcedReasonId != null
                ? (forcedReasonId, forcedDisplayName ?? forcedReasonId)
                : ResolveBlockedDrawReasonLocked(player, modifier, ownership);
            RecordBlockedDrawReason(sourceAgg, reason.Item1, reason.Item2);
        }

        if (!recordedSourceBlockedDraw && !recordedBlockingEffect)
        {
            CoreMain.LogDebug(
                $"Blocked draw unattributed modifier={modifier?.GetType().Name ?? "null"}");
        }
    }

    private static void RecordPowerReceived(PowerReceivedEntry entry)
    {
        lock (_lock)
        {
            _pendingCombat ??= new PendingCombat();

            try
            {
                var target = TryResolvePowerReceivedTarget(entry);
                var causingPlay = FindCurrentlyResolvingCardPlay();
                if (causingPlay?.Card == null)
                {
                    if (target != null
                        && IsPoisonPower(entry.Power)
                        && TryRecordNoxiousFumesPoisonApplicationLocked(entry.Power, target, entry.Applier, entry.Amount))
                        return;

                    CoreMain.LogDebug(
                        $"PowerReceivedEntry unattributed power={entry.Power.Id} amount={entry.Amount} " +
                        $"target={DescribeCreature(target)} applier={DescribeCreature(entry.Applier)}");
                    return;
                }

                var instanceId = GetOrAssignInstanceId(causingPlay.Card);
                var agg = GetOrCreateAggregate(_pendingCombat, instanceId);
                if (entry.Amount > 0m
                    && IsPoisonPower(entry.Power)
                    && target != null
                    && !target.IsPlayer)
                    RecordPoisonApplicationLocked(target, instanceId, entry.Power, entry.Amount);
                else if (entry.Amount > 0m)
                {
                    // Only positive applications count. The game fires
                    // History.PowerReceived even for Artifact-zeroed (amount==0)
                    // and debuff-consuming (negative) deltas; without this guard
                    // an Artifact-eaten stack showed as both "applied" and
                    // "blocked by Artifact", and negatives drove TotalAmountApplied
                    // below zero.
                    var effect = GetOrCreateAppliedEffect(agg, entry.Power);
                    effect.TimesApplied++;
                    effect.TotalAmountApplied += entry.Amount;
                }

                if (target?.IsPlayer == true && entry.Amount > 0m)
                {
                    var effect = GetOrCreateAppliedEffect(agg, entry.Power);
                    TrackPlayerPowerOwnershipLocked(entry.Power, instanceId, effect);

                    if (IsNoxiousFumesPower(entry.Power))
                        TrackNoxiousFumesContributionLocked(entry.Power, instanceId, entry.Amount);
                }
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordPowerReceived failed: {e.Message}");
            }
        }
    }

    // Max CombatHistory-entry distance between a poison window being armed in
    // AfterSideTurnStart and its tick's DamageReceivedEntry arriving. Ticks land
    // within a handful of history entries; 24 tolerates intervening
    // power/decrement/vfx entries while rejecting a window that survived to an
    // unrelated later hit. Re-stamped per tick, so it applies per-tick-gap not
    // across a whole multi-iteration burst.
    private const int PoisonTickMaxHistoryDistance = 24;

    private static bool TryRecordPoisonTickDamage(DamageReceivedEntry entry)
    {
        lock (_lock)
        {
            if (_pendingCombat == null) return false;
            if (!_pendingCombat.PoisonOwnershipByTarget.TryGetValue(entry.Receiver, out var ownership)
                || ownership.Count == 0)
                return false;

            // Route through the one canonical damage convention: intended =
            // blocked + unblocked + overkill (the true tick size), effective =
            // unblocked (HP actually lost — overkill is disjoint). Omitting
            // overkill previously made the normalize step scale ownership down
            // on kills and the unarmed-fallback amount-match drop lethal ticks.
            var tickTotals = ComputeEnemyDamageTotals(
                entry.Result.BlockedDamage, entry.Result.UnblockedDamage, entry.Result.OverkillDamage);
            decimal totalAttempted = tickTotals.IntendedDamage;
            if (totalAttempted <= 0m) return false;

            // Genuine poison ticks are dealt with dealer:null AND cardSource:null
            // (PoisonPower.cs line 64). A non-null Dealer means some other hit
            // that merely lands on a poisoned creature; never a tick.
            if (entry.Dealer != null)
                return false;

            // Bounded freshness. The old `historyCount >= ArmedAtHistoryCount`
            // was vacuous (CombatHistory only grows). A tick's entry lands only
            // a small bounded number of history entries after the arm.
            bool armedTick = false;
            if (_pendingCombat.PendingPoisonTicks.TryGetValue(entry.Receiver, out var pendingTick))
            {
                int historyCount = CombatManager.Instance?.History?.Entries?.Count() ?? 0;
                int delta = historyCount - pendingTick.ArmedAtHistoryCount;
                if (delta >= 0 && delta <= PoisonTickMaxHistoryDistance)
                    armedTick = true;
                else
                    _pendingCombat.PendingPoisonTicks.Remove(entry.Receiver); // stale
            }

            decimal trackedTotal = ownership.Values.Sum(share => Math.Max(0m, share.Amount));
            if (trackedTotal <= 0m) return false;

            // Dealer==null already established; fallback reduces to amount match.
            bool fallbackAmountMatch = AreClose(trackedTotal, totalAttempted);
            if (!armedTick && !fallbackAmountMatch)
            {
                if (entry.Result.WasTargetKilled)
                    _pendingCombat.PoisonOwnershipByTarget.Remove(entry.Receiver);
                return false;
            }

            // One AfterSideTurnStart loops TriggerCount times, each a separate
            // null-dealer entry on the same receiver. Keep the armed entry live
            // across the burst and re-stamp its freshness clock so the next tick
            // stays in-window; dropped on kill/exhaust/stale below.
            if (armedTick && _pendingCombat.PendingPoisonTicks.ContainsKey(entry.Receiver))
            {
                _pendingCombat.PendingPoisonTicks[entry.Receiver] = new PendingPoisonTick
                {
                    ArmedAtHistoryCount = CombatManager.Instance?.History?.Entries?.Count() ?? 0,
                };
            }

            if (trackedTotal > totalAttempted)
            {
                decimal normalize = totalAttempted / trackedTotal;
                foreach (var share in ownership.Values)
                    share.Amount *= normalize;
            }

            // Effective = HP actually lost (tickTotals.EffectiveDamage =
            // UnblockedDamage). The old `unblocked - overkill` double-counted
            // overkill and zeroed every killing tick's effective damage.
            decimal effectiveDamage = Math.Max(0m, (decimal)tickTotals.EffectiveDamage);
            decimal overkillDamage = Math.Max(0m, entry.Result.OverkillDamage);

            foreach (var share in ownership.Values.ToList())
            {
                if (share.Amount <= PoisonOwnershipEpsilon)
                {
                    ownership.Remove(share.Key);
                    continue;
                }

                decimal fraction = share.Amount / totalAttempted;
                var agg = GetOrCreateAggregate(_pendingCombat, share.CardInstanceId);
                var effect = GetOrCreateAppliedEffect(agg, share.EffectId, share.DisplayName, share.IconPath);
                effect.TotalTriggeredEffectiveDamage += effectiveDamage * fraction;
                effect.TotalTriggeredOverkill += overkillDamage * fraction;
            }

            if (entry.Result.WasTargetKilled || totalAttempted <= 1m)
            {
                _pendingCombat.PoisonOwnershipByTarget.Remove(entry.Receiver);
                _pendingCombat.PendingPoisonTicks.Remove(entry.Receiver);
                return true;
            }

            decimal decay = (totalAttempted - 1m) / totalAttempted;
            foreach (var key in ownership.Keys.ToList())
            {
                ownership[key].Amount *= decay;
                if (ownership[key].Amount <= PoisonOwnershipEpsilon)
                    ownership.Remove(key);
            }

            if (ownership.Count == 0)
                _pendingCombat.PoisonOwnershipByTarget.Remove(entry.Receiver);

            return true;
        }
    }

    private static void AddPoisonOwnershipLocked(Creature target, string instanceId, PowerModel power, decimal amount)
    {
        if (_pendingCombat == null || target.IsPlayer || amount <= 0m) return;

        if (!_pendingCombat.PoisonOwnershipByTarget.TryGetValue(target, out var ownership))
        {
            ownership = new Dictionary<PoisonOwnershipKey, PoisonOwnershipShare>();
            _pendingCombat.PoisonOwnershipByTarget[target] = ownership;
        }

        string effectId = power.Id.ToString();
        var key = new PoisonOwnershipKey(instanceId, effectId);
        if (!ownership.TryGetValue(key, out var share))
        {
            share = new PoisonOwnershipShare
            {
                Key = key,
                CardInstanceId = instanceId,
                EffectId = effectId,
                DisplayName = GetPowerDisplayName(power),
                IconPath = GetPowerIconPath(power),
            };
            ownership[key] = share;
        }

        share.Amount += amount;
    }

    private static void RecordBlockGainedEntry(BlockGainedEntry entry)
    {
        lock (_lock)
        {
            // Co-op: block gained by a partner (or their summon) must not enter
            // our aggregates/ledger. Non-player receivers unaffected.
            if (entry.Receiver.IsPlayer && !IsTrackedPlayerCreature(entry.Receiver)) return;
            _pendingCombat ??= new PendingCombat();

            string? instanceId = null;
            if (entry.CardPlay?.Card != null)
            {
                instanceId = GetOrAssignInstanceId(entry.CardPlay.Card);
            }
            else if (entry.Receiver.IsPlayer)
            {
                var fallbackCard = FindLikelyBlockSourceCard(entry.Receiver);
                if (fallbackCard != null)
                    instanceId = GetOrAssignInstanceId(fallbackCard);
            }

            if (instanceId != null)
            {
                var agg = GetOrCreateAggregate(_pendingCombat, instanceId);
                agg.TotalBlockGained += entry.Amount;

                _pendingCombat.CombatEvents.Add(new CardEvent
                {
                    T = Now(),
                    Type = "block_gained",
                    CardId = instanceId,
                    Blocked = entry.Amount,
                });
            }
            else if (entry.Receiver.IsPlayer)
            {
                var recvDesc = DescribeCreature(entry.Receiver);
                CoreMain.LogDebug(
                    $"BlockGainedEntry unattributed receiver={recvDesc} amount={entry.Amount}");
            }

            if (entry.Receiver.IsPlayer)
            {
                AppendPlayerBlockChunkLocked(instanceId, entry.Amount);
                ReconcilePlayerBlockLedgerLocked(entry.Receiver);
            }
        }
    }

    private static void RecordPlayerBlockedDamage(DamageReceivedEntry entry)
    {
        if (!entry.Receiver.IsPlayer) return;

        int blocked = entry.Result.BlockedDamage;
        if (blocked <= 0) return;

        lock (_lock)
        {
            _pendingCombat ??= new PendingCombat();
            AttributeBlockedDamageLocked(blocked);
            ReconcilePlayerBlockLedgerLocked(entry.Receiver);
        }
    }

    private static void RecordEnemyDamage(DamageReceivedEntry entry)
    {
        // Only damage dealt TO the player counts as enemy damage. Without this
        // guard, a summon body (Osty is a Creature with Monster set but
        // IsPlayer=false) absorbing an enemy hit via DieForYou is counted as
        // damage-to-player and doubles DamageInstances per redirect, and Osty's
        // OWN attacks on enemies mint a phantom MONSTER.OSTY enemy aggregate.
        // Matches RecordPlayerBlockedDamage and the primer's enemy-damage spec.
        if (!entry.Receiver.IsPlayer) return;
        // Co-op: only the TRACKED player's damage taken is our defensive stat
        // (a partner soaking a hit is not).
        if (!IsTrackedPlayerCreature(entry.Receiver)) return;
        if (entry.Dealer == null || entry.Dealer.IsPlayer || entry.Dealer.Monster == null) return;

        int blocked = Math.Max(0, entry.Result.BlockedDamage);
        int dealt = Math.Max(0, entry.Result.UnblockedDamage);
        int overkill = Math.Max(0, entry.Result.OverkillDamage);
        int attempted = blocked + dealt + overkill;
        if (attempted <= 0) return;

        lock (_lock)
        {
            try
            {
                _pendingCombat ??= new PendingCombat();
                string enemyId = GetEnemyId(entry.Dealer);
                string displayName = GetEnemyDisplayName(entry.Dealer);
                var agg = GetOrCreateEnemyAggregateLocked(enemyId);
                if (string.IsNullOrWhiteSpace(agg.DisplayName))
                    agg.DisplayName = displayName;
                agg.DamageInstances += 1;
                agg.DamageAttempted += attempted;
                agg.DamageDealt += dealt;
                agg.DamageBlocked += blocked;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordEnemyDamage failed: {e.Message}");
            }
        }
    }

    public static void NotePotentialPlayerBlockClear(Creature creature)
    {
        lock (_lock)
        {
            if (!creature.IsPlayer) return;
            _pendingPlayerBlockClearAmount = Math.Max(0, creature.Block);
            _pendingPlayerBlockClearArmed = _pendingPlayerBlockClearAmount > 0;
        }
    }

    public static void NotePlayerBlockClearPrevented(Creature creature)
    {
        lock (_lock)
        {
            if (!creature.IsPlayer) return;
            ClearPendingPlayerBlockClearLocked();
        }
    }

    public static void NotePlayerBlockCleared(Creature creature)
    {
        lock (_lock)
        {
            if (!creature.IsPlayer) return;

            if (_pendingCombat == null)
            {
                ClearPendingPlayerBlockClearLocked();
                return;
            }

            int actualRemaining = Math.Max(0, creature.Block);
            // Unarmed fallback is 0, not the whole ledger: the game fires
            // AfterBlockCleared even when a retain effect (Barricade/Blur/Sturdy
            // Clamp) PREVENTED the clear, so wasting all tracked block would
            // mislabel retained block as wasted and strip its card attribution.
            // Reconcile below wastes exactly max(0, tracked - actual), which is
            // correct whether block truly cleared (actual→0 wastes all) or was
            // retained (actual unchanged wastes nothing).
            int removed = _pendingPlayerBlockClearArmed
                ? Math.Max(0, _pendingPlayerBlockClearAmount - actualRemaining)
                : 0;

            AttributeUnusedBlockLocked(removed);
            ReconcilePlayerBlockLedgerLocked(creature);
            ClearPendingPlayerBlockClearLocked();
        }
    }

    private static void AppendPlayerBlockChunkLocked(string? cardInstanceId, int amount)
    {
        if (_pendingCombat == null || amount <= 0) return;

        _pendingCombat.PlayerBlockLedger.Add(new BlockChunk
        {
            CardInstanceId = cardInstanceId,
            Remaining = amount,
        });
    }

    private static void AttributeBlockedDamageLocked(int blocked)
    {
        if (_pendingCombat == null || blocked <= 0) return;

        int remainingToAttribute = blocked;
        for (int i = 0; i < _pendingCombat.PlayerBlockLedger.Count && remainingToAttribute > 0; i++)
        {
            var chunk = _pendingCombat.PlayerBlockLedger[i];
            if (chunk.Remaining <= 0) continue;

            int consumed = Math.Min(chunk.Remaining, remainingToAttribute);
            chunk.Remaining -= consumed;
            remainingToAttribute -= consumed;

            if (chunk.CardInstanceId != null)
            {
                var agg = GetOrCreateAggregate(_pendingCombat, chunk.CardInstanceId);
                agg.TotalBlockEffective += consumed;
            }
        }

        _pendingCombat.PlayerBlockLedger.RemoveAll(chunk => chunk.Remaining <= 0);
    }

    private static void AttributeUnusedBlockLocked(int unusedBlockToRemove)
    {
        if (_pendingCombat == null || unusedBlockToRemove <= 0) return;

        for (int i = _pendingCombat.PlayerBlockLedger.Count - 1; i >= 0 && unusedBlockToRemove > 0; i--)
        {
            var chunk = _pendingCombat.PlayerBlockLedger[i];
            if (chunk.Remaining <= 0) continue;

            int wasted = Math.Min(chunk.Remaining, unusedBlockToRemove);
            chunk.Remaining -= wasted;
            unusedBlockToRemove -= wasted;

            if (chunk.CardInstanceId != null)
            {
                var agg = GetOrCreateAggregate(_pendingCombat, chunk.CardInstanceId);
                agg.TotalBlockWasted += wasted;
            }
        }

        _pendingCombat.PlayerBlockLedger.RemoveAll(chunk => chunk.Remaining <= 0);
    }

    private static int TotalTrackedPlayerBlockLocked()
    {
        return _pendingCombat?.PlayerBlockLedger.Sum(chunk => chunk.Remaining) ?? 0;
    }

    /// <summary>
    /// Test seam for the block-ledger attribution pipeline (issue #6). Runs the
    /// real gain → absorb → clear sequence against a throwaway pending combat
    /// and returns it so the FIFO-absorb / LIFO-waste invariant can be pinned
    /// headlessly, without needing a live combat to feed BlockGainedEntry /
    /// DamageReceivedEntry. Saves and restores the live <c>_pendingCombat</c>
    /// under <c>_lock</c>, so it never disturbs a concurrent real combat.
    /// </summary>
    internal static PendingCombat RunBlockLedgerForTest(
        IEnumerable<(string? cardInstanceId, int amount)> gains,
        int blockedDamage,
        int clearedUnusedBlock)
    {
        lock (_lock)
        {
            var previous = _pendingCombat;
            try
            {
                var scratch = new PendingCombat();
                _pendingCombat = scratch;
                foreach (var (cardInstanceId, amount) in gains)
                {
                    // Mirror the production block-gain pairing: a card-attributed
                    // gain credits TotalBlockGained AND pushes a ledger chunk of
                    // the same amount (see RecordBlockGainedEntry). Crediting it
                    // here lets the conservation test assert the real invariant
                    // gained == effective + wasted — the tooltip divides absorbed
                    // and wasted percentages by TotalBlockGained.
                    if (cardInstanceId != null)
                        GetOrCreateAggregate(scratch, cardInstanceId).TotalBlockGained += amount;
                    AppendPlayerBlockChunkLocked(cardInstanceId, amount);
                }
                AttributeBlockedDamageLocked(blockedDamage);
                AttributeUnusedBlockLocked(clearedUnusedBlock);
                return scratch;
            }
            finally
            {
                _pendingCombat = previous;
            }
        }
    }

    private static void ReconcilePlayerBlockLedgerLocked(Creature creature)
    {
        if (_pendingCombat == null || !creature.IsPlayer) return;

        int actualBlock = Math.Max(0, creature.Block);
        int trackedBlock = TotalTrackedPlayerBlockLocked();

        if (trackedBlock > actualBlock)
        {
            AttributeUnusedBlockLocked(trackedBlock - actualBlock);
        }
        else if (trackedBlock < actualBlock)
        {
            AppendPlayerBlockChunkLocked(cardInstanceId: null, amount: actualBlock - trackedBlock);
        }
    }

    private static void ClearPendingPlayerBlockClearLocked()
    {
        _pendingPlayerBlockClearAmount = 0;
        _pendingPlayerBlockClearArmed = false;
    }

    private static void RecordDamageFromCard(DamageReceivedEntry entry)
    {
        var result = entry.Result;

        lock (_lock)
        {
            _pendingCombat ??= new PendingCombat();
            var instanceId = GetOrAssignInstanceId(entry.CardSource!);
            var agg = GetOrCreateAggregate(_pendingCombat, instanceId);
            MarkReplayAttackDamageLocked(entry.CardSource!);

            if (entry.Receiver.IsPlayer)
            {
                // Self-damage (Hemokinesis, Offering, Combust tick, etc.).
                // We track HP actually lost (UnblockedDamage), which is
                // POST-reduction — Tungsten Rod / buffer effects naturally
                // show up as less HP loss. That's what the user wants to
                // see: what did this card really cost me?
                agg.TotalHpLost += result.UnblockedDamage;

                _pendingCombat.CombatEvents.Add(new CardEvent
                {
                    T = Now(),
                    Type = "damage_received",
                    CardId = instanceId,
                    // A player creature always has a Character id; the
                    // "PLAYER" fallback only guards the theoretical null
                    // and — crucially — never carries the "MONSTER."
                    // prefix, so the repair path keeps classifying this
                    // as self-damage (excluded from offensive totals).
                    Receiver = entry.Receiver.Player?.Character?.Id.ToString() ?? "PLAYER",
                    Blocked = result.BlockedDamage,
                    Unblocked = result.UnblockedDamage,
                    Overkill = result.OverkillDamage,
                    Killed = result.WasTargetKilled,
                });
            }
            else
            {
                // Enemy damage — offensive stats.
                var damageTotals = ComputeEnemyDamageTotals(
                    result.BlockedDamage,
                    result.UnblockedDamage,
                    result.OverkillDamage);
                agg.TotalIntended += damageTotals.IntendedDamage;
                agg.TotalBlocked += result.BlockedDamage;
                agg.TotalOverkill += result.OverkillDamage;
                agg.TotalEffective += damageTotals.EffectiveDamage;
                if (result.WasTargetKilled) agg.Kills++;
                RecordMiniatureCannonUpgradedAttackHitIfOwnedLocked(entry.CardSource!);

                _pendingCombat.CombatEvents.Add(new CardEvent
                {
                    T = Now(),
                    Type = "damage_received",
                    CardId = instanceId,
                    // GetEnemyId falls back to "MONSTER.UNKNOWN" when the
                    // receiver has no Monster ref, so an enemy event always
                    // carries the "MONSTER." prefix the repair keys on. A
                    // raw Monster?.Id.ToString() could be null, and the
                    // repair drops null/non-"MONSTER." receivers — silently
                    // wiping this card's damage + kills on any adopt or
                    // Continue rebuild (#254).
                    Receiver = GetEnemyId(entry.Receiver),
                    Blocked = result.BlockedDamage,
                    Unblocked = result.UnblockedDamage,
                    Overkill = result.OverkillDamage,
                    Killed = result.WasTargetKilled,
                });
            }
        }
    }

    /// <summary>
    /// Atomically record that this DamageResult has been observed, returning
    /// true only for the FIRST observation. Observe() calls this for every
    /// real DamageReceivedEntry (discarding the result); the combat-ending
    /// capture branches on it — false means the game already emitted (or we
    /// already synthesized) an entry for this exact DamageResult object.
    /// One method for both sides so the dedup pairing can't drift.
    /// </summary>
    internal static bool TryMarkDamageResultObserved(DamageResult? result)
    {
        if (result == null) return false;
        lock (_lock) { return _observedDamageResults.Add(result); }
    }

    /// <summary>Test isolation for the observed-result dedup set.</summary>
    internal static void ClearObservedDamageResultsForTest()
    {
        lock (_lock) { _observedDamageResults.Clear(); }
    }

    /// <summary>
    /// Record damage whose combat-history entry the game suppressed because
    /// the hit itself ended the combat.
    ///
    /// <c>CreatureCmd.Damage</c> applies HP loss first and only then emits
    /// <c>DamageReceivedEntry</c> behind an <c>IsInProgress &amp;&amp; !IsEnding</c>
    /// gate — so the hit that kills the last living enemy flips
    /// <c>CombatManager.IsEnding</c> and suppresses its own history entry.
    /// Scaling finishers (Death March) systematically lost their biggest,
    /// fight-ending hits to this.
    ///
    /// Called from <see cref="Patches.HookAfterDamageGivenPatch"/>.
    /// <c>Hook.AfterDamageGiven</c> is dispatched directly by the game so it
    /// "must still resolve" for the killing hit, and it fires AFTER the
    /// (possible) history emission for the same DamageResult — so
    /// "combat is ending" + "result not observed yet" identifies exactly the
    /// suppressed window. Out-of-combat damage (<c>!IsInProgress</c>) stays
    /// unrecorded, matching the game's own combat-history behavior.
    ///
    /// The synthesized entry is routed through <see cref="Observe"/> so
    /// instance identity, replay marking, enemy bookkeeping, and the persisted
    /// event schema stay identical to an organic entry. It is NOT added to the
    /// game's own CombatHistory — game state is left untouched.
    /// </summary>
    public static void RecordCombatEndingSuppressedDamage(
        MegaCrit.Sts2.Core.Combat.ICombatState? combatState,
        Creature? dealer,
        DamageResult? result,
        CardModel? cardSource)
    {
        if (combatState == null || result == null) return;

        var combatManager = CombatManager.Instance;
        // IsEnding is only true while combat is still in progress — exactly
        // the window where the game's emission gate is closed. Known residual:
        // IsEnding is a live computed property re-derived here, slightly after
        // the game's own gate evaluated it; a hook listener that spawns a new
        // primary enemy between the two (Phrog-Parasite pattern) could flip it
        // back and make us skip a genuinely suppressed hit. Capturing at the
        // exact gate point would need a transpiler on CreatureCmd.Damage —
        // deliberately not worth that fragility for the corner case.
        if (combatManager == null || !combatManager.IsEnding) return;

        // Post-OnRunEnded tail guard: on a loss the game calls
        // RunManager.OnEnded synchronously from the killing action, and the
        // deferred CombatEnded (ProcessPendingLoss) only fires afterwards.
        // Damage resolving in that gap would lazily resurrect _pendingCombat
        // through the Record* paths and mint a junk run file at that late
        // CombatEnded. Once the run record is gone, stop capturing.
        if (Current == null) return;

        if (!TryMarkDamageResultObserved(result)) return;  // emitted normally

        var receiver = result.Receiver;
        if (receiver == null) return;

        // Always-on Info (like the CardSource=null diagnostic): these should
        // be rare — one per combat-ending hit — and when totals are disputed
        // we want the evidence in the log without a debug flag.
        CoreMain.Logger.Info(
            $"Recording combat-ending suppressed damage: card={cardSource?.Title ?? "null"} " +
            $"receiver={DescribeCreature(receiver)} dealer={DescribeCreature(dealer)} " +
            $"blocked={result.BlockedDamage} unblocked={result.UnblockedDamage} " +
            $"overkill={result.OverkillDamage} killed={result.WasTargetKilled}");

        var entry = new DamageReceivedEntry(
            result, receiver, dealer, cardSource,
            combatState.RoundNumber, combatState.CurrentSide,
            combatManager.History, combatState.Players);
        Observe(entry);
    }

    private static void MarkReplayAttackDamageLocked(CardModel card)
    {
        if (_currentPlayerCardPlay?.Card == null) return;
        if (!IsReplayExtraPlay(_currentPlayerCardPlay)) return;
        if (!ReferenceEquals(Canonical(_currentPlayerCardPlay.Card), Canonical(card))) return;

        if (_pendingReplayAttackOutcomes.TryGetValue(_currentPlayerCardPlay, out var outcome))
            outcome.HasDamage = true;
    }

    private static bool TryFinishReplayAttackNoDamageLocked(CardPlay cardPlay)
    {
        if (!_pendingReplayAttackOutcomes.TryGetValue(cardPlay, out var outcome))
            return false;

        _pendingReplayAttackOutcomes.Remove(cardPlay);
        return !outcome.HasDamage;
    }

    internal static (int IntendedDamage, int EffectiveDamage) ComputeEnemyDamageTotals(
        int blockedDamage,
        int unblockedDamage,
        int overkillDamage)
    {
        // DamageReceivedEntry reports lethal hits as:
        //   unblocked = HP actually lost
        //   overkill = attempted damage beyond lethal
        // So intended damage needs all three components, while "effective"
        // damage is simply the HP that really came off the target.
        int intendedDamage = blockedDamage + unblockedDamage + overkillDamage;
        int effectiveDamage = unblockedDamage;
        return (intendedDamage, effectiveDamage);
    }

    internal static bool RepairOffensiveDamageAggregatesFromEvents(RunData run)
    {
        var rebuilt = new Dictionary<string, (int Intended, int Blocked, int Overkill, int Effective, int Kills)>();

        foreach (var cardEvent in run.Events)
        {
            if (!string.Equals(cardEvent.Type, "damage_received", StringComparison.Ordinal)) continue;
            if (string.IsNullOrWhiteSpace(cardEvent.CardId)) continue;
            // A null/empty Receiver is treated as enemy damage. The pre-#254
            // writer could stamp a null Receiver for enemy hits (no Monster
            // ref), and dropping those here permanently wiped the card's
            // offensive totals on rebuild. Self-damage always carries a
            // non-null character id, so a present-but-non-"MONSTER." receiver
            // is the only thing still excluded from offensive stats.
            if (!string.IsNullOrWhiteSpace(cardEvent.Receiver) &&
                !cardEvent.Receiver.StartsWith("MONSTER.", StringComparison.Ordinal)) continue;

            rebuilt.TryGetValue(cardEvent.CardId, out var totals);

            int blockedDamage = cardEvent.Blocked ?? 0;
            int unblockedDamage = cardEvent.Unblocked ?? 0;
            int overkillDamage = cardEvent.Overkill ?? 0;
            var damageTotals = ComputeEnemyDamageTotals(blockedDamage, unblockedDamage, overkillDamage);

            totals.Intended += damageTotals.IntendedDamage;
            totals.Blocked += blockedDamage;
            totals.Overkill += overkillDamage;
            totals.Effective += damageTotals.EffectiveDamage;
            if (cardEvent.Killed == true) totals.Kills++;

            rebuilt[cardEvent.CardId] = totals;
        }

        bool changed = false;
        foreach (var aggregate in run.Aggregates.Values)
        {
            if (aggregate.TotalIntended != 0) changed = true;
            if (aggregate.TotalBlocked != 0) changed = true;
            if (aggregate.TotalOverkill != 0) changed = true;
            if (aggregate.TotalEffective != 0) changed = true;
            if (aggregate.Kills != 0) changed = true;

            aggregate.TotalIntended = 0;
            aggregate.TotalBlocked = 0;
            aggregate.TotalOverkill = 0;
            aggregate.TotalEffective = 0;
            aggregate.Kills = 0;
        }

        foreach (var (cardId, totals) in rebuilt)
        {
            if (!run.Aggregates.TryGetValue(cardId, out var aggregate))
            {
                aggregate = new CardAggregate();
                run.Aggregates[cardId] = aggregate;
                changed = true;
            }

            if (aggregate.TotalIntended != totals.Intended) changed = true;
            if (aggregate.TotalBlocked != totals.Blocked) changed = true;
            if (aggregate.TotalOverkill != totals.Overkill) changed = true;
            if (aggregate.TotalEffective != totals.Effective) changed = true;
            if (aggregate.Kills != totals.Kills) changed = true;

            aggregate.TotalIntended = totals.Intended;
            aggregate.TotalBlocked = totals.Blocked;
            aggregate.TotalOverkill = totals.Overkill;
            aggregate.TotalEffective = totals.Effective;
            aggregate.Kills = totals.Kills;
        }

        return changed;
    }

    // -------- Helpers --------

    private static CardAggregate GetOrCreateAggregate(PendingCombat pending, string cardId)
    {
        if (!pending.CombatAggregates.TryGetValue(cardId, out var agg))
        {
            agg = new CardAggregate();
            pending.CombatAggregates[cardId] = agg;
        }
        return agg;
    }

    private static CardAggregate GetOrCreateAggregate(RunData run, string cardId)
    {
        if (!run.Aggregates.TryGetValue(cardId, out var agg))
        {
            agg = new CardAggregate();
            run.Aggregates[cardId] = agg;
        }
        return agg;
    }

    private static string GetEnemyId(Creature creature)
    {
        return creature.Monster?.Id.ToString() ?? "MONSTER.UNKNOWN";
    }

    private static string GetEnemyDisplayName(Creature creature)
    {
        return creature.Monster?.Id.ToString() ?? "Enemy";
    }

    private static int GetMakeItSoThreshold(CardModel card)
    {
        if (card is not MakeItSo makeItSo) return 0;

        try
        {
            return Math.Max(0, makeItSo.DynamicVars.Cards.IntValue);
        }
        catch
        {
            return 0;
        }
    }

    private static int CountSkillsPlayedThisTurnLocked(Player owner, ICombatState? combatState)
    {
        if (combatState is not CombatState concreteCombatState) return 0;

        try
        {
            var finishedPlays = CombatManager.Instance?.History?.CardPlaysFinished;
            if (finishedPlays == null) return 0;

            return finishedPlays.Count(e =>
                e.CardPlay?.Card != null
                && ReferenceEquals(e.CardPlay.Card.Owner, owner)
                && e.CardPlay.Card.Type == CardType.Skill
                && e.HappenedThisTurn(concreteCombatState));
        }
        catch
        {
            return 0;
        }
    }

    // Clone = copy the per-instance lineage/identity fields (which a merge
    // intentionally does NOT accumulate), then delegate every accumulating
    // field to MergeAggregateInto so there is ONE field list to maintain.
    // RemovedSnapshot is shared by reference (an immutable removal snapshot).
    internal static CardAggregate CloneAggregate(CardAggregate source)
    {
        var clone = new CardAggregate
        {
            FloorAdded = source.FloorAdded,
            InitialUpgradeLevel = source.InitialUpgradeLevel,
            Removed = source.Removed,
            RemovedAtFloor = source.RemovedAtFloor,
            RemovedSnapshot = source.RemovedSnapshot,
        };
        MergeAggregateInto(clone, source);
        return clone;
    }

    internal static void MergeAggregateInto(CardAggregate target, CardAggregate source)
    {
        target.CombatsInDeck += source.CombatsInDeck;
        target.Plays += source.Plays;
        target.TotalIntended += source.TotalIntended;
        target.TotalBlocked += source.TotalBlocked;
        target.TotalOverkill += source.TotalOverkill;
        target.TotalEffective += source.TotalEffective;
        target.Kills += source.Kills;
        target.TotalEnergySpent += source.TotalEnergySpent;
        target.TotalEnergyGenerated += source.TotalEnergyGenerated;
        target.TotalStarsSpent += source.TotalStarsSpent;
        target.TotalStarsGenerated += source.TotalStarsGenerated;
        target.TotalForgeGenerated += source.TotalForgeGenerated;
        target.TotalBlockGained += source.TotalBlockGained;
        target.TotalBlockEffective += source.TotalBlockEffective;
        target.TotalBlockWasted += source.TotalBlockWasted;
        target.TimesDrawn += source.TimesDrawn;
        target.TimesDiscarded += source.TimesDiscarded;
        target.TimesPlacedOnTopFromHand += source.TimesPlacedOnTopFromHand;
        target.TimesPlacedOnTopFromDiscard += source.TimesPlacedOnTopFromDiscard;
        target.TimesExhaustedOtherCards += source.TimesExhaustedOtherCards;
        target.TimesExhausted += source.TimesExhausted;
        target.TotalHpLost += source.TotalHpLost;
        target.TimesCardsDrawn += source.TimesCardsDrawn;
        target.TimesCardsDrawAttempted += source.TimesCardsDrawAttempted;
        target.TimesCardsDrawBlocked += source.TimesCardsDrawBlocked;
        target.TimesSummonedToHand += source.TimesSummonedToHand;
        target.TotalOstyHpAttackBonus += source.TotalOstyHpAttackBonus;
        target.TimesOstyHpAttackBonusApplied += source.TimesOstyHpAttackBonusApplied;
        target.TimesOstySummoned += source.TimesOstySummoned;
        target.TotalOstyHpSummoned += source.TotalOstyHpSummoned;
        target.TimesReplayExtraPlanned += source.TimesReplayExtraPlanned;
        target.TimesReplayExtraPlayed += source.TimesReplayExtraPlayed;
        target.TimesReplayAttackNoDamage += source.TimesReplayAttackNoDamage;
        MergeBlockedDrawReasonsInto(target.BlockedDrawReasons, source.BlockedDrawReasons);
        MergeReplayExtraPlayReasonsInto(target.ReplayExtraPlayPlannedReasons, source.ReplayExtraPlayPlannedReasons);
        MergeReplayExtraPlayReasonsInto(target.ReplayExtraPlayReasons, source.ReplayExtraPlayReasons);
        MergeReplayExtraPlayReasonsInto(target.ReplayAttackNoDamageReasons, source.ReplayAttackNoDamageReasons);
        MergeAppliedEffectsInto(target.AppliedEffects, source.AppliedEffects);
    }

    private static void MergeMetaStatsInto(RunMetaStats target, RunMetaStats? source)
    {
        if (source == null) return;

        target.TotalOstyHpSummoned += source.TotalOstyHpSummoned;
        target.TotalOstyDamageAbsorbed += source.TotalOstyDamageAbsorbed;
    }

    private static void MergeBlockedDrawReasonsInto(
        Dictionary<string, BlockedDrawReasonAggregate> target,
        Dictionary<string, BlockedDrawReasonAggregate> source)
    {
        foreach (var kv in source)
        {
            if (!target.TryGetValue(kv.Key, out var reason))
            {
                reason = new BlockedDrawReasonAggregate
                {
                    ReasonId = kv.Value.ReasonId,
                    DisplayName = kv.Value.DisplayName,
                };
                target[kv.Key] = reason;
            }

            reason.Count += kv.Value.Count;
            if (string.IsNullOrWhiteSpace(reason.DisplayName) && !string.IsNullOrWhiteSpace(kv.Value.DisplayName))
                reason.DisplayName = kv.Value.DisplayName;
        }
    }

    private static void MergeReplayExtraPlayReasonsInto(
        Dictionary<string, ReplayExtraPlayReasonAggregate> target,
        Dictionary<string, ReplayExtraPlayReasonAggregate> source)
    {
        foreach (var kv in source)
        {
            if (!target.TryGetValue(kv.Key, out var reason))
            {
                reason = new ReplayExtraPlayReasonAggregate
                {
                    ReasonId = kv.Value.ReasonId,
                    DisplayName = kv.Value.DisplayName,
                };
                target[kv.Key] = reason;
            }

            reason.Count += kv.Value.Count;
            if (string.IsNullOrWhiteSpace(reason.DisplayName) && !string.IsNullOrWhiteSpace(kv.Value.DisplayName))
                reason.DisplayName = kv.Value.DisplayName;
        }
    }

    private static void MergeAppliedEffectsInto(
        Dictionary<string, AppliedEffectAggregate> target,
        Dictionary<string, AppliedEffectAggregate> source)
    {
        foreach (var kv in source)
        {
            if (!target.TryGetValue(kv.Key, out var effect))
            {
                effect = new AppliedEffectAggregate
                {
                    EffectId = kv.Value.EffectId,
                    DisplayName = kv.Value.DisplayName,
                    IconPath = kv.Value.IconPath,
                };
                target[kv.Key] = effect;
            }

            effect.TimesApplied += kv.Value.TimesApplied;
            effect.TotalAmountApplied += kv.Value.TotalAmountApplied;
            effect.TimesBlockedByArtifact += kv.Value.TimesBlockedByArtifact;
            effect.TotalAmountBlockedByArtifact += kv.Value.TotalAmountBlockedByArtifact;
            effect.TotalTriggeredEffectiveDamage += kv.Value.TotalTriggeredEffectiveDamage;
            effect.TotalTriggeredOverkill += kv.Value.TotalTriggeredOverkill;
            effect.TotalTriggeredCardsDrawBlocked += kv.Value.TotalTriggeredCardsDrawBlocked;
            if (string.IsNullOrWhiteSpace(effect.DisplayName) && !string.IsNullOrWhiteSpace(kv.Value.DisplayName))
                effect.DisplayName = kv.Value.DisplayName;
            if (string.IsNullOrWhiteSpace(effect.IconPath) && !string.IsNullOrWhiteSpace(kv.Value.IconPath))
                effect.IconPath = kv.Value.IconPath;
        }
    }

    private static AppliedEffectAggregate GetOrCreateAppliedEffect(CardAggregate agg, PowerModel power)
    {
        var effectId = power.Id.ToString();
        return GetOrCreateAppliedEffect(agg, effectId, GetPowerDisplayName(power), GetPowerIconPath(power));
    }

    private static AppliedEffectAggregate GetOrCreateAppliedEffect(
        CardAggregate agg,
        string effectId,
        string displayName,
        string? iconPath)
    {
        if (!agg.AppliedEffects.TryGetValue(effectId, out var effect))
        {
            effect = new AppliedEffectAggregate
            {
                EffectId = effectId,
                DisplayName = displayName,
                IconPath = iconPath,
            };
            agg.AppliedEffects[effectId] = effect;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(effect.DisplayName) && !string.IsNullOrWhiteSpace(displayName))
                effect.DisplayName = displayName;
            if (string.IsNullOrWhiteSpace(effect.IconPath) && !string.IsNullOrWhiteSpace(iconPath))
                effect.IconPath = iconPath;
        }
        return effect;
    }

    private static string GetPowerDisplayName(PowerModel power)
    {
        try
        {
            var title = power.Title.GetFormattedText();
            if (!string.IsNullOrWhiteSpace(title)) return title;
        }
        catch { }
        try
        {
            var title = power.Title.GetRawText();
            if (!string.IsNullOrWhiteSpace(title)) return title;
        }
        catch { }
        return power.Id.ToString();
    }

    private static string? GetPowerIconPath(PowerModel power)
    {
        return !string.IsNullOrWhiteSpace(power.IconPath) ? power.IconPath : power.PackedIconPath;
    }

    private static bool IsPoisonPower(PowerModel power)
    {
        var effectId = power.Id.ToString();
        if (effectId.Contains("POISON", StringComparison.OrdinalIgnoreCase))
            return true;

        if (power.GetType().Name.Contains("Poison", StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(GetPowerDisplayName(power), "Poison", StringComparison.OrdinalIgnoreCase);
    }

    private static Creature? TryResolvePowerReceivedTarget(PowerReceivedEntry entry)
    {
        return TryResolveCreatureMember(
            entry,
            preferredNames: ["Target", "Receiver", "Creature", "Owner", "Holder"],
            excludedNames: ["Applier", "Giver", "Dealer"]);
    }

    private static Creature? TryResolvePoisonPowerTarget(object poisonPower)
    {
        return TryResolveCreatureMember(
            poisonPower,
            preferredNames: ["Target", "Receiver", "Creature", "Owner", "Holder"],
            excludedNames: []);
    }

    private static Creature? TryResolveCreatureMember(
        object source,
        IReadOnlyList<string> preferredNames,
        IReadOnlyCollection<string> excludedNames)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        return TryResolveCreatureMemberRecursive(source, preferredNames, excludedNames, depthRemaining: 2, visited);
    }

    private static bool TryReadCreatureMember(Type type, object source, string memberName, out Creature? creature)
    {
        var prop = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop != null && prop.CanRead && typeof(Creature).IsAssignableFrom(prop.PropertyType))
        {
            try
            {
                creature = prop.GetValue(source) as Creature;
                if (creature != null) return true;
            }
            catch { }
        }

        var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null && typeof(Creature).IsAssignableFrom(field.FieldType))
        {
            try
            {
                creature = field.GetValue(source) as Creature;
                if (creature != null) return true;
            }
            catch { }
        }

        creature = null;
        return false;
    }

    private static Creature? TryResolveCreatureMemberRecursive(
        object? source,
        IReadOnlyList<string> preferredNames,
        IReadOnlyCollection<string> excludedNames,
        int depthRemaining,
        HashSet<object> visited)
    {
        if (source == null) return null;
        if (source is Creature directCreature) return directCreature;
        if (depthRemaining < 0) return null;
        if (!visited.Add(source)) return null;

        var type = source.GetType();
        foreach (var name in preferredNames)
        {
            if (TryReadCreatureMember(type, source, name, out var preferredCreature))
                return preferredCreature;
        }

        var candidates = new List<Creature>();
        var nestedValues = new List<object>();

        foreach (var prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!prop.CanRead) continue;
            if (excludedNames.Contains(prop.Name)) continue;

            object? value;
            try { value = prop.GetValue(source); }
            catch { continue; }

            if (value == null) continue;
            if (value is Creature propCreature)
            {
                candidates.Add(propCreature);
                continue;
            }

            if (!IsSimpleObject(value))
                nestedValues.Add(value);
        }

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (excludedNames.Contains(field.Name)) continue;

            object? value;
            try { value = field.GetValue(source); }
            catch { continue; }

            if (value == null) continue;
            if (value is Creature fieldCreature)
            {
                candidates.Add(fieldCreature);
                continue;
            }

            if (!IsSimpleObject(value))
                nestedValues.Add(value);
        }

        var distinctCandidates = candidates.Distinct().ToList();
        if (distinctCandidates.Count == 1) return distinctCandidates[0];

        if (depthRemaining == 0) return null;

        foreach (var nested in nestedValues)
        {
            var nestedCreature = TryResolveCreatureMemberRecursive(
                nested,
                preferredNames,
                excludedNames,
                depthRemaining - 1,
                visited);
            if (nestedCreature != null)
                return nestedCreature;
        }

        return null;
    }

    private static bool IsSimpleObject(object value)
    {
        var type = value.GetType();
        return type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(TimeSpan)
            || type == typeof(Guid);
    }

    private static bool AreClose(decimal left, decimal right)
    {
        return decimal.Abs(left - right) <= PoisonOwnershipEpsilon;
    }

    private static string Now() => DateTime.UtcNow.ToString("o");

    /// <summary>
    /// Compact description of a Creature for diagnostic logs. Returns
    /// "player/CHARACTER.DEFECT" or "MONSTER.LEAF_SLIME_M" or "null" so
    /// log lines stay greppable alongside the events JSON.
    /// </summary>
    private static string DescribeCreature(MegaCrit.Sts2.Core.Entities.Creatures.Creature? c)
    {
        if (c == null) return "null";
        try
        {
            if (c.IsPlayer) return $"player/{c.Player?.Character?.Id}";
            return c.Monster?.Id.ToString() ?? "monster?";
        }
        catch
        {
            return "err";
        }
    }
}

internal sealed class PendingPaelSacrificeReward
{
    public int CommonCards { get; private set; }
    public int UncommonCards { get; private set; }
    public int RareCards { get; private set; }

    public static PendingPaelSacrificeReward FromCards(IEnumerable<CardModel>? cards)
    {
        var result = new PendingPaelSacrificeReward();
        if (cards == null) return result;

        foreach (var card in cards)
        {
            if (card == null) continue;
            switch (card.Rarity)
            {
                case CardRarity.Common:
                    result.CommonCards += 1;
                    break;
                case CardRarity.Uncommon:
                    result.UncommonCards += 1;
                    break;
                case CardRarity.Rare:
                    result.RareCards += 1;
                    break;
            }
        }

        return result;
    }
}

/// <summary>
/// Holds per-combat stats and events while a combat is in progress.
/// Discarded if the combat doesn't finish cleanly; promoted into the run on CombatEnded.
/// </summary>
internal class PendingCombat
{
    public int EtherealCardsPlayed { get; set; }
    public Dictionary<string, CardAggregate> CombatAggregates { get; } = new();
    public List<CardEvent> CombatEvents { get; } = new();
    public Dictionary<string, RelicAggregate> RelicAggregates { get; } = new();
    public Dictionary<string, EnemyAggregate> EnemyAggregates { get; } = new();
    public List<BlockChunk> PlayerBlockLedger { get; } = new();
    public Dictionary<AbstractModel, PlayerPowerOwnershipShare> PlayerPowerOwnershipByModifier { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<PowerModel, Dictionary<string, NoxiousFumesContributionShare>> NoxiousFumesContributionsByPower { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Creature, PendingNoxiousFumesApplicationWindow> PendingNoxiousFumesApplications { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Creature, Dictionary<PoisonOwnershipKey, PoisonOwnershipShare>> PoisonOwnershipByTarget { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Creature, PendingPoisonTick> PendingPoisonTicks { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, PendingBrilliantScarfDiscount> BrilliantScarfDiscountOffers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> HappyFlowerCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> NunchakuCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> NunchakuCombatEndChargeRecordedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> BrilliantScarfCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> MiniatureCannonCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> BookmarkCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> PaelsEyeCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> PaelsEyeActivationStartedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> CandelabraSecondTurnExcessRecordedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> GamblingChipDiscardAttributionPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> CentennialPuzzleDrawsRemaining { get; }
        = new(ReferenceEqualityComparer.Instance);
    public RunMetaStats MetaStats { get; } = new();

    /// <summary>Exclusive per-kind attribution windows for this combat. A fresh
    /// PendingCombat (setup/end, run boundary) starts empty, so this IS the reset.</summary>
    public AttributionWindowRegistry Windows { get; } = new();
}

internal sealed class PendingBrilliantScarfDiscount
{
    public int TurnNumber { get; init; }
    public Dictionary<CardModel, PendingBrilliantScarfCardSaving> SavingsByCard { get; }
        = new(ReferenceEqualityComparer.Instance);
}

internal sealed class PendingBrilliantScarfCardSaving
{
    public int EnergySaved { get; set; }
    public int StarsSaved { get; set; }
}

internal sealed class EnemyStatusSourceFrame
{
    public required Creature Source { get; init; }
    public EnemyStatusSourceFrame? Previous { get; init; }
}

internal sealed class BlockChunk
{
    public string? CardInstanceId { get; init; }
    public int Remaining { get; set; }
    public bool CountsForCardStats => CardInstanceId != null;
}

internal sealed class PendingPowerChangeAttempt
{
    public required PowerModel Power { get; init; }
    public required Creature Target { get; init; }
    public Creature? Applier { get; init; }
    public required decimal RequestedAmount { get; init; }
    public CardModel? CardSource { get; init; }
}

internal sealed class PendingDrawAttempt
{
    public required Player Player { get; init; }
    public required CardModel SourceCard { get; init; }
}

internal sealed class PendingReplayExtraPlaySource
{
    public required string ReasonId { get; init; }
    public required string DisplayName { get; init; }
    public int Count { get; set; }
}

internal sealed class PendingReplayAttackOutcome
{
    public bool HasDamage { get; set; }
}

internal sealed class PendingRelicHealing
{
    public required string RelicId { get; init; }
    public required Creature Creature { get; init; }
    public required decimal Attempted { get; init; }
    public required decimal InitialCurrentHp { get; init; }
    public required decimal InitialMissingHp { get; init; }
    public bool PersistDirectlyToRun { get; init; }
    public decimal ActualRestored { get; set; }
}

internal sealed class PendingRegalPillowRestHeal
{
    public required decimal IncomingHealAmount { get; init; }
    public required decimal AttemptedBonusHealing { get; init; }
    public required decimal InitialCurrentHp { get; init; }
    public required decimal InitialMissingHp { get; init; }
}

internal sealed class PendingPrecariousShearsPickup
{
    public required decimal StartingMaxHp { get; init; }
    public List<string> CardsRemoved { get; } = new();
}

internal sealed class PendingSandCastlePickup
{
    public List<string> UpgradedCards { get; } = new();
}

internal sealed class PendingWhetstonePickup
{
    public List<string> UpgradedCards { get; } = new();
}

internal sealed class PendingWarPaintPickup
{
    public List<string> UpgradedCards { get; } = new();
}

internal sealed class PlayerPowerOwnershipShare
{
    public required string CardInstanceId { get; init; }
    public required string EffectId { get; init; }
    public required string DisplayName { get; init; }
    public string? IconPath { get; init; }
}

internal sealed class PendingBronzeScalesDamageAttribution
{
    public PendingBronzeScalesDamageAttribution(
        Creature thornsOwner,
        Creature damageTarget,
        decimal totalAmount,
        decimal attributedAmount)
    {
        ThornsOwner = thornsOwner;
        DamageTarget = damageTarget;
        TotalAmount = totalAmount;
        AttributedAmount = attributedAmount;
    }

    public Creature ThornsOwner { get; }
    public Creature DamageTarget { get; }
    public decimal TotalAmount { get; }
    public decimal AttributedAmount { get; }
}

internal readonly record struct PoisonOwnershipKey(string CardInstanceId, string EffectId);

internal sealed class PoisonOwnershipShare
{
    public required PoisonOwnershipKey Key { get; init; }
    public required string CardInstanceId { get; init; }
    public required string EffectId { get; init; }
    public required string DisplayName { get; init; }
    public string? IconPath { get; init; }
    public decimal Amount { get; set; }
}

internal sealed class PendingPoisonTick
{
    public int ArmedAtHistoryCount { get; init; }
}

internal sealed class PendingNoxiousFumesApplicationWindow
{
    public List<NoxiousFumesContributionShare> Contributions { get; } = new();
    public decimal ExpectedAmount { get; set; }
    public int RemainingApplications { get; set; }
}

internal sealed class NoxiousFumesContributionShare
{
    public string CardInstanceId { get; set; } = "";
    public decimal Amount { get; set; }
}

internal sealed class NoxiousFumesContributionAllocation
{
    public string CardInstanceId { get; set; } = "";
    public decimal Amount { get; set; }
}
