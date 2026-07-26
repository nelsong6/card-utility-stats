using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
    private const string CursedPearlCurseDefinitionId = "CARD.GREED";
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
    private static readonly List<PendingUnsettlingLampDebuff> _pendingUnsettlingLampDebuffs = new();
    private static readonly System.Threading.AsyncLocal<EnemyStatusSourceFrame?> _enemyStatusSourceFrame = new();
    private static readonly System.Threading.AsyncLocal<PendingToastyMittensActivation?> _toastyMittensActivation = new();
    private static int _pendingPlayerBlockClearAmount;
    private static bool _pendingPlayerBlockClearArmed;
    private static bool _pendingAkabekoVigorAttribution;
    private static readonly List<PendingRelicHealing> _pendingRelicHeals = new();
    private static readonly List<Player> _pendingPendulumDrawAttributions = new();
    private static readonly List<Creature> _pendingParryingShieldDamageAttributions = new();
    private static readonly List<Creature> _pendingKusarigamaDamageAttributions = new();
    private static readonly List<Creature> _pendingFestivePopperDamageAttributions = new();
    private static readonly List<Creature> _pendingOrnamentalFanBlockAttributions = new();
    private static readonly List<Creature> _pendingIntimidatingHelmetBlockAttributions = new();
    private static readonly List<Creature> _pendingDaughterOfTheWindBlockAttributions = new();
    private static readonly List<PendingDanseMacabreBlockAttribution> _pendingDanseMacabreBlockAttributions = new();
    private static readonly List<Creature> _pendingMercuryHourglassDamageAttributions = new();
    private static readonly List<Creature> _pendingMrStrugglesDamageAttributions = new();
    private static readonly Dictionary<PowerModel, int> _bronzeScalesThornsContributions = new(ReferenceEqualityComparer.Instance);
    private static readonly List<PendingBronzeScalesDamageAttribution> _pendingBronzeScalesDamageAttributions = new();
    private static readonly List<Creature> _pendingHornCleatBlockAttributions = new();
    private static readonly List<Creature> _pendingCaptainsWheelBlockAttributions = new();
    private static readonly Dictionary<string, int> _lastEnergyResetRoundByRelicAndPlayer = new();
    private static int _pendingWhiteBeastPotionRewards;
    private static int _pendingToolboxOfferScreens;
    private static readonly List<Player> _pendingHeftyTabletChoicePlayers = new();
    private static readonly HashSet<PotionReward> _whiteBeastPotionRewards = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<CardReward, PendingPaelSacrificeReward> _paelSacrificeRewards = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<CardReward, PendingFresnelLensReward> _fresnelLensRewards = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<CardReward, PendingSilverCrucibleReward> _silverCrucibleRewards = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<CardReward, PendingOrreryReward> _orreryRewards = new(ReferenceEqualityComparer.Instance);
    private static Orrery? _orreryRewardRegistrationRelic;
    private static readonly List<int> _silverCrucibleRestoreBatchScreenNumbers = new();
    private static int _silverCrucibleRestoreBatchDepth;
    private static readonly Dictionary<Player, PendingRegalPillowRestHeal> _pendingRegalPillowRestHeals = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Player, PendingPrecariousShearsPickup> _pendingPrecariousShearsPickups = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<Player> _pendingLeafyPoulticePickups = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Player, PendingSandCastlePickup> _pendingSandCastlePickups = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Player, PendingWhetstonePickup> _pendingWhetstonePickups = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Player, PendingWarPaintPickup> _pendingWarPaintPickups = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Player, PendingFragrantMushroomPickup> _pendingFragrantMushroomPickups = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Player, PendingFishingRodUpgrade> _pendingFishingRodUpgrades = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Player, PendingWarHammerActivation> _pendingWarHammerActivations = new(ReferenceEqualityComparer.Instance);
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

    public static bool AreCardStatsDisabledForActiveCombat()
    {
        lock (_lock)
        {
            return !ShouldTrackCardStatsDuringCombatLocked();
        }
    }

    private static bool ShouldTrackCardStatsDuringCombatLocked()
    {
        return ShouldTrackCardStatsDuringCombat(
            RuntimeOptionsProvider.Current.DisableCardStatsDuringCombat,
            _pendingCombat != null || CombatManager.Instance?.IsInProgress == true);
    }

    internal static bool ShouldTrackCardStatsDuringCombatForTest(
        bool disableCardStatsDuringCombat,
        bool combatActive)
        => ShouldTrackCardStatsDuringCombat(disableCardStatsDuringCombat, combatActive);

    private static bool ShouldTrackCardStatsDuringCombat(
        bool disableCardStatsDuringCombat,
        bool combatActive)
        => !disableCardStatsDuringCombat || !combatActive;

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

    public static CardAggregate GetCursedPearlCurseAggregate()
    {
        lock (_lock)
        {
            return GetPooledEffectiveAggregateByDefinitionLocked(CursedPearlCurseDefinitionId)
                   ?? new CardAggregate();
        }
    }

    public static CardAggregate GetPooledCardAggregateByDefinition(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId)) return new CardAggregate();

        lock (_lock)
        {
            return GetPooledEffectiveAggregateByDefinitionLocked(definitionId)
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
    [MemberNotNull(nameof(_currentRun))]
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
        _pendingUnsettlingLampDebuffs.Clear();
        _toastyMittensActivation.Value = null;
        _pendingPlayerBlockClearAmount = 0;
        _pendingPlayerBlockClearArmed = false;
        // Ported windows (Orichalcum, Anchor, Abacus, BoneFlute, CloakClasp,
        // HappyFlower, BoomingConch, GremlinHorn energy+draw) now live on
        // PendingCombat.Windows and reset with a fresh PendingCombat.
        // DEFERRED (not ported this pass — keep their own reset):
        _pendingAkabekoVigorAttribution = false;
        _pendingPendulumDrawAttributions.Clear();
        _pendingParryingShieldDamageAttributions.Clear();
        _pendingKusarigamaDamageAttributions.Clear();
        _pendingFestivePopperDamageAttributions.Clear();
        _pendingOrnamentalFanBlockAttributions.Clear();
        _pendingIntimidatingHelmetBlockAttributions.Clear();
        _pendingDaughterOfTheWindBlockAttributions.Clear();
        _pendingDanseMacabreBlockAttributions.Clear();
        _pendingMercuryHourglassDamageAttributions.Clear();
        _pendingMrStrugglesDamageAttributions.Clear();
        _bronzeScalesThornsContributions.Clear();
        _pendingBronzeScalesDamageAttributions.Clear();
        _pendingHornCleatBlockAttributions.Clear();
        _pendingCaptainsWheelBlockAttributions.Clear();
        _lastEnergyResetRoundByRelicAndPlayer.Clear();
        _pendingWarHammerActivations.Clear();
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
        _fresnelLensRewards.Clear();
        _silverCrucibleRewards.Clear();
        _orreryRewards.Clear();
        _orreryRewardRegistrationRelic = null;
        _silverCrucibleRestoreBatchScreenNumbers.Clear();
        _silverCrucibleRestoreBatchDepth = 0;
        _pendingRegalPillowRestHeals.Clear();
        _pendingPrecariousShearsPickups.Clear();
        _pendingLeafyPoulticePickups.Clear();
        _pendingSandCastlePickups.Clear();
        _pendingWhetstonePickups.Clear();
        _pendingWarPaintPickups.Clear();
        _pendingFragrantMushroomPickups.Clear();
        _pendingFishingRodUpgrades.Clear();
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
        bool dowsingRoomsChanged = RefreshDowsingRoomsRemainingIfOwnedLocked(player);
        bool paelsClawSnapshotChanged = RefreshPaelsClawSnapshotIfOwnedLocked();
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
            || miniatureCannonDeckCountsChanged
            || dowsingRoomsChanged
            || paelsClawSnapshotChanged)
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
            RuntimeOptionsProvider.Refresh();
            // Fresh pending buffer for this combat. Anything accumulated from a prior
            // combat that didn't get a CombatEnded (shouldn't happen but defensive) is dropped.
            _pendingCombat = new PendingCombat();
            ResetCombatContextState();
            RecordPantographCombatStartForTrackedPlayerLocked(state);
            if (ShouldTrackCardStatsDuringCombatLocked())
            {
                RecordCombatsInDeckForCurrentDeckLocked();
            }
            else
            {
                CoreMain.Logger.Info("SpireLens card combat stats are disabled for this combat.");
            }
            if (RefreshPaelsClawSnapshotIfOwnedLocked())
                SaveCurrentRun();
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
        RecordLetterOpenerTurnForTrackedPlayerLocked();
        RecordTuningForkTurnForTrackedPlayerLocked();
        RecordCloakClaspTurnForTrackedPlayerLocked();
        RecordEmberTeaActiveTurnForTrackedPlayerLocked();
        RecordRippleBasinTurnForTrackedPlayerLocked();
        RecordReptileTrinketTurnForTrackedPlayerLocked();
        RecordBeatingRemnantTurnForTrackedPlayerLocked();
        RecordStoneCrackerTurnForTrackedPlayerLocked();
        RecordPaperPhrogTurnForTrackedPlayerLocked();
        RecordRazorToothTurnForTrackedPlayerLocked();
        RecordWarHammerTurnForTrackedPlayerLocked();
        RecordPaelsClawTurnForTrackedPlayerLocked();
        RecordMummifiedHandTurnForTrackedPlayerLocked();
        RecordBrilliantScarfTurnForTrackedPlayerLocked();
        RecordPaelsEyeCombatsWithoutActivationForTrackedPlayerLocked();
        RecordTurnEnergyRelicCombatsWithoutEnergyForTrackedPlayerLocked();
        RecordPendulumCombatEndChargeForTrackedPlayerLocked();
        RecordNunchakuCombatEndChargeForTrackedPlayerLocked();
        RecordIronClubCombatEndChargeForTrackedPlayerLocked();

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
        MergeAppliedEffectsInto(target.AppliedEffects, source.AppliedEffects);
        target.AdditionalCardsDrawn += source.AdditionalCardsDrawn;
        target.PendulumCombats += source.PendulumCombats;
        target.PendulumCombatsEndedOn0Charges += source.PendulumCombatsEndedOn0Charges;
        target.PendulumCombatsEndedOn1Charge += source.PendulumCombatsEndedOn1Charge;
        target.PendulumCombatsEndedOn2Charges += source.PendulumCombatsEndedOn2Charges;
        target.PendulumCombatEndChargeTotal += source.PendulumCombatEndChargeTotal;
        target.PendulumCombatEndChargeCount += source.PendulumCombatEndChargeCount;
        target.AdditionalBlockGained += source.AdditionalBlockGained;
        target.CloakClaspTurns += source.CloakClaspTurns;
        target.CloakClaspCombats += source.CloakClaspCombats;
        target.PermafrostCombats += source.PermafrostCombats;
        target.BlockedTriggers += source.BlockedTriggers;
        target.StrengthAdded += source.StrengthAdded;
        target.ToastyMittensCardsExhausted += source.ToastyMittensCardsExhausted;
        target.ToastyMittensCombats += source.ToastyMittensCombats;
        target.ReptileTrinketTurns += source.ReptileTrinketTurns;
        target.ReptileTrinketCombats += source.ReptileTrinketCombats;
        target.ReptileTrinketTurnsWithExactlyTwoActivations +=
            source.ReptileTrinketTurnsWithExactlyTwoActivations;
        target.ReptileTrinketTurnsWithMoreThanTwoActivations +=
            source.ReptileTrinketTurnsWithMoreThanTwoActivations;
        target.BeatingRemnantHpLossPrevented += source.BeatingRemnantHpLossPrevented;
        target.BeatingRemnantTurns += source.BeatingRemnantTurns;
        target.BeatingRemnantCombats += source.BeatingRemnantCombats;
        target.PlatingAdded += source.PlatingAdded;
        target.CardsUpgraded += source.CardsUpgraded;
        MergeUpgradedCardsInto(target, source);
        target.StoneCrackerUpgradedCommons += source.StoneCrackerUpgradedCommons;
        target.StoneCrackerUpgradedUncommons += source.StoneCrackerUpgradedUncommons;
        target.StoneCrackerUpgradedRares += source.StoneCrackerUpgradedRares;
        target.StoneCrackerUpgradedCardPlays += source.StoneCrackerUpgradedCardPlays;
        target.StoneCrackerCombats += source.StoneCrackerCombats;
        target.StoneCrackerTurns += source.StoneCrackerTurns;
        MergeWarHammerUpgradedCardInstanceIdsInto(target, source);
        target.WarHammerUpgradedCardPlays += source.WarHammerUpgradedCardPlays;
        target.WarHammerCombats += source.WarHammerCombats;
        target.WarHammerTurns += source.WarHammerTurns;
        MergeSharpEnchantedCardsInto(target, source);
        MergeTriBoomerangInstinctCardsInto(target, source);
        target.TriBoomerangInstinctCardPlays += source.TriBoomerangInstinctCardPlays;
        target.TriBoomerangCombats += source.TriBoomerangCombats;
        target.RazorToothCombats += source.RazorToothCombats;
        target.RazorToothTurns += source.RazorToothTurns;
        target.RazorToothUpgradedCardPlays += source.RazorToothUpgradedCardPlays;
        target.RazorToothUpgradedCardDraws += source.RazorToothUpgradedCardDraws;
        target.BoneFluteTriggers += source.BoneFluteTriggers;
        target.TotalOstyHpSummoned += source.TotalOstyHpSummoned;
        target.CursesAcquired += source.CursesAcquired;
        target.TotalMaxHpGained += source.TotalMaxHpGained;
        target.TotalHealingAttempted += source.TotalHealingAttempted;
        target.TotalHealingRestored += source.TotalHealingRestored;
        target.TotalHealingLost += source.TotalHealingLost;
        MergeHealingLostReasonsInto(target, source);
        if (source.FloorAcquired.HasValue && !target.FloorAcquired.HasValue)
            target.FloorAcquired = source.FloorAcquired;
        if (source.FloorActivated.HasValue)
            target.FloorActivated = source.FloorActivated;
        target.MaxHpGained += source.MaxHpGained;
        MergeRelicMaxHpActivations(target, source);
        if (source.OriginalMaxHp.HasValue && !target.OriginalMaxHp.HasValue)
            target.OriginalMaxHp = source.OriginalMaxHp;
        if (source.NewMaxHp.HasValue)
            target.NewMaxHp = source.NewMaxHp;
        target.DoomDeathTriggers += source.DoomDeathTriggers;
        target.DoomKills += source.DoomKills;
        target.EnergyGenerated += source.EnergyGenerated;
        target.GoldGained += source.GoldGained;
        target.CardsAddedToDeck += source.CardsAddedToDeck;
        target.CardRewardsSkipped += source.CardRewardsSkipped;
        target.GoldLost += source.GoldLost;
        target.GoldLossBlocked += source.GoldLossBlocked;
        target.EnergyGeneratedCombats += source.EnergyGeneratedCombats;
        target.ArtOfWarTurns += source.ArtOfWarTurns;
        target.CrackedCoreOrbEvokes += source.CrackedCoreOrbEvokes;
        target.CrackedCoreOrbPassiveTriggers += source.CrackedCoreOrbPassiveTriggers;
        target.CrackedCoreOrbFizzles += source.CrackedCoreOrbFizzles;
        target.FirstTurnsEndedWithExcessEnergy += source.FirstTurnsEndedWithExcessEnergy;
        target.SecondTurnsEndedWithExcessEnergy += source.SecondTurnsEndedWithExcessEnergy;
        target.ThirdTurnsEndedWithExcessEnergy += source.ThirdTurnsEndedWithExcessEnergy;
        target.VigorGained += source.VigorGained;
        target.PenNibAttacksPlayed += source.PenNibAttacksPlayed;
        target.PenNibTurnsEndedOn8Charges += source.PenNibTurnsEndedOn8Charges;
        target.PenNibTurnsEndedOn9Charges += source.PenNibTurnsEndedOn9Charges;
        target.PenNibTurnEndChargeTotal += source.PenNibTurnEndChargeTotal;
        target.PenNibTurnEndChargeCount += source.PenNibTurnEndChargeCount;
        target.TotalDamageAttempted += source.TotalDamageAttempted;
        target.TotalDamageDealt += source.TotalDamageDealt;
        target.TotalDamageBlocked += source.TotalDamageBlocked;
        target.TotalDamageOverkill += source.TotalDamageOverkill;
        target.Kills += source.Kills;
        target.TotalTargets += source.TotalTargets;
        target.LetterOpenerSkillsPlayed += source.LetterOpenerSkillsPlayed;
        target.LetterOpenerCombats += source.LetterOpenerCombats;
        target.LetterOpenerTurns += source.LetterOpenerTurns;
        target.LetterOpenerTurnsEndedAt1Charge += source.LetterOpenerTurnsEndedAt1Charge;
        target.LetterOpenerTurnsEndedAt2Charges += source.LetterOpenerTurnsEndedAt2Charges;
        target.TuningForkSkillsPlayed += source.TuningForkSkillsPlayed;
        target.TuningForkCombats += source.TuningForkCombats;
        target.TuningForkTurns += source.TuningForkTurns;
        target.TuningForkTurnsEndedOn8Charges += source.TuningForkTurnsEndedOn8Charges;
        target.TuningForkTurnsEndedOn9Charges += source.TuningForkTurnsEndedOn9Charges;
        target.TuningForkTurnEndChargeTotal += source.TuningForkTurnEndChargeTotal;
        target.TuningForkTurnEndChargeCount += source.TuningForkTurnEndChargeCount;
        target.RippleBasinCombats += source.RippleBasinCombats;
        target.RippleBasinTurns += source.RippleBasinTurns;
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
        MergeRelicsGranted(target.RelicsGranted, source.RelicsGranted);
        target.UncommonCardsOffered += source.UncommonCardsOffered;
        target.RareCardsOffered += source.RareCardsOffered;
        target.UncommonCardsTaken += source.UncommonCardsTaken;
        target.RareCardsTaken += source.RareCardsTaken;
        target.UpgradedCardsOffered += source.UpgradedCardsOffered;
        target.CommonCardsConsumed += source.CommonCardsConsumed;
        target.UncommonCardsConsumed += source.UncommonCardsConsumed;
        target.RareCardsConsumed += source.RareCardsConsumed;
        target.SacrificesMade += source.SacrificesMade;
        target.SacrificesSkipped += source.SacrificesSkipped;
        target.PaelsClawGoopyCardsPlayed += source.PaelsClawGoopyCardsPlayed;
        target.PaelsClawGoopyEnhancements += source.PaelsClawGoopyEnhancements;
        target.PaelsClawGoopyCards = Math.Max(target.PaelsClawGoopyCards, source.PaelsClawGoopyCards);
        target.PaelsClawTurns += source.PaelsClawTurns;
        target.PaelsClawCombats += source.PaelsClawCombats;
        target.StatusCardsExhausted += source.StatusCardsExhausted;
        target.CurseCardsExhausted += source.CurseCardsExhausted;
        target.CombatsWithoutActivation += source.CombatsWithoutActivation;

        target.StrikeDummyStrikesPlayed += source.StrikeDummyStrikesPlayed;
        if (source.StrikeDummyBaseStrikesInDeck != 0 || target.StrikeDummyBaseStrikesInDeck == 0)
            target.StrikeDummyBaseStrikesInDeck = source.StrikeDummyBaseStrikesInDeck;
        if (source.StrikeDummyNonBaseStrikeCardsInDeck != 0 || target.StrikeDummyNonBaseStrikeCardsInDeck == 0)
            target.StrikeDummyNonBaseStrikeCardsInDeck = source.StrikeDummyNonBaseStrikeCardsInDeck;

        target.NutritiousSoupEnchantedStrikesPlayed += source.NutritiousSoupEnchantedStrikesPlayed;

        if (source.MiniatureCannonUpgradedAttacksInDeck != 0 || target.MiniatureCannonUpgradedAttacksInDeck == 0)
            target.MiniatureCannonUpgradedAttacksInDeck = source.MiniatureCannonUpgradedAttacksInDeck;
        if (source.MiniatureCannonNonUpgradedAttacksInDeck != 0 || target.MiniatureCannonNonUpgradedAttacksInDeck == 0)
            target.MiniatureCannonNonUpgradedAttacksInDeck = source.MiniatureCannonNonUpgradedAttacksInDeck;
        target.MiniatureCannonUpgradedAttackPlays += source.MiniatureCannonUpgradedAttackPlays;
        target.MiniatureCannonUpgradedAttackHits += source.MiniatureCannonUpgradedAttackHits;
        target.VajraAttacksPlayed += source.VajraAttacksPlayed;
        target.VajraAttackHits += source.VajraAttackHits;
        target.EmberTeaAttacksPlayedWhileActive += source.EmberTeaAttacksPlayedWhileActive;
        target.EmberTeaHitsWhileActive += source.EmberTeaHitsWhileActive;
        target.EmberTeaActiveTurns += source.EmberTeaActiveTurns;
        target.EmberTeaActiveCombats += source.EmberTeaActiveCombats;
        target.KunaiAttacksPlayed += source.KunaiAttacksPlayed;
        target.KunaiDexterityGained += source.KunaiDexterityGained;
        target.KunaiTurnsEndedAt1Charge += source.KunaiTurnsEndedAt1Charge;
        target.KunaiTurnsEndedAt2Charges += source.KunaiTurnsEndedAt2Charges;
        target.KunaiTurnEndChargeTotal += source.KunaiTurnEndChargeTotal;
        target.KunaiTurnEndChargeCount += source.KunaiTurnEndChargeCount;
        target.KusarigamaAttacksPlayed += source.KusarigamaAttacksPlayed;
        target.KusarigamaTurnsEndedAt1Charge += source.KusarigamaTurnsEndedAt1Charge;
        target.KusarigamaTurnsEndedAt2Charges += source.KusarigamaTurnsEndedAt2Charges;
        target.KusarigamaTurnEndChargeTotal += source.KusarigamaTurnEndChargeTotal;
        target.KusarigamaTurnEndChargeCount += source.KusarigamaTurnEndChargeCount;
        target.OrnamentalFanAttacksPlayed += source.OrnamentalFanAttacksPlayed;
        target.OrnamentalFanTurnsEndedAt0Charges += source.OrnamentalFanTurnsEndedAt0Charges;
        target.OrnamentalFanTurnsEndedAt1Charge += source.OrnamentalFanTurnsEndedAt1Charge;
        target.OrnamentalFanTurnsEndedAt2Charges += source.OrnamentalFanTurnsEndedAt2Charges;
        target.OrnamentalFanTurnEndChargeTotal += source.OrnamentalFanTurnEndChargeTotal;
        target.OrnamentalFanTurnEndChargeCount += source.OrnamentalFanTurnEndChargeCount;
        target.ShurikenAttacksPlayed += source.ShurikenAttacksPlayed;
        target.ShurikenTurnsEndedAt1Charge += source.ShurikenTurnsEndedAt1Charge;
        target.ShurikenTurnsEndedAt2Charges += source.ShurikenTurnsEndedAt2Charges;
        target.ShurikenTurnEndChargeTotal += source.ShurikenTurnEndChargeTotal;
        target.ShurikenTurnEndChargeCount += source.ShurikenTurnEndChargeCount;
        target.PaperPhrogDamageAdded += source.PaperPhrogDamageAdded;
        target.PaperPhrogEnhancedAttacks += source.PaperPhrogEnhancedAttacks;
        target.PaperPhrogCombats += source.PaperPhrogCombats;
        target.PaperPhrogTurns += source.PaperPhrogTurns;
        target.RegaliteCardsCreated += source.RegaliteCardsCreated;
        target.RegaliteCombats += source.RegaliteCombats;
        target.RegaliteTurns += source.RegaliteTurns;
        target.IntimidatingHelmetCombats += source.IntimidatingHelmetCombats;
        target.IntimidatingHelmetTurns += source.IntimidatingHelmetTurns;
        target.DaughterOfTheWindCombats += source.DaughterOfTheWindCombats;
        target.DaughterOfTheWindTurns += source.DaughterOfTheWindTurns;
        target.SturdyClampBlockRetained += source.SturdyClampBlockRetained;
        target.SturdyClampExcessBlockOverTen += source.SturdyClampExcessBlockOverTen;
        target.SturdyClampTurns += source.SturdyClampTurns;
        target.SturdyClampCombats += source.SturdyClampCombats;
        target.RuinedHelmetCombats += source.RuinedHelmetCombats;
        target.MummifiedHandTriggeringPowerCostTotal += source.MummifiedHandTriggeringPowerCostTotal;
        target.MummifiedHandDiscountGivenTotal += source.MummifiedHandDiscountGivenTotal;
        target.MummifiedHandEnergySpentToDiscountedCostRatioTotal += source.MummifiedHandEnergySpentToDiscountedCostRatioTotal;
        target.MummifiedHandEnergySpentToDiscountedCostRatioCount += source.MummifiedHandEnergySpentToDiscountedCostRatioCount;
        target.MummifiedHandCombats += source.MummifiedHandCombats;
        target.MummifiedHandTurns += source.MummifiedHandTurns;
        target.MummifiedHandDiscountedPowers += source.MummifiedHandDiscountedPowers;
        target.MummifiedHandDiscountedAttacks += source.MummifiedHandDiscountedAttacks;
        target.MummifiedHandDiscountedSkills += source.MummifiedHandDiscountedSkills;
        target.MummifiedHandDiscountedCommons += source.MummifiedHandDiscountedCommons;
        target.MummifiedHandDiscountedUncommons += source.MummifiedHandDiscountedUncommons;
        target.MummifiedHandDiscountedRares += source.MummifiedHandDiscountedRares;

        target.BookmarkCombats += source.BookmarkCombats;
        target.BookmarkCommonActivations += source.BookmarkCommonActivations;
        target.BookmarkUncommonActivations += source.BookmarkUncommonActivations;
        target.BookmarkRareActivations += source.BookmarkRareActivations;

        target.NunchakuAttacksPlayed += source.NunchakuAttacksPlayed;
        target.NunchakuCombatsEndedOn8Charges += source.NunchakuCombatsEndedOn8Charges;
        target.NunchakuCombatsEndedOn9Charges += source.NunchakuCombatsEndedOn9Charges;
        target.NunchakuCombatEndChargeTotal += source.NunchakuCombatEndChargeTotal;
        target.IronClubCombats += source.IronClubCombats;
        target.IronClubCombatsEndedOn0Charges += source.IronClubCombatsEndedOn0Charges;
        target.IronClubCombatsEndedOn1Charges += source.IronClubCombatsEndedOn1Charges;
        target.IronClubCombatsEndedOn2Charges += source.IronClubCombatsEndedOn2Charges;
        target.IronClubCombatsEndedOn3Charges += source.IronClubCombatsEndedOn3Charges;
        target.IronClubCombatEndChargeTotal += source.IronClubCombatEndChargeTotal;
        target.IronClubCombatEndChargeCount += source.IronClubCombatEndChargeCount;

        target.DiscountCombats += source.DiscountCombats;
        target.DiscountTurns += source.DiscountTurns;
        target.DiscountsOffered += source.DiscountsOffered;
        target.DiscountsTaken += source.DiscountsTaken;
        target.EnergySavedByDiscount += source.EnergySavedByDiscount;
        target.BrilliantScarfEnergySavedForTurnAverage +=
            source.BrilliantScarfEnergySavedForTurnAverage;
        MergeDiscountedCardCosts(target, source);
        target.CardsDiscarded += source.CardsDiscarded;
        target.QuestionMarkSitesEntered += source.QuestionMarkSitesEntered;
        if (source.DowsingQuestionRoomsRemaining.HasValue)
            target.DowsingQuestionRoomsRemaining = source.DowsingQuestionRoomsRemaining;
        if (source.FloorsAscendedBeforeFirstShop.HasValue && !target.FloorsAscendedBeforeFirstShop.HasValue)
            target.FloorsAscendedBeforeFirstShop = source.FloorsAscendedBeforeFirstShop;
        if (source.FloorsTraveledUntilNextShop.HasValue && !target.FloorsTraveledUntilNextShop.HasValue)
            target.FloorsTraveledUntilNextShop = source.FloorsTraveledUntilNextShop;
        MergeWingedBootsDestinations(target, source);
        MergeCardsRemovedInto(target, source);
        if (source.StartingMaxHp.HasValue) target.StartingMaxHp = source.StartingMaxHp;
        if (source.ResultingMaxHp.HasValue) target.ResultingMaxHp = source.ResultingMaxHp;
        target.CardRewardsAffected += source.CardRewardsAffected;
        target.NimbleCardsTaken += source.NimbleCardsTaken;
        target.RewardScreensWithNimbleCards += source.RewardScreensWithNimbleCards;
        target.RewardScreensWithTwoNimbleCards += source.RewardScreensWithTwoNimbleCards;
        target.RewardScreensWithThreeOrMoreNimbleCards += source.RewardScreensWithThreeOrMoreNimbleCards;
        target.RewardScreensWithoutNimbleCards += source.RewardScreensWithoutNimbleCards;
        target.RewardScreensWithNimbleCardsButNoneTaken += source.RewardScreensWithNimbleCardsButNoneTaken;
        MergeCardRewardScreens(target, source);
        MergeOrreryRewards(target, source);
        MergeCardRewardCategories(target.CardRewardCategories, source.CardRewardCategories);
        MergeRelicCardsGranted(target.CardsGranted, source.CardsGranted);
        MergeRelicCardsReturned(target, source);
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
            bool trackCardStats;
            lock (_lock)
            {
                trackCardStats = ShouldTrackCardStatsDuringCombatLocked();
            }

            switch (entry)
            {
                case CardPlayStartedEntry cps when cps.CardPlay != null:
                    if (trackCardStats)
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
                        if (trackCardStats)
                            NoteCardPlayFinished(cpf.CardPlay);
                        RecordCardPlay(cpf.CardPlay);
                    }
                    break;
                case CardDrawnEntry cde:
                    // Draw-hook validation is complete; keep the trace debug-gated.
                    CoreMain.LogDebug($"CardDrawnEntry card='{cde.Card?.Title ?? "null"}' fromHandDraw={cde.FromHandDraw}");
                    if (trackCardStats && cde.Card != null) RecordCardDrawn(cde);
                    break;
                case CardDiscardedEntry cdisc when cdisc.Card != null:
                    RecordCardDiscarded(cdisc.Card);
                    break;
                case CardExhaustedEntry cex when cex.Card != null:
                    if (trackCardStats)
                        RecordCardExhausted(cex.Card);
                    break;
                case BlockGainedEntry bge:
                    if (trackCardStats)
                        RecordBlockGainedEntry(bge);
                    break;
                case DamageReceivedEntry dre:
                    // Remember the result ref so the combat-ending capture
                    // (RecordCombatEndingSuppressedDamage) knows this hit was
                    // recorded normally and won't synthesize a duplicate.
                    TryMarkDamageResultObserved(dre.Result);

                    if (dre.Receiver.IsPlayer)
                    {
                        if (trackCardStats)
                            RecordPlayerBlockedDamage(dre);
                    }

                    RecordEnemyDamage(dre);

                    if (dre.CardSource != null)
                    {
                        if (trackCardStats)
                            CoreMain.LogDebug($"  -> RecordDamage from '{dre.CardSource.Title}' intended={dre.Result.BlockedDamage + dre.Result.UnblockedDamage} canonicalHash={Canonical(dre.CardSource).GetHashCode()}");
                        RecordDamageFromCard(dre);
                    }
                    else if (trackCardStats && !dre.Receiver.IsPlayer && TryRecordPoisonTickDamage(dre))
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

            RecordStoneCrackerUpgradedCardPlayedLocked(cardPlay.Card);
            RecordRazorToothUpgradedCardPlayedLocked(cardPlay.Card);
            RecordWarHammerUpgradedCardPlayedLocked(cardPlay.Card);
            RecordTriBoomerangInstinctCardPlayedLocked(cardPlay.Card);
            RecordStrikeDummyStrikePlayedIfOwnedLocked(cardPlay.Card);
            RecordNutritiousSoupEnchantedStrikePlayedIfOwnedLocked(cardPlay.Card);
            RecordMiniatureCannonUpgradedAttackPlayedIfOwnedLocked(cardPlay.Card);
            RecordVajraAttackPlayedIfOwnedLocked(cardPlay.Card);
            RecordEmberTeaAttackPlayedIfActiveLocked(cardPlay.Card);
            RecordBrilliantScarfDiscountTaken(cardPlay);
            RecordPaelsClawGoopyCardPlayedIfOwnedLocked(cardPlay.Card);

            if (!ShouldTrackCardStatsDuringCombatLocked()) return;
            RecordDrainPowerUpgradedCardPlayedLocked(cardPlay.Card);

            // Per-instance tracking: each physical card in the deck gets its
            // own aggregates bucket. First play assigns its instance id.
            var instanceId = GetOrAssignInstanceId(cardPlay.Card);

            var agg = GetOrCreateAggregate(_pendingCombat, instanceId);
            agg.Plays++;
            if (IsEtherealCard(cardPlay.Card))
                _pendingCombat.EtherealCardsPlayed++;
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
            if (!ShouldTrackCardStatsDuringCombatLocked()) return;
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
            if (!ShouldTrackCardStatsDuringCombatLocked()) return;
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
        if (!ShouldTrackCardStatsDuringCombatLocked()) return;

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
                if (!ShouldTrackCardStatsDuringCombatLocked()) return;

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
                if (!ShouldTrackCardStatsDuringCombatLocked()) return;

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
                if (!ShouldTrackCardStatsDuringCombatLocked()) return;

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

    /// <summary>
    /// Capture Alchemize's physical source card while its owner-specific play
    /// is still resolving. The potion command finishes asynchronously, so the
    /// source must be captured before awaiting its result.
    /// </summary>
    internal static CardModel? CaptureAlchemizePotionSource(Player? player)
    {
        if (player == null) return null;

        lock (_lock)
        {
            try
            {
                if (!ShouldTrackCardStatsDuringCombatLocked()) return null;
                if (!IsTrackedPlayer(player)) return null;

                var causingPlay = FindCurrentlyResolvingCardPlay();
                var sourceCard = causingPlay?.Card;
                if (sourceCard is not Alchemize) return null;
                if (sourceCard.Owner != null && !ReferenceEquals(sourceCard.Owner, player)) return null;

                return Canonical(sourceCard);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"CaptureAlchemizePotionSource failed: {e.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Record the observed result returned by Alchemize's potion procurement.
    /// A failed result is the card-side equivalent of White Beast Statue's
    /// skipped reward: the potion was generated but did not enter the belt.
    /// </summary>
    internal static void RecordAlchemizePotionResult(
        CardModel sourceCard,
        Player? player,
        PotionProcureResult? result)
    {
        if (player == null || result == null) return;

        lock (_lock)
        {
            try
            {
                if (!ShouldTrackCardStatsDuringCombatLocked()) return;
                if (!IsTrackedPlayer(player)) return;
                if (sourceCard is not Alchemize) return;
                if (sourceCard.Owner != null && !ReferenceEquals(sourceCard.Owner, player)) return;

                _pendingCombat ??= new PendingCombat();
                var instanceId = GetOrAssignInstanceId(sourceCard);
                var agg = GetOrCreateAggregate(_pendingCombat, instanceId);
                AccumulateAlchemizePotionResult(agg, result.success, result.potion?.Rarity);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordAlchemizePotionResult failed: {e.Message}");
            }
        }
    }

    private static void AccumulateAlchemizePotionResult(
        CardAggregate agg,
        bool success,
        PotionRarity? rarity)
    {
        if (!success)
        {
            agg.PotionsSkipped++;
            return;
        }

        agg.PotionsGained++;
        switch (rarity)
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
    }

    internal static void RecordAlchemizePotionResultForTest(
        CardAggregate agg,
        bool success,
        PotionRarity? rarity)
        => AccumulateAlchemizePotionResult(agg, success, rarity);

    /// <summary>
    /// Capture Jack of All Trades while its generated-card add command is
    /// still resolving. The command completes asynchronously, so the physical
    /// source must be retained before awaiting the observed add result.
    /// </summary>
    internal static CardModel? CaptureJackOfAllTradesSource(Player? player)
    {
        if (player == null) return null;

        lock (_lock)
        {
            try
            {
                if (!ShouldTrackCardStatsDuringCombatLocked()) return null;
                if (!IsTrackedPlayer(player)) return null;

                var causingPlay = FindCurrentlyResolvingCardPlay();
                var sourceCard = causingPlay?.Card;
                if (sourceCard is not JackOfAllTrades) return null;
                if (sourceCard.Owner != null && !ReferenceEquals(sourceCard.Owner, player)) return null;

                return Canonical(sourceCard);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"CaptureJackOfAllTradesSource failed: {e.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Record one successful generated-card arrival caused by Jack of All
    /// Trades. The result's card is the post-hook observed card that actually
    /// entered combat, so its final rarity, type, and cost are authoritative.
    /// </summary>
    internal static void RecordJackOfAllTradesCardAdded(
        CardModel sourceCard,
        Player? player,
        CardPileAddResult result)
    {
        if (player == null || !result.success || result.cardAdded == null) return;

        lock (_lock)
        {
            try
            {
                if (!ShouldTrackCardStatsDuringCombatLocked()) return;
                if (!IsTrackedPlayer(player)) return;
                if (sourceCard is not JackOfAllTrades) return;
                if (sourceCard.Owner != null && !ReferenceEquals(sourceCard.Owner, player)) return;

                var addedCard = result.cardAdded;
                _pendingCombat ??= new PendingCombat();
                var instanceId = GetOrAssignInstanceId(sourceCard);
                var agg = GetOrCreateAggregate(_pendingCombat, instanceId);
                AccumulateJackOfAllTradesCardAdded(
                    agg,
                    addedCard.Rarity,
                    addedCard.Type,
                    GetJackOfAllTradesAddedCardCost(addedCard));
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordJackOfAllTradesCardAdded failed: {e.Message}");
            }
        }
    }

    private static int GetJackOfAllTradesAddedCardCost(CardModel card)
    {
        try
        {
            return Math.Max(0, card.EnergyCost.GetWithModifiers(CostModifiers.None));
        }
        catch
        {
            return 0;
        }
    }

    private static void AccumulateJackOfAllTradesCardAdded(
        CardAggregate agg,
        CardRarity rarity,
        CardType type,
        int energyCost)
    {
        agg.JackColorlessCardsAdded++;
        agg.JackAddedCardCostTotal += Math.Max(0, energyCost);

        switch (rarity)
        {
            case CardRarity.Uncommon:
                agg.JackUncommonCardsAdded++;
                break;
            case CardRarity.Rare:
                agg.JackRareCardsAdded++;
                break;
        }

        switch (type)
        {
            case CardType.Attack:
                agg.JackAttacksAdded++;
                break;
            case CardType.Skill:
                agg.JackSkillsAdded++;
                break;
            case CardType.Power:
                agg.JackPowersAdded++;
                break;
        }
    }

    internal static void RecordJackOfAllTradesCardAddedForTest(
        CardAggregate agg,
        CardRarity rarity,
        CardType type,
        int energyCost)
        => AccumulateJackOfAllTradesCardAdded(agg, rarity, type, energyCost);

    /// <summary>
    /// Arm the exact generated-card calls made when Juggling's third owner
    /// Attack resolves. The power's pre-increment internal counter is the
    /// authoritative trigger boundary; Amount is the number of copies the
    /// native callback is about to attempt.
    /// </summary>
    internal static PendingJugglingCopyWindow? ArmJugglingCopyAttribution(
        JugglingPower? power,
        CardPlay? cardPlay)
    {
        if (power?.Owner?.Player == null || cardPlay?.Card == null) return null;

        lock (_lock)
        {
            try
            {
                if (!ShouldTrackCardStatsDuringCombatLocked()) return null;

                var player = power.Owner.Player;
                if (!IsTrackedPlayer(player)) return null;
                if (!ReferenceEquals(cardPlay.Card.Owner, player)) return null;
                if (cardPlay.Card.Type != CardType.Attack) return null;
                if (power.Amount <= 0) return null;
                if (power.GetInternalData<JugglingPower.Data>().attacksPlayedThisTurn != 2)
                    return null;

                _pendingCombat ??= new PendingCombat();
                RecordJugglingPowerActiveForPlayerLocked(power, player);

                var window = new PendingJugglingCopyWindow
                {
                    Player = player,
                    PowerId = power.Id.ToString(),
                    DisplayName = GetPowerDisplayName(power),
                    TriggerCardId = cardPlay.Card.Id.ToString(),
                    RemainingAttempts = power.Amount,
                };
                _pendingCombat.PendingJugglingCopyWindows[player] = window;
                return window;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"ArmJugglingCopyAttribution failed: {e.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Match one generated-card add command to the currently resolving
    /// Juggling trigger. The returned window survives the asynchronous pile
    /// result so only a confirmed arrival is counted.
    /// </summary>
    internal static PendingJugglingCopyWindow? CaptureJugglingCopyAttempt(
        CardModel? card,
        Player? creator)
    {
        if (card == null || creator == null) return null;

        lock (_lock)
        {
            try
            {
                if (!ShouldTrackCardStatsDuringCombatLocked()) return null;
                if (_pendingCombat == null) return null;
                if (!_pendingCombat.PendingJugglingCopyWindows.TryGetValue(
                        creator,
                        out var window))
                    return null;
                if (window.RemainingAttempts <= 0) return null;
                if (card.Type != CardType.Attack) return null;
                if (!string.Equals(
                        card.Id.ToString(),
                        window.TriggerCardId,
                        StringComparison.Ordinal))
                    return null;

                window.RemainingAttempts--;
                return window;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"CaptureJugglingCopyAttempt failed: {e.Message}");
                return null;
            }
        }
    }

    internal static void RecordJugglingCopyResult(
        PendingJugglingCopyWindow? window,
        CardPileAddResult result)
    {
        if (window == null || !result.success || result.cardAdded == null) return;

        lock (_lock)
        {
            try
            {
                if (!ShouldTrackCardStatsDuringCombatLocked()) return;
                if (_pendingCombat == null || !IsTrackedPlayer(window.Player)) return;
                if (!_pendingCombat.PendingJugglingCopyWindows.TryGetValue(
                        window.Player,
                        out var activeWindow)
                    || !ReferenceEquals(activeWindow, window))
                    return;

                var agg = GetOrCreatePowerAggregate(
                    _pendingCombat.MetaStats,
                    window.PowerId,
                    window.DisplayName);
                AccumulateJugglingCopy(agg, success: true, result.cardAdded.Rarity);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordJugglingCopyResult failed: {e.Message}");
            }
        }
    }

    internal static void DisarmJugglingCopyAttribution(PendingJugglingCopyWindow? window)
    {
        if (window == null) return;

        lock (_lock)
        {
            if (_pendingCombat == null) return;
            if (_pendingCombat.PendingJugglingCopyWindows.TryGetValue(
                    window.Player,
                    out var activeWindow)
                && ReferenceEquals(activeWindow, window))
            {
                _pendingCombat.PendingJugglingCopyWindows.Remove(window.Player);
            }
        }
    }

    /// <summary>
    /// Count the application turn and combat as held-power denominators.
    /// Re-applying another Juggling stack in the same turn is deduplicated.
    /// </summary>
    internal static void RecordJugglingPowerApplied(JugglingPower? power)
    {
        if (power?.Owner?.Player == null) return;

        lock (_lock)
        {
            try
            {
                if (!ShouldTrackCardStatsDuringCombatLocked()) return;
                if (!IsTrackedPlayer(power.Owner.Player)) return;
                _pendingCombat ??= new PendingCombat();
                RecordJugglingPowerActiveForPlayerLocked(power, power.Owner.Player);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordJugglingPowerApplied failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Count every player turn that begins with Juggling active, including
    /// zero-copy turns. Mid-turn first applications are counted separately by
    /// <see cref="RecordJugglingPowerApplied"/>.
    /// </summary>
    public static void RecordJugglingPowerTurnStarted(Player? player)
    {
        if (player?.Creature == null) return;

        lock (_lock)
        {
            try
            {
                if (!ShouldTrackCardStatsDuringCombatLocked()) return;
                if (!IsTrackedPlayer(player)) return;
                var power = player.Creature.GetPower<JugglingPower>();
                if (power == null) return;

                _pendingCombat ??= new PendingCombat();
                RecordJugglingPowerActiveForPlayerLocked(power, player);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordJugglingPowerTurnStarted failed: {e.Message}");
            }
        }
    }

    private static void RecordJugglingPowerActiveForPlayerLocked(
        JugglingPower power,
        Player player)
    {
        if (_pendingCombat == null) return;

        var agg = GetOrCreatePowerAggregate(
            _pendingCombat.MetaStats,
            power.Id.ToString(),
            GetPowerDisplayName(power));

        if (_pendingCombat.JugglingPowerCombatCountedPlayers.Add(player))
            agg.CombatsActive++;

        int turnNumber = player.PlayerCombatState?.TurnNumber ?? 0;
        if (turnNumber <= 0) return;
        if (_pendingCombat.JugglingPowerTurnCountedTurns.TryGetValue(
                player,
                out var recordedTurn)
            && recordedTurn == turnNumber)
            return;

        _pendingCombat.JugglingPowerTurnCountedTurns[player] = turnNumber;
        agg.TurnsActive++;
    }

    /// <summary>
    /// Count Danse Macabre's exact owner-card trigger and arm only the
    /// immediately issued decimal/ValueProp gain-block command.
    /// </summary>
    internal static PendingDanseMacabreBlockAttribution?
        RecordDanseMacabreTriggerAndArmBlockAttribution(
            DanseMacabrePower? power,
            CardPlay? cardPlay)
    {
        if (power?.Owner?.Player == null || cardPlay?.Card?.Owner?.Creature == null)
            return null;
        if (!ReferenceEquals(cardPlay.Card.Owner.Creature, power.Owner))
            return null;

        int resolvedEnergyCost;
        int triggerThreshold;
        try
        {
            resolvedEnergyCost = cardPlay.Card.EnergyCost.GetResolved();
            triggerThreshold = power.DynamicVars.Energy.IntValue;
        }
        catch
        {
            return null;
        }

        if (!DanseMacabreCardQualifiesForTest(resolvedEnergyCost, triggerThreshold))
            return null;

        lock (_lock)
        {
            try
            {
                if (!ShouldTrackCardStatsDuringCombatLocked()) return null;
                if (CombatManager.Instance?.IsInProgress != true) return null;

                var player = power.Owner.Player;
                if (!IsTrackedPlayer(player)) return null;

                _pendingCombat ??= new PendingCombat();
                RecordDanseMacabrePowerActiveForPlayerLocked(power, player);

                var agg = GetOrCreatePowerAggregate(
                    _pendingCombat.MetaStats,
                    power.Id.ToString(),
                    GetPowerDisplayName(power));
                RecordDanseMacabreTriggerForTest(agg);

                var attribution = new PendingDanseMacabreBlockAttribution
                {
                    PendingCombat = _pendingCombat,
                    Owner = power.Owner,
                    PowerId = power.Id.ToString(),
                    DisplayName = GetPowerDisplayName(power),
                };
                _pendingDanseMacabreBlockAttributions.Add(attribution);
                return attribution;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug(
                    $"RecordDanseMacabreTriggerAndArmBlockAttribution failed: {e.Message}");
                return null;
            }
        }
    }

    internal static PendingDanseMacabreBlockAttribution?
        TryConsumeDanseMacabreBlockAttribution(Creature? creature)
    {
        if (creature == null) return null;

        lock (_lock)
        {
            for (int i = 0; i < _pendingDanseMacabreBlockAttributions.Count; i++)
            {
                var attribution = _pendingDanseMacabreBlockAttributions[i];
                if (!ReferenceEquals(attribution.Owner, creature)) continue;
                if (!ReferenceEquals(attribution.PendingCombat, _pendingCombat)) continue;

                _pendingDanseMacabreBlockAttributions.RemoveAt(i);
                return attribution;
            }

            return null;
        }
    }

    internal static void DisarmDanseMacabreBlockAttribution(
        PendingDanseMacabreBlockAttribution? attribution)
    {
        if (attribution == null) return;
        lock (_lock)
            _pendingDanseMacabreBlockAttributions.Remove(attribution);
    }

    internal static void RecordDanseMacabreBlockGained(
        PendingDanseMacabreBlockAttribution? attribution,
        decimal amount)
    {
        if (attribution == null || amount <= 0m) return;

        lock (_lock)
        {
            try
            {
                if (!ShouldTrackCardStatsDuringCombatLocked()) return;
                if (!ReferenceEquals(attribution.PendingCombat, _pendingCombat)) return;

                var agg = GetOrCreatePowerAggregate(
                    _pendingCombat!.MetaStats,
                    attribution.PowerId,
                    attribution.DisplayName);
                RecordDanseMacabreBlockGainedForTest(agg, amount);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordDanseMacabreBlockGained failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Count every player turn that begins with Danse Macabre active. Its
    /// first application turn is counted from the observed power application.
    /// </summary>
    public static void RecordDanseMacabrePowerTurnStarted(Player? player)
    {
        if (player?.Creature == null) return;

        lock (_lock)
        {
            try
            {
                if (!ShouldTrackCardStatsDuringCombatLocked()) return;
                if (!IsTrackedPlayer(player)) return;
                var power = player.Creature.GetPower<DanseMacabrePower>();
                if (power == null) return;

                _pendingCombat ??= new PendingCombat();
                RecordDanseMacabrePowerActiveForPlayerLocked(power, player);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordDanseMacabrePowerTurnStarted failed: {e.Message}");
            }
        }
    }

    private static void RecordDanseMacabrePowerActiveForPlayerLocked(
        DanseMacabrePower power,
        Player player)
    {
        if (_pendingCombat == null) return;

        var agg = GetOrCreatePowerAggregate(
            _pendingCombat.MetaStats,
            power.Id.ToString(),
            GetPowerDisplayName(power));

        if (_pendingCombat.DanseMacabrePowerCombatCountedPlayers.Add(player))
            agg.CombatsActive++;

        int turnNumber = player.PlayerCombatState?.TurnNumber ?? 0;
        if (turnNumber <= 0) return;
        if (_pendingCombat.DanseMacabrePowerTurnCountedTurns.TryGetValue(
                player,
                out var recordedTurn)
            && recordedTurn == turnNumber)
            return;

        _pendingCombat.DanseMacabrePowerTurnCountedTurns[player] = turnNumber;
        agg.TurnsActive++;
    }

    internal static bool DanseMacabreCardQualifiesForTest(
        int resolvedEnergyCost,
        int triggerThreshold)
        => resolvedEnergyCost >= triggerThreshold;

    internal static void RecordDanseMacabreTriggerForTest(
        PowerAggregate agg,
        int count = 1)
    {
        if (agg == null || count <= 0) return;
        agg.TimesTriggered += count;
    }

    internal static void RecordDanseMacabreBlockGainedForTest(
        PowerAggregate agg,
        decimal amount)
    {
        if (agg == null || amount <= 0m) return;
        agg.BlockGained += amount;
    }

    private static PowerAggregate GetOrCreatePowerAggregate(
        RunMetaStats metaStats,
        string powerId,
        string displayName)
    {
        metaStats.PowerAggregates ??= new Dictionary<string, PowerAggregate>();
        if (!metaStats.PowerAggregates.TryGetValue(powerId, out var agg))
        {
            agg = new PowerAggregate
            {
                PowerId = powerId,
                DisplayName = displayName,
            };
            metaStats.PowerAggregates[powerId] = agg;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(agg.PowerId))
                agg.PowerId = powerId;
            if (string.IsNullOrWhiteSpace(agg.DisplayName)
                && !string.IsNullOrWhiteSpace(displayName))
                agg.DisplayName = displayName;
        }

        return agg;
    }

    private static void AccumulateJugglingCopy(
        PowerAggregate agg,
        bool success,
        CardRarity? rarity)
    {
        if (agg == null || !success) return;

        agg.AttacksCopied++;
        switch (rarity)
        {
            case CardRarity.Common:
                agg.CommonAttacksCopied++;
                break;
            case CardRarity.Uncommon:
                agg.UncommonAttacksCopied++;
                break;
            case CardRarity.Rare:
                agg.RareAttacksCopied++;
                break;
        }
    }

    internal static void RecordJugglingCopyForTest(
        PowerAggregate agg,
        bool success,
        CardRarity? rarity)
        => AccumulateJugglingCopy(agg, success, rarity);

    /// <summary>
    /// Remember Free Attack's marginal energy-cost reduction for an exact
    /// combat card. Cost modifiers are queried repeatedly, so this is only an
    /// offer snapshot; the power's BeforeCardPlayed callback confirms whether
    /// that card actually consumes a charge.
    /// </summary>
    internal static void RememberFreeAttackEnergySavings(
        FreeAttackPower? power,
        CardModel? card,
        decimal originalCost,
        decimal modifiedCost)
    {
        if (power?.Owner?.Player == null || card == null) return;

        lock (_lock)
        {
            try
            {
                if (!ShouldTrackCardStatsDuringCombatLocked()) return;
                if (!IsTrackedPlayer(power.Owner.Player)) return;
                if (!ReferenceEquals(card.Owner, power.Owner.Player)) return;
                if (card.Type != CardType.Attack) return;
                if (_pendingCombat == null) return;

                _pendingCombat.FreeAttackEnergySavingsByCard[card] =
                    Math.Max(0m, originalCost - modifiedCost);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RememberFreeAttackEnergySavings failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Capture the exact Attack that Free Attack is about to consume a charge
    /// for. The returned observation is committed only after the native async
    /// decrement completes and the power's amount actually falls.
    /// </summary>
    internal static PendingFreeAttackUse? CaptureFreeAttackUse(
        FreeAttackPower? power,
        CardPlay? cardPlay)
    {
        if (power?.Owner?.Player == null || cardPlay?.Card == null) return null;

        lock (_lock)
        {
            try
            {
                if (!ShouldTrackCardStatsDuringCombatLocked()) return null;
                if (!IsTrackedPlayer(power.Owner.Player)) return null;
                if (!ReferenceEquals(cardPlay.Card.Owner, power.Owner.Player)) return null;
                if (cardPlay.Card.Type != CardType.Attack) return null;
                if (cardPlay.Card.Pile?.Type is not (PileType.Hand or PileType.Play)) return null;
                if (power.Amount <= 0) return null;

                _pendingCombat ??= new PendingCombat();
                _pendingCombat.FreeAttackEnergySavingsByCard.Remove(
                    cardPlay.Card,
                    out var offeredEnergySavings);

                return new PendingFreeAttackUse
                {
                    Power = power,
                    Player = power.Owner.Player,
                    Card = cardPlay.Card,
                    StartingPowerAmount = power.Amount,
                    OfferedEnergySavings = Math.Max(0m, offeredEnergySavings),
                    IsAutoPlay = cardPlay.IsAutoPlay,
                };
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"CaptureFreeAttackUse failed: {e.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Commit one observed Free Attack charge consumption after the power's
    /// decrement task succeeds.
    /// </summary>
    internal static void RecordFreeAttackUse(PendingFreeAttackUse? observation)
    {
        if (observation == null) return;

        lock (_lock)
        {
            try
            {
                if (!ShouldTrackCardStatsDuringCombatLocked()) return;
                if (_pendingCombat == null || !IsTrackedPlayer(observation.Player)) return;
                if (observation.Power.Amount >= observation.StartingPowerAmount) return;

                var agg = GetOrCreatePowerAggregate(
                    _pendingCombat.MetaStats,
                    observation.Power.Id.ToString(),
                    GetPowerDisplayName(observation.Power));
                AccumulateFreeAttackUse(
                    agg,
                    observation.IsAutoPlay ? 0m : observation.OfferedEnergySavings,
                    observation.Card.Rarity);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordFreeAttackUse failed: {e.Message}");
            }
        }
    }

    private static void AccumulateFreeAttackGrant(PowerAggregate agg, int charges)
    {
        if (agg == null || charges <= 0) return;
        agg.FreeAttackChargesGranted += charges;
    }

    private static void AccumulateFreeAttackUse(
        PowerAggregate agg,
        decimal energySaved,
        CardRarity rarity)
    {
        if (agg == null) return;

        var observedEnergySaved = Math.Max(0m, energySaved);
        agg.FreeAttackChargesUsed++;
        agg.FreeAttackEnergySaved += observedEnergySaved;
        if (observedEnergySaved <= 0m)
            agg.FreeAttackZeroEnergySavingsUses++;

        switch (rarity)
        {
            case CardRarity.Basic:
                agg.FreeAttackBasicAttacksDiscounted++;
                break;
            case CardRarity.Common:
                agg.FreeAttackCommonAttacksDiscounted++;
                break;
            case CardRarity.Uncommon:
                agg.FreeAttackUncommonAttacksDiscounted++;
                break;
            case CardRarity.Rare:
                agg.FreeAttackRareAttacksDiscounted++;
                break;
        }
    }

    internal static void RecordFreeAttackGrantForTest(PowerAggregate agg, int charges)
        => AccumulateFreeAttackGrant(agg, charges);

    internal static void RecordFreeAttackUseForTest(
        PowerAggregate agg,
        decimal energySaved,
        CardRarity rarity)
        => AccumulateFreeAttackUse(agg, energySaved, rarity);

    /// <summary>
    /// Capture Discovery at the exact SetToFreeThisTurn call it makes on the
    /// picked card. A skipped choice never reaches this boundary.
    /// </summary>
    internal static CardModel? CaptureDiscoveryChoiceSource(CardModel? selectedCard)
    {
        if (selectedCard == null) return null;

        lock (_lock)
        {
            try
            {
                if (!ShouldTrackCardStatsDuringCombatLocked()) return null;

                var causingPlay = FindCurrentlyResolvingCardPlay();
                var sourceCard = causingPlay?.Card;
                if (sourceCard is not Discovery) return null;
                var player = sourceCard.Owner;
                if (player == null || !IsTrackedPlayer(player)) return null;
                if (selectedCard.Owner != null && !ReferenceEquals(selectedCard.Owner, player)) return null;

                return Canonical(sourceCard);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"CaptureDiscoveryChoiceSource failed: {e.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Record the picked card after Discovery makes it free. The discount is
    /// the observed effective energy-cost reduction across that exact call.
    /// </summary>
    internal static void RecordDiscoveryCardPicked(
        CardModel sourceCard,
        CardModel? selectedCard,
        int costBefore,
        int costAfter)
    {
        if (selectedCard == null) return;

        lock (_lock)
        {
            try
            {
                if (!ShouldTrackCardStatsDuringCombatLocked()) return;
                if (sourceCard is not Discovery) return;
                if (!IsTrackedCard(sourceCard)) return;

                _pendingCombat ??= new PendingCombat();
                var instanceId = GetOrAssignInstanceId(sourceCard);
                var agg = GetOrCreateAggregate(_pendingCombat, instanceId);
                AccumulateDiscoveryCardPicked(
                    agg,
                    selectedCard.Rarity,
                    selectedCard.Type,
                    Math.Max(0, costBefore - costAfter));
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordDiscoveryCardPicked failed: {e.Message}");
            }
        }
    }

    private static void AccumulateDiscoveryCardPicked(
        CardAggregate agg,
        CardRarity rarity,
        CardType type,
        int energyDiscount)
    {
        agg.DiscoveryCardsPicked++;
        agg.DiscoveryEnergyDiscountTotal += Math.Max(0, energyDiscount);

        switch (rarity)
        {
            case CardRarity.Common:
                agg.DiscoveryCommonCardsPicked++;
                break;
            case CardRarity.Uncommon:
                agg.DiscoveryUncommonCardsPicked++;
                break;
            case CardRarity.Rare:
                agg.DiscoveryRareCardsPicked++;
                break;
        }

        switch (type)
        {
            case CardType.Attack:
                agg.DiscoveryAttacksPicked++;
                break;
            case CardType.Skill:
                agg.DiscoverySkillsPicked++;
                break;
            case CardType.Power:
                agg.DiscoveryPowersPicked++;
                break;
        }
    }

    internal static void RecordDiscoveryCardPickedForTest(
        CardAggregate agg,
        CardRarity rarity,
        CardType type,
        int costBefore,
        int costAfter)
        => AccumulateDiscoveryCardPicked(
            agg,
            rarity,
            type,
            Math.Max(0, costBefore - costAfter));

    /// <summary>
    /// Count every player turn that starts while each physical Drain Power is
    /// in the permanent deck. This is the zero-inclusive turn denominator for
    /// its upgrade and upgraded-card-play averages.
    /// </summary>
    public static void RecordDrainPowerTurnStarted(Player? player)
    {
        if (player?.Deck?.Cards == null) return;

        lock (_lock)
        {
            try
            {
                if (!ShouldTrackCardStatsDuringCombatLocked()) return;
                if (!IsTrackedPlayer(player)) return;

                var turnNumber = player.PlayerCombatState?.TurnNumber ?? 0;
                if (turnNumber <= 0) return;

                _pendingCombat ??= new PendingCombat();
                foreach (var card in player.Deck.Cards)
                {
                    if (!IsDrainPowerCard(card)) continue;

                    var sourceId = GetOrAssignInstanceId(card);
                    if (_pendingCombat.DrainPowerTurnCountedTurns.TryGetValue(
                            sourceId,
                            out var recordedTurn)
                        && recordedTurn == turnNumber)
                    {
                        continue;
                    }

                    _pendingCombat.DrainPowerTurnCountedTurns[sourceId] = turnNumber;
                    var agg = GetOrCreateAggregate(_pendingCombat, sourceId);
                    AccumulateDrainPowerTurn(agg);
                }
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordDrainPowerTurnStarted failed: {e.Message}");
            }
        }
    }

    private static void RecordDrainPowerCardUpgradedLocked(CardModel upgradedCard)
    {
        if (!ShouldTrackCardStatsDuringCombatLocked()) return;

        var causingPlay = FindCurrentlyResolvingCardPlay();
        var sourceCard = causingPlay?.Card;
        if (!IsDrainPowerCard(sourceCard)) return;
        if (!IsTrackedCard(sourceCard)) return;
        if (sourceCard!.Owner == null || upgradedCard.Owner == null) return;
        if (!ReferenceEquals(sourceCard.Owner, upgradedCard.Owner)) return;

        _pendingCombat ??= new PendingCombat();
        var sourceId = GetOrAssignInstanceId(sourceCard);
        var sourceAgg = GetOrCreateAggregate(_pendingCombat, sourceId);
        AccumulateDrainPowerUpgrade(sourceAgg);

        if (!_pendingCombat.DrainPowerSourcesByUpgradedCard.TryGetValue(
                upgradedCard,
                out var sourceIds))
        {
            sourceIds = new HashSet<string>(StringComparer.Ordinal);
            _pendingCombat.DrainPowerSourcesByUpgradedCard[upgradedCard] = sourceIds;
        }

        sourceIds.Add(sourceId);
    }

    private static void RecordDrainPowerUpgradedCardPlayedLocked(CardModel card)
    {
        if (_pendingCombat == null) return;
        if (!_pendingCombat.DrainPowerSourcesByUpgradedCard.TryGetValue(
                card,
                out var sourceIds))
        {
            return;
        }

        foreach (var sourceId in sourceIds)
        {
            var sourceAgg = GetOrCreateAggregate(_pendingCombat, sourceId);
            AccumulateDrainPowerUpgradedCardPlay(sourceAgg);
        }
    }

    private static bool IsDrainPowerCard(CardModel? card)
    {
        if (card is DrainPower) return true;

        try
        {
            return string.Equals(
                card?.Id?.ToString(),
                "CARD.DRAIN_POWER",
                StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static void AccumulateDrainPowerUpgrade(CardAggregate agg, int count = 1)
    {
        if (agg == null || count <= 0) return;
        agg.DrainPowerCardsUpgraded += count;
    }

    private static void AccumulateDrainPowerTurn(CardAggregate agg, int count = 1)
    {
        if (agg == null || count <= 0) return;
        agg.DrainPowerTurnsInDeck += count;
    }

    private static void AccumulateDrainPowerUpgradedCardPlay(
        CardAggregate agg,
        int count = 1)
    {
        if (agg == null || count <= 0) return;
        agg.DrainPowerUpgradedCardPlays += count;
    }

    internal static void RecordDrainPowerUpgradeForTest(CardAggregate agg, int count = 1)
        => AccumulateDrainPowerUpgrade(agg, count);

    internal static void RecordDrainPowerTurnForTest(CardAggregate agg, int count = 1)
        => AccumulateDrainPowerTurn(agg, count);

    internal static void RecordDrainPowerUpgradedCardPlayForTest(
        CardAggregate agg,
        int count = 1)
        => AccumulateDrainPowerUpgradedCardPlay(agg, count);

    /// <summary>
    /// Record one completed Debt end-of-turn effect. Debt clamps the amount
    /// passed to LoseGold to the owner's current balance, so the card's Gold
    /// dynamic var is the intended loss while the before/after balance delta
    /// is the observed loss. Their difference is the amount blocked by being
    /// out of gold.
    /// </summary>
    internal static void RecordDebtTrigger(
        Debt card,
        Player? player,
        int intendedGoldLoss,
        int initialGold,
        int finalGold)
    {
        if (card == null || player == null) return;

        lock (_lock)
        {
            try
            {
                if (!ShouldTrackCardStatsDuringCombatLocked()) return;
                if (!IsTrackedCard(card) || !IsTrackedPlayer(player)) return;
                if (card.Owner != null && !ReferenceEquals(card.Owner, player)) return;

                _pendingCombat ??= new PendingCombat();
                var instanceId = GetOrAssignInstanceId(card);
                var agg = GetOrCreateAggregate(_pendingCombat, instanceId);
                AccumulateDebtTrigger(agg, intendedGoldLoss, initialGold, finalGold);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordDebtTrigger failed: {e.Message}");
            }
        }
    }

    private static void AccumulateDebtTrigger(
        CardAggregate agg,
        int intendedGoldLoss,
        int initialGold,
        int finalGold)
    {
        if (agg == null) return;

        int intended = Math.Max(0, intendedGoldLoss);
        int observed = Math.Max(0, initialGold - finalGold);
        int actual = Math.Min(intended, observed);

        agg.DebtTriggers++;
        agg.DebtGoldLost += actual;
        agg.DebtGoldLossBlocked += Math.Max(0, intended - actual);
    }

    internal static void RecordDebtTriggerForTest(
        CardAggregate agg,
        int intendedGoldLoss,
        int initialGold,
        int finalGold)
        => AccumulateDebtTrigger(agg, intendedGoldLoss, initialGold, finalGold);

    // -------- Relic stat recording --------

    private const string BagOfMarblesRelicId = "RELIC.BAG_OF_MARBLES";
    private const string RedMaskRelicId = "RELIC.RED_MASK";
    private const string UnsettlingLampRelicId = "RELIC.UNSETTLING_LAMP";
    private const string PocketwatchRelicId = "RELIC.POCKETWATCH";
    private const string OrichalcumRelicId = "RELIC.ORICHALCUM";
    private const string PermafrostRelicId = "RELIC.PERMAFROST";
    private const string TuningForkRelicId = "RELIC.TUNING_FORK";
    private const string RippleBasinRelicId = "RELIC.RIPPLE_BASIN";
    private const string AnchorRelicId = "RELIC.ANCHOR";
    private const string TheAbacusRelicId = "RELIC.THE_ABACUS";
    private const string LetterOpenerRelicId = "RELIC.LETTER_OPENER";
    private const int LetterOpenerDamagePerTarget = 5;
    private const string PenNibRelicId = "RELIC.PEN_NIB";
    private const string AkabekoRelicId = "RELIC.AKABEKO";
    private const string BookRepairKnifeRelicId = "RELIC.BOOK_REPAIR_KNIFE";
    private const string BookOfFiveRingsRelicId = "RELIC.BOOK_OF_FIVE_RINGS";
    private const string EternalFeatherRelicId = "RELIC.ETERNAL_FEATHER";
    private const string BoneFluteRelicId = "RELIC.BONE_FLUTE";
    private const string HealingLostFullHpReasonId = "full_hp";
    private const string HealingLostOtherReasonId = "other";
    private const string HappyFlowerRelicId = "RELIC.HAPPY_FLOWER";
    private const string BoomingConchRelicId = "RELIC.BOOMING_CONCH";
    private const string GremlinHornRelicId = "RELIC.GREMLIN_HORN";
    private const string NunchakuRelicId = "RELIC.NUNCHAKU";
    private const string IronClubRelicId = "RELIC.IRON_CLUB";
    private const string LanternRelicId = "RELIC.LANTERN";
    private const string VeryHotCocoaRelicId = "RELIC.VERY_HOT_COCOA";
    private const string CandelabraRelicId = "RELIC.CANDELABRA";
    private const string ChandelierRelicId = "RELIC.CHANDELIER";
    private const string PendulumRelicId = "RELIC.PENDULUM";
    private const string ParryingShieldRelicId = "RELIC.PARRYING_SHIELD";
    private const string FestivePopperRelicId = "RELIC.FESTIVE_POPPER";
    private const string MercuryHourglassRelicId = "RELIC.MERCURY_HOURGLASS";
    private const string MrStrugglesRelicId = "RELIC.MR_STRUGGLES";
    private const string BronzeScalesRelicId = "RELIC.BRONZE_SCALES";
    private const string HornCleatRelicId = "RELIC.HORN_CLEAT";
    private const string CaptainsWheelRelicId = "RELIC.CAPTAINS_WHEEL";
    private const string PrismaticGemRelicId = "RELIC.PRISMATIC_GEM";
    private const string SealOfGoldRelicId = "RELIC.SEAL_OF_GOLD";
    private const string FresnelLensRelicId = "RELIC.FRESNEL_LENS";
    private const string SilverCrucibleRelicId = "RELIC.SILVER_CRUCIBLE";
    private const string OrreryRelicId = "RELIC.ORRERY";
    private const string BloodSoakedRoseRelicId = "RELIC.BLOOD_SOAKED_ROSE";
    private const string CursedPearlRelicId = "RELIC.CURSED_PEARL";
    private const string SignetRingRelicId = "RELIC.SIGNET_RING";
    private const string WingedBootsRelicId = "RELIC.WINGED_BOOTS";
    private const string CloakClaspRelicId = "RELIC.CLOAK_CLASP";
    private const string ReptileTrinketRelicId = "RELIC.REPTILE_TRINKET";
    private const string BeatingRemnantRelicId = "RELIC.BEATING_REMNANT";
    private const string GorgetRelicId = "RELIC.GORGET";
    private const string StoneCrackerRelicId = "RELIC.STONE_CRACKER";
    private const string RazorToothRelicId = "RELIC.RAZOR_TOOTH";
    private const string WarHammerRelicId = "RELIC.WAR_HAMMER";
    private const string WhetstoneRelicId = "RELIC.WHETSTONE";
    private const string WarPaintRelicId = "RELIC.WAR_PAINT";
    private const string FragrantMushroomRelicId = "RELIC.FRAGRANT_MUSHROOM";
    private const string ArtOfWarRelicId = "RELIC.ART_OF_WAR";
    private const string CrackedCoreRelicId = "RELIC.CRACKED_CORE";
    private const string FishingRodRelicId = "RELIC.FISHING_ROD";
    private const string MoltenEggRelicId = "RELIC.MOLTEN_EGG";
    private const string ToxicEggRelicId = "RELIC.TOXIC_EGG";
    private const string FrozenEggRelicId = "RELIC.FROZEN_EGG";
    private const string MealTicketRelicId = "RELIC.MEAL_TICKET";
    private const string BurningBloodRelicId = "RELIC.BURNING_BLOOD";
    private const string BloodVialRelicId = "RELIC.BLOOD_VIAL";
    private const string PantographRelicId = "RELIC.PANTOGRAPH";
    private const string PlanisphereRelicId = "RELIC.PLANISPHERE";
    private const string LizardTailRelicId = "RELIC.LIZARD_TAIL";
    private const string LeesWaffleRelicId = "RELIC.LEES_WAFFLE";
    private const string StrawberryRelicId = "RELIC.STRAWBERRY";
    private const string PearRelicId = "RELIC.PEAR";
    private const string MangoRelicId = "RELIC.MANGO";
    private const string NutritiousOysterRelicId = "RELIC.NUTRITIOUS_OYSTER";
    private const string StoneHumidifierRelicId = "RELIC.STONE_HUMIDIFIER";
    private const string ChosenCheeseRelicId = "RELIC.CHOSEN_CHEESE";
    private const string DarkstonePeriaptRelicId = "RELIC.DARKSTONE_PERIAPT";
    private const string LuckyFyshRelicId = "RELIC.LUCKY_FYSH";
    private const string LeafyPoulticeRelicId = "RELIC.LEAFY_POULTICE";
    private const string RegalPillowRelicId = "RELIC.REGAL_PILLOW";
    private const string WhiteBeastStatueRelicId = "RELIC.WHITE_BEAST_STATUE";
    private const string ShovelRelicId = "RELIC.SHOVEL";
    private const string LargeCapsuleRelicId = "RELIC.LARGE_CAPSULE";
    private const string NeowsBonesRelicId = "RELIC.NEOWS_BONES";
    private const string BoundPhylacteryRelicId = "RELIC.BOUND_PHYLACTERY";
    private const string PhylacteryUnboundRelicId = "RELIC.PHYLACTERY_UNBOUND";
    private const string ToolboxRelicId = "RELIC.TOOLBOX";
    private const string PaelsWingRelicId = "RELIC.PAELS_WING";
    private const string PaelsToothRelicId = "RELIC.PAELS_TOOTH";
    private const string PaelsClawRelicId = "RELIC.PAELS_CLAW";
    private const string PaelsEyeRelicId = "RELIC.PAELS_EYE";
    private const string StrikeDummyRelicId = "RELIC.STRIKE_DUMMY";
    private const string NutritiousSoupRelicId = "RELIC.NUTRITIOUS_SOUP";
    private const string MiniatureCannonRelicId = "RELIC.MINIATURE_CANNON";
    private const string VajraRelicId = "RELIC.VAJRA";
    private const string EmberTeaRelicId = "RELIC.EMBER_TEA";
    private const string ToastyMittensRelicId = "RELIC.TOASTY_MITTENS";
    private const string KunaiRelicId = "RELIC.KUNAI";
    private const string KusarigamaRelicId = "RELIC.KUSARIGAMA";
    private const string OrnamentalFanRelicId = "RELIC.ORNAMENTAL_FAN";
    private const string ShurikenRelicId = "RELIC.SHURIKEN";
    private const string PaperPhrogRelicId = "RELIC.PAPER_PHROG";
    private const string RegaliteRelicId = "RELIC.REGALITE";
    private const string IntimidatingHelmetRelicId = "RELIC.INTIMIDATING_HELMET";
    private const string DaughterOfTheWindRelicId = "RELIC.DAUGHTER_OF_THE_WIND";
    private const string SturdyClampRelicId = "RELIC.STURDY_CLAMP";
    private const string RuinedHelmetRelicId = "RELIC.RUINED_HELMET";
    private const string MummifiedHandRelicId = "RELIC.MUMMIFIED_HAND";
    private const string GnarledHammerRelicId = "RELIC.GNARLED_HAMMER";
    private const string TriBoomerangRelicId = "RELIC.TRI_BOOMERANG";
    private const string BookmarkRelicId = "RELIC.BOOKMARK";
    private const string BrilliantScarfRelicId = "RELIC.BRILLIANT_SCARF";
    private const string JuzuBraceletRelicId = "RELIC.JUZU_BRACELET";
    private const string DowsingRodRelicId = "RELIC.DOWSING_ROD";
    private const string HeftyTabletRelicId = "RELIC.HEFTY_TABLET";
    private const string ArcaneScrollRelicId = "RELIC.ARCANE_SCROLL";
    private const string VambraceRelicId = "RELIC.VAMBRACE";
    private const string GamblingChipRelicId = "RELIC.GAMBLING_CHIP";
    private const string CentennialPuzzleRelicId = "RELIC.CENTENNIAL_PUZZLE";
    private const string PrecariousShearsRelicId = "RELIC.PRECARIOUS_SHEARS";
    private const string SandCastleRelicId = "RELIC.SAND_CASTLE";

    /// <summary>
    /// Record one choosable card option that an egg relic actually upgraded.
    /// Card rewards, merchant cards, and any future offer surface using the
    /// game's shared egg helper all pass through this observation point.
    /// </summary>
    public static void RecordEggUpgradedCardOffered(RelicModel eggRelic)
    {
        if (eggRelic == null) return;

        var relicId = eggRelic switch
        {
            MoltenEgg => MoltenEggRelicId,
            ToxicEgg => ToxicEggRelicId,
            FrozenEgg => FrozenEggRelicId,
            _ => null,
        };
        if (relicId == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(eggRelic) || !IsTrackedPlayer(eggRelic.Owner)) return;

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(relicId);
                RecordEggUpgradedCardOfferedForTest(agg);
                RefreshCurrentRunMetadataLocked();
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordEggUpgradedCardOffered failed: {e.Message}");
            }
        }
    }

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

    public static void RecordUnsettlingLampDebuffMultiplier(
        RelicModel relic,
        PowerModel power,
        Creature giver,
        decimal amountBeforeMultiplier,
        Creature? target,
        CardModel? cardSource,
        decimal multiplier)
    {
        if (relic == null || power == null || giver == null || target == null) return;
        if (amountBeforeMultiplier <= 0m || multiplier <= 1m) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic)) return;
                if (!IsTrackedPlayer(relic.Owner)) return;
                if (target.IsPlayer) return;
                if (power.GetTypeForAmount(amountBeforeMultiplier) != PowerType.Debuff) return;

                _pendingCombat ??= new PendingCombat();
                _pendingUnsettlingLampDebuffs.Add(new PendingUnsettlingLampDebuff
                {
                    Power = power,
                    Target = target,
                    Applier = giver,
                    CardSource = cardSource != null ? Canonical(cardSource) : null,
                    ExtraAmount = amountBeforeMultiplier * (multiplier - 1m),
                });
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordUnsettlingLampDebuffMultiplier failed: {e.Message}");
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
    public static void RecordPermafrostActivationAndArmBlockAttribution(Permafrost relic)
    {
        lock (_lock)
        {
            try
            {
                if (relic?.Owner == null) return;
                _pendingCombat ??= new PendingCombat();
                RecordPermafrostCombatForPlayerLocked(relic.Owner);
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

    internal static void RecordPermafrostCombatForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.PermafrostCombats += Math.Max(0, count);
    }

    public static void DisarmPermafrostBlockAttribution()
    {
        lock (_lock)
        {
            _pendingCombat?.Windows.Disarm(PermafrostRelicId, AttributionEventKind.PlayerBlockGain);
        }
    }

    /// <summary>
    /// Record a Tuning Fork-owned Skill play and arm observed block attribution
    /// if this play will cross the relic's threshold. The actual block amount
    /// is observed by <see cref="Patches.HookAfterBlockGainedPatch"/>.
    /// </summary>
    public static bool RecordTuningForkSkillPlayedAndShouldArmBlockAttribution(TuningFork relic, CardPlay cardPlay)
    {
        if (relic?.Owner == null || cardPlay?.Card == null) return false;
        if (cardPlay.Card.Type != CardType.Skill) return false;
        if (!ReferenceEquals(cardPlay.Card.Owner, relic.Owner)) return false;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic)) return false;
                if (!IsTrackedPlayer(relic.Owner)) return false;
                if (!CombatManager.Instance.IsInProgress) return false;

                _pendingCombat ??= new PendingCombat();
                RecordTuningForkCombatForPlayerLocked(relic.Owner);
                RecordTuningForkTurnForPlayerLocked(relic.Owner);

                var agg = GetOrCreatePendingRelicAggregateLocked(TuningForkRelicId);
                RecordTuningForkSkillPlayedForTest(agg);

                var threshold = TuningForkCardsPerActivation(relic);
                if (threshold <= 0) return false;
                if (Math.Max(0, relic.SkillsPlayed) + 1 < threshold) return false;

                agg.Activations += 1;
                _pendingCombat.Windows.Arm(
                    TuningForkRelicId,
                    AttributionEventKind.PlayerBlockGain,
                    CurrentHistoryCountLocked(),
                    maxHistoryAdvance: -1);
                return true;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordTuningForkSkillPlayedAndShouldArmBlockAttribution failed: {e.Message}");
                return false;
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
    /// Snapshot Tuning Fork's persistent Skill counter at the end of each
    /// tracked player turn while held.
    /// </summary>
    public static void RecordTuningForkTurnEnded(IEnumerable<Creature>? participants)
    {
        lock (_lock)
        {
            try
            {
                _pendingCombat ??= new PendingCombat();
                var recordedParticipant = false;

                if (participants != null)
                {
                    foreach (var creature in participants)
                    {
                        var player = creature?.Player;
                        if (player == null || !IsTrackedPlayer(player)) continue;

                        recordedParticipant |= RecordTuningForkTurnEndChargeForPlayerLocked(
                            player,
                            player.PlayerCombatState?.TurnNumber ?? 0);
                    }
                }

                if (recordedParticipant) return;

                var trackedPlayer = GetTrackedRunPlayerLocked();
                if (trackedPlayer == null) return;
                RecordTuningForkTurnEndChargeForPlayerLocked(
                    trackedPlayer,
                    trackedPlayer.PlayerCombatState?.TurnNumber ?? 0);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordTuningForkTurnEnded failed: {e.Message}");
            }
        }
    }

    private static bool RecordTuningForkTurnEndChargeForPlayerLocked(
        Player player,
        int turnNumber,
        TuningFork? tuningFork = null)
    {
        if (_pendingCombat == null) return false;
        if (player == null || !IsTrackedPlayer(player)) return false;
        tuningFork ??= TryGetTuningFork(player, out var foundRelic) ? foundRelic : null;
        if (tuningFork == null) return false;
        if (turnNumber <= 0) return false;

        RecordTuningForkTurnForPlayerLocked(player, turnNumber);

        if (_pendingCombat.TuningForkTurnEndChargeRecordedTurns.TryGetValue(player, out var recordedTurn)
            && recordedTurn == turnNumber)
        {
            return true;
        }

        _pendingCombat.TuningForkTurnEndChargeRecordedTurns[player] = turnNumber;
        var agg = GetOrCreatePendingRelicAggregateLocked(TuningForkRelicId);
        RecordTuningForkTurnEndChargeForTest(agg, TuningForkCharge(tuningFork));
        return true;
    }

    internal static void RecordTuningForkSkillPlayedForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.TuningForkSkillsPlayed += Math.Max(0, count);
    }

    internal static void RecordTuningForkCombatForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.TuningForkCombats += Math.Max(0, count);
    }

    internal static void RecordTuningForkTurnForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.TuningForkTurns += Math.Max(0, count);
    }

    internal static void RecordTuningForkTurnEndChargeForTest(RelicAggregate agg, int charge)
    {
        if (agg == null || charge < 0) return;

        charge %= 10;
        agg.TuningForkTurnEndChargeTotal += charge;
        agg.TuningForkTurnEndChargeCount += 1;
        if (charge == 8)
            agg.TuningForkTurnsEndedOn8Charges += 1;
        else if (charge == 9)
            agg.TuningForkTurnsEndedOn9Charges += 1;
    }

    /// <summary>
    /// Count a distinct player turn toward Ripple Basin's held-turn average,
    /// whether or not the relic grants block at that turn's end.
    /// </summary>
    public static void RecordRippleBasinTurnStarted(Player player)
    {
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(player)) return;
                _pendingCombat ??= new PendingCombat();
                RecordRippleBasinTurnForPlayerLocked(player);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordRippleBasinTurnStarted failed: {e.Message}");
            }
        }
    }

    internal static void RecordRippleBasinCombatForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.RippleBasinCombats += Math.Max(0, count);
    }

    internal static void RecordRippleBasinTurnForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.RippleBasinTurns += Math.Max(0, count);
    }

    /// <summary>
    /// Record Ripple Basin's no-attack turn-end trigger and arm observed
    /// block attribution. The actual block amount is observed by
    /// <see cref="Patches.HookAfterBlockGainedPatch"/>.
    /// </summary>
    public static void RecordRippleBasinActivationAndArmBlockAttribution()
    {
        lock (_lock)
        {
            try
            {
                _pendingCombat ??= new PendingCombat();
                var agg = GetOrCreateRelicAggregateLocked(RippleBasinRelicId);
                agg.Activations += 1;
                _pendingCombat.Windows.Arm(
                    RippleBasinRelicId,
                    AttributionEventKind.PlayerBlockGain,
                    CurrentHistoryCountLocked(),
                    maxHistoryAdvance: -1);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordRippleBasinActivationAndArmBlockAttribution failed: {e.Message}");
            }
        }
    }

    public static void DisarmRippleBasinBlockAttribution()
    {
        lock (_lock)
        {
            _pendingCombat?.Windows.Disarm(RippleBasinRelicId, AttributionEventKind.PlayerBlockGain);
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
    public static void RecordReptileTrinketActivation(
        ReptileTrinket relic,
        decimal strengthAdded)
    {
        var owner = relic?.Owner;
        if (owner == null || strengthAdded <= 0m) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic) || !IsTrackedPlayer(owner)) return;
                if (CombatManager.Instance?.IsInProgress != true) return;

                _pendingCombat ??= new PendingCombat();
                RecordReptileTrinketTurnForPlayerLocked(owner);

                var agg = GetOrCreatePendingRelicAggregateLocked(ReptileTrinketRelicId);
                RecordReptileTrinketActivationForTest(agg, strengthAdded);

                _pendingCombat.ReptileTrinketActivationsThisTurn.TryGetValue(
                    owner,
                    out var previousTurnActivations);
                var currentTurnActivations = previousTurnActivations + 1;
                _pendingCombat.ReptileTrinketActivationsThisTurn[owner] =
                    currentTurnActivations;
                RecordReptileTrinketTurnActivationTransitionForTest(
                    agg,
                    previousTurnActivations,
                    currentTurnActivations);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordReptileTrinketActivation failed: {e.Message}");
            }
        }
    }

    public static void RecordReptileTrinketTurnStarted(Player player)
    {
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(player)) return;
                if (CombatManager.Instance?.IsInProgress != true) return;

                _pendingCombat ??= new PendingCombat();
                RecordReptileTrinketTurnForPlayerLocked(player);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordReptileTrinketTurnStarted failed: {e.Message}");
            }
        }
    }

    internal static void RecordReptileTrinketActivationForTest(
        RelicAggregate agg,
        decimal strengthAdded)
    {
        if (agg == null || strengthAdded <= 0m) return;
        agg.Activations += 1;
        agg.StrengthAdded += strengthAdded;
    }

    internal static void RecordReptileTrinketTurnActivationTransitionForTest(
        RelicAggregate agg,
        int previousTurnActivations,
        int currentTurnActivations)
    {
        if (agg == null) return;

        if (previousTurnActivations == 1 && currentTurnActivations == 2)
        {
            agg.ReptileTrinketTurnsWithExactlyTwoActivations += 1;
        }
        else if (previousTurnActivations == 2 && currentTurnActivations == 3)
        {
            agg.ReptileTrinketTurnsWithExactlyTwoActivations = Math.Max(
                0,
                agg.ReptileTrinketTurnsWithExactlyTwoActivations - 1);
            agg.ReptileTrinketTurnsWithMoreThanTwoActivations += 1;
        }
    }

    internal static void RecordReptileTrinketTurnForTest(
        RelicAggregate agg,
        int count = 1)
    {
        if (agg == null) return;
        agg.ReptileTrinketTurns += Math.Max(0, count);
    }

    internal static void RecordReptileTrinketCombatForTest(
        RelicAggregate agg,
        int count = 1)
    {
        if (agg == null) return;
        agg.ReptileTrinketCombats += Math.Max(0, count);
    }

    /// <summary>
    /// Records the positive input/output delta at Beating Remnant's own
    /// post-Osty HP-loss modifier.
    /// </summary>
    public static void RecordBeatingRemnantHpLossPrevented(
        BeatingRemnant relic,
        Creature target,
        decimal amountBefore,
        decimal amountAfter)
    {
        var owner = relic?.Owner;
        if (owner?.Creature == null || target != owner.Creature) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic) || !IsTrackedPlayer(owner)) return;
                if (CombatManager.Instance?.IsInProgress != true) return;

                var prevented = CalculateBeatingRemnantHpLossPreventedForTest(
                    amountBefore,
                    amountAfter);
                if (prevented <= 0m) return;

                _pendingCombat ??= new PendingCombat();
                RecordBeatingRemnantCombatForPlayerLocked(owner);

                var agg = GetOrCreatePendingRelicAggregateLocked(BeatingRemnantRelicId);
                agg.BeatingRemnantHpLossPrevented += prevented;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug(
                    $"RecordBeatingRemnantHpLossPrevented failed: {e.Message}");
            }
        }
    }

    public static void RecordBeatingRemnantTurnStarted(BeatingRemnant relic)
    {
        var owner = relic?.Owner;
        if (owner == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic) || !IsTrackedPlayer(owner)) return;
                if (CombatManager.Instance?.IsInProgress != true) return;

                _pendingCombat ??= new PendingCombat();
                RecordBeatingRemnantTurnForPlayerLocked(owner);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordBeatingRemnantTurnStarted failed: {e.Message}");
            }
        }
    }

    internal static decimal CalculateBeatingRemnantHpLossPreventedForTest(
        decimal amountBefore,
        decimal amountAfter)
    {
        if (amountBefore <= 0m) return 0m;

        var appliedAmount = Math.Clamp(amountAfter, 0m, amountBefore);
        return amountBefore - appliedAmount;
    }

    internal static void RecordBeatingRemnantHpLossPreventedForTest(
        RelicAggregate agg,
        decimal amountBefore,
        decimal amountAfter)
    {
        if (agg == null) return;
        agg.BeatingRemnantHpLossPrevented +=
            CalculateBeatingRemnantHpLossPreventedForTest(amountBefore, amountAfter);
    }

    internal static void RecordBeatingRemnantTurnForTest(
        RelicAggregate agg,
        int count = 1)
    {
        if (agg == null) return;
        agg.BeatingRemnantTurns += Math.Max(0, count);
    }

    internal static void RecordBeatingRemnantCombatForTest(
        RelicAggregate agg,
        int count = 1)
    {
        if (agg == null) return;
        agg.BeatingRemnantCombats += Math.Max(0, count);
    }

    /// <summary>
    /// Attribute an observed generated Soul arrival to the card play that is
    /// still resolving. The Soul's current pile is authoritative, so hand-full
    /// or other redirections are counted at their actual destination.
    /// </summary>
    public static void RecordSoulAddedToCombatPile(
        CardModel soul,
        Player? creator)
    {
        if (soul is not Soul || creator == null) return;
        var pileType = soul.Pile?.Type;
        if (pileType is not (PileType.Draw or PileType.Hand or PileType.Discard))
            return;

        lock (_lock)
        {
            try
            {
                if (!ShouldTrackCardStatsDuringCombatLocked()) return;

                var sourceCard = FindCurrentlyResolvingCardPlay()?.Card;
                if (sourceCard?.Owner == null) return;
                if (!ReferenceEquals(sourceCard.Owner, creator)) return;
                if (!IsTrackedCard(sourceCard)) return;

                _pendingCombat ??= new PendingCombat();
                var sourceId = GetOrAssignInstanceId(sourceCard);
                var agg = GetOrCreateAggregate(_pendingCombat, sourceId);
                RecordSoulAddedToPileForTest(agg, pileType.Value);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordSoulAddedToCombatPile failed: {e.Message}");
            }
        }
    }

    internal static void RecordSoulAddedToPileForTest(
        CardAggregate agg,
        PileType pileType,
        int count = 1)
    {
        if (agg == null || count <= 0) return;

        switch (pileType)
        {
            case PileType.Draw:
                agg.SoulsAddedToDrawPile += count;
                break;
            case PileType.Hand:
                agg.SoulsAddedToHand += count;
                break;
            case PileType.Discard:
                agg.SoulsAddedToDiscardPile += count;
                break;
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
    /// exact combat card instances whose upgrade level actually increased.
    /// Those raw references remain stable as cards move between combat piles,
    /// allowing later finished plays to be attributed without treating the
    /// temporary upgrades as permanent deck changes.
    /// </summary>
    public static void RecordStoneCrackerActivation(
        StoneCracker relic,
        IReadOnlyCollection<CardModel> upgradedCards)
    {
        if (relic?.Owner == null || upgradedCards == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic) || !IsTrackedPlayer(relic.Owner)) return;

                _pendingCombat ??= new PendingCombat();
                RecordStoneCrackerCombatForPlayerLocked(relic.Owner);

                var observedCards = new HashSet<CardModel>(ReferenceEqualityComparer.Instance);
                foreach (var card in upgradedCards)
                {
                    if (card != null && ReferenceEquals(card.Owner, relic.Owner))
                        observedCards.Add(card);
                }
                var agg = GetOrCreatePendingRelicAggregateLocked(StoneCrackerRelicId);
                RecordStoneCrackerActivationForTest(
                    agg,
                    observedCards.Select(card => card.Rarity));
                foreach (var card in observedCards)
                    _pendingCombat.StoneCrackerUpgradedCards.Add(card);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordStoneCrackerActivation failed: {e.Message}");
            }
        }
    }

    public static void RecordStoneCrackerTurnStarted(Player player)
    {
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(player)) return;
                _pendingCombat ??= new PendingCombat();
                RecordStoneCrackerTurnForPlayerLocked(player);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordStoneCrackerTurnStarted failed: {e.Message}");
            }
        }
    }

    private static void RecordStoneCrackerUpgradedCardPlayedLocked(CardModel card)
    {
        if (_pendingCombat == null) return;
        if (!_pendingCombat.StoneCrackerUpgradedCards.Contains(card)) return;
        if (card.Owner is not Player player || !IsTrackedPlayer(player)) return;

        RecordStoneCrackerCombatForPlayerLocked(player);
        RecordStoneCrackerTurnForPlayerLocked(player);

        var agg = GetOrCreatePendingRelicAggregateLocked(StoneCrackerRelicId);
        RecordStoneCrackerUpgradedCardPlayForTest(agg);
    }

    internal static void RecordStoneCrackerActivationForTest(
        RelicAggregate agg,
        IEnumerable<CardRarity>? upgradedCardRarities)
    {
        if (agg == null) return;

        agg.Activations += 1;
        if (upgradedCardRarities == null) return;

        foreach (var rarity in upgradedCardRarities)
        {
            agg.CardsUpgraded += 1;
            switch (rarity)
            {
                case CardRarity.Common:
                    agg.StoneCrackerUpgradedCommons += 1;
                    break;
                case CardRarity.Uncommon:
                    agg.StoneCrackerUpgradedUncommons += 1;
                    break;
                case CardRarity.Rare:
                    agg.StoneCrackerUpgradedRares += 1;
                    break;
            }
        }
    }

    internal static void RecordStoneCrackerCombatForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.StoneCrackerCombats += Math.Max(0, count);
    }

    internal static void RecordStoneCrackerTurnForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.StoneCrackerTurns += Math.Max(0, count);
    }

    internal static void RecordStoneCrackerUpgradedCardPlayForTest(
        RelicAggregate agg,
        int count = 1)
    {
        if (agg == null) return;
        agg.StoneCrackerUpgradedCardPlays += Math.Max(0, count);
    }

    /// <summary>
    /// Record one card that Razor Tooth actually upgraded. The relic calls
    /// <c>CardCmd.Upgrade</c> synchronously from its owner-specific
    /// <c>AfterCardPlayed</c> callback, so callers pass the observed upgrade
    /// level before and after that callback rather than inferring success from
    /// card type or upgrade eligibility.
    /// </summary>
    public static void RecordRazorToothUpgrade(
        RazorTooth relic,
        CardModel card,
        int previousUpgradeLevel,
        int currentUpgradeLevel)
    {
        if (relic?.Owner == null || card == null) return;
        if (currentUpgradeLevel <= previousUpgradeLevel) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic)) return;
                if (!IsTrackedPlayer(relic.Owner)) return;
                if (!ReferenceEquals(card.Owner, relic.Owner)) return;

                _pendingCombat ??= new PendingCombat();
                RecordRazorToothCombatForPlayerLocked(relic.Owner);
                RecordRazorToothTurnForPlayerLocked(relic.Owner);

                var agg = GetOrCreateRelicAggregateLocked(RazorToothRelicId);
                RecordRazorToothUpgradeForTest(agg, previousUpgradeLevel, currentUpgradeLevel);
                _pendingCombat.RazorToothUpgradedCards.Add(card);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordRazorToothUpgrade failed: {e.Message}");
            }
        }
    }

    public static void RecordRazorToothTurnStarted(Player player)
    {
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(player)) return;
                _pendingCombat ??= new PendingCombat();
                RecordRazorToothTurnForPlayerLocked(player);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordRazorToothTurnStarted failed: {e.Message}");
            }
        }
    }

    private static void RecordRazorToothUpgradedCardPlayedLocked(CardModel card)
    {
        if (_pendingCombat == null) return;
        if (!_pendingCombat.RazorToothUpgradedCards.Contains(card)) return;
        if (card.Owner is not Player player || !IsTrackedPlayer(player)) return;

        RecordRazorToothCombatForPlayerLocked(player);
        RecordRazorToothTurnForPlayerLocked(player);

        var agg = GetOrCreatePendingRelicAggregateLocked(RazorToothRelicId);
        RecordRazorToothUpgradedCardPlayForTest(agg);
    }

    private static void RecordRazorToothUpgradedCardDrawnLocked(CardModel card)
    {
        if (_pendingCombat == null) return;
        if (!_pendingCombat.RazorToothUpgradedCards.Contains(card)) return;
        if (card.Owner is not Player player || !IsTrackedPlayer(player)) return;

        RecordRazorToothCombatForPlayerLocked(player);
        RecordRazorToothTurnForPlayerLocked(player);

        var agg = GetOrCreatePendingRelicAggregateLocked(RazorToothRelicId);
        RecordRazorToothUpgradedCardDrawForTest(agg);
    }

    private static void RecordWarHammerUpgradedCardPlayedLocked(CardModel card)
    {
        if (_pendingCombat == null) return;
        if (card.Owner is not Player player || !IsTrackedPlayer(player)) return;
        if (!TryGetInstanceId(card, out var instanceId)) return;

        var upgradedByWarHammer =
            (_currentRun?.RelicAggregates.TryGetValue(WarHammerRelicId, out var committed) == true
                && committed.WarHammerUpgradedCardInstanceIds?.Contains(
                    instanceId,
                    StringComparer.Ordinal) == true)
            || (_pendingCombat.RelicAggregates.TryGetValue(WarHammerRelicId, out var pending) == true
                && pending.WarHammerUpgradedCardInstanceIds?.Contains(
                    instanceId,
                    StringComparer.Ordinal) == true);
        if (!upgradedByWarHammer) return;

        RecordWarHammerCombatForPlayerLocked(player);
        RecordWarHammerTurnForPlayerLocked(player);

        var agg = GetOrCreatePendingRelicAggregateLocked(WarHammerRelicId);
        RecordWarHammerUpgradedCardPlayForTest(agg);
    }

    private static void RecordTriBoomerangInstinctCardPlayedLocked(CardModel card)
    {
        if (_pendingCombat == null) return;
        if (card?.Owner is not Player player || !IsTrackedPlayer(player)) return;
        if (!PlayerHasTriBoomerang(player)) return;
        if (card.Enchantment is not Instinct) return;
        if (!TryGetInstanceId(card, out var instanceId)) return;

        var enchantedByTriBoomerang =
            (_currentRun?.RelicAggregates.TryGetValue(
                    TriBoomerangRelicId,
                    out var committed) == true
                && committed.TriBoomerangInstinctCards?.Any(candidate =>
                    candidate != null
                    && string.Equals(
                        candidate.CardInstanceId,
                        instanceId,
                        StringComparison.Ordinal)) == true)
            || (_pendingCombat.RelicAggregates.TryGetValue(
                    TriBoomerangRelicId,
                    out var pending) == true
                && pending.TriBoomerangInstinctCards?.Any(candidate =>
                    candidate != null
                    && string.Equals(
                        candidate.CardInstanceId,
                        instanceId,
                        StringComparison.Ordinal)) == true);
        if (!enchantedByTriBoomerang) return;

        RecordTriBoomerangCombatForPlayerLocked(player);
        var agg = GetOrCreatePendingRelicAggregateLocked(TriBoomerangRelicId);
        RecordTriBoomerangInstinctCardPlayForTest(agg);
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

        lock (_lock)
        {
            try
            {
                var owner = cardPlay.Card.Owner;
                if (owner == null || !IsTrackedPlayer(owner)) return;

                _pendingCombat ??= new PendingCombat();
                RecordLetterOpenerCombatForPlayerLocked(owner);
                RecordLetterOpenerTurnForPlayerLocked(owner);

                var agg = GetOrCreatePendingRelicAggregateLocked(LetterOpenerRelicId);
                RecordLetterOpenerSkillPlayedForTest(agg);

                if (skillsPlayedIncludingThis <= 0 || skillsPlayedIncludingThis % activationThreshold != 0) return;

                int targetCount = CountLetterOpenerTargets(cardPlay.Card.CombatState);
                if (targetCount <= 0) return;

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

    /// <summary>
    /// Snapshot Letter Opener's live charge at the end of each tracked player
    /// turn while held.
    /// </summary>
    public static void RecordLetterOpenerTurnEnded(ICombatState? combatState, IEnumerable<Creature>? participants)
    {
        lock (_lock)
        {
            try
            {
                _pendingCombat ??= new PendingCombat();
                var recordedParticipant = false;

                if (participants != null)
                {
                    foreach (var creature in participants)
                    {
                        var player = creature?.Player;
                        if (player == null || !IsTrackedPlayer(player)) continue;

                        recordedParticipant |= RecordLetterOpenerTurnEndChargeForPlayerLocked(
                            player,
                            player.PlayerCombatState?.TurnNumber ?? 0);
                    }
                }

                if (recordedParticipant) return;

                var trackedPlayer = GetTrackedRunPlayerLocked();
                if (trackedPlayer == null) return;
                RecordLetterOpenerTurnEndChargeForPlayerLocked(
                    trackedPlayer,
                    trackedPlayer.PlayerCombatState?.TurnNumber ?? 0);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordLetterOpenerTurnEnded failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Capture Letter Opener's previous-turn charge before its own
    /// AfterSideTurnStart callback resets SkillsPlayedThisTurn.
    /// </summary>
    public static void RecordLetterOpenerPreviousTurnChargeBeforeReset(LetterOpener? relic, int endedTurnNumber)
    {
        if (relic?.Owner == null || endedTurnNumber <= 0) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic)) return;
                if (!IsTrackedPlayer(relic.Owner)) return;

                _pendingCombat ??= new PendingCombat();
                RecordLetterOpenerTurnEndChargeForPlayerLocked(relic.Owner, endedTurnNumber, relic);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordLetterOpenerPreviousTurnChargeBeforeReset failed: {e.Message}");
            }
        }
    }

    private static bool RecordLetterOpenerTurnEndChargeForPlayerLocked(
        Player player,
        int turnNumber,
        LetterOpener? letterOpener = null)
    {
        if (_pendingCombat == null) return false;
        if (player == null || !IsTrackedPlayer(player)) return false;
        letterOpener ??= TryGetLetterOpener(player, out var foundRelic) ? foundRelic : null;
        if (letterOpener == null) return false;
        if (turnNumber <= 0) return false;

        RecordLetterOpenerTurnForPlayerLocked(player, turnNumber);

        var charge = LetterOpenerCharge(letterOpener);
        if (charge != 1 && charge != 2) return true;

        if (_pendingCombat.LetterOpenerTurnEndChargeRecordedTurns.TryGetValue(player, out var recordedTurn)
            && recordedTurn == turnNumber)
        {
            return true;
        }

        _pendingCombat.LetterOpenerTurnEndChargeRecordedTurns[player] = turnNumber;
        var agg = GetOrCreatePendingRelicAggregateLocked(LetterOpenerRelicId);
        RecordLetterOpenerTurnEndChargeForTest(agg, charge);
        return true;
    }

    internal static void RecordLetterOpenerSkillPlayedForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.LetterOpenerSkillsPlayed += Math.Max(0, count);
    }

    internal static void RecordLetterOpenerCombatForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.LetterOpenerCombats += Math.Max(0, count);
    }

    internal static void RecordLetterOpenerTurnForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.LetterOpenerTurns += Math.Max(0, count);
    }

    internal static void RecordLetterOpenerTurnEndChargeForTest(RelicAggregate agg, int charge)
    {
        if (agg == null || charge < 0) return;

        if (charge == 1)
            agg.LetterOpenerTurnsEndedAt1Charge += 1;
        else if (charge == 2)
            agg.LetterOpenerTurnsEndedAt2Charges += 1;
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

    /// <summary>
    /// Record a Pen Nib-owned attack play. Mirrors Pen Nib.BeforeCardPlayed's
    /// owner/type checks so the total matches the relic's own charge counter.
    /// </summary>
    public static void RecordPenNibAttackPlayed(PenNib relic, CardPlay cardPlay)
    {
        if (relic?.Owner == null || cardPlay?.Card == null) return;
        if (cardPlay.Card.Type != CardType.Attack) return;
        if (!ReferenceEquals(cardPlay.Card.Owner, relic.Owner)) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic)) return;
                if (!IsTrackedPlayer(relic.Owner)) return;

                var agg = GetOrCreateRelicAggregateLocked(PenNibRelicId);
                RecordPenNibAttackPlayedForTest(agg);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordPenNibAttackPlayed failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Snapshot Pen Nib's live charge at the end of each tracked player turn.
    /// </summary>
    public static void RecordPenNibTurnEnded(IEnumerable<Creature>? participants)
    {
        if (participants == null) return;

        lock (_lock)
        {
            try
            {
                _pendingCombat ??= new PendingCombat();

                foreach (var creature in participants)
                {
                    var player = creature?.Player;
                    if (player == null || !IsTrackedPlayer(player)) continue;
                    if (!TryGetPenNib(player, out var penNib) || penNib == null) continue;

                    var turnNumber = player.PlayerCombatState?.TurnNumber ?? 0;
                    if (turnNumber <= 0) continue;
                    if (_pendingCombat.PenNibTurnEndChargeRecordedTurns.TryGetValue(player, out var recordedTurn)
                        && recordedTurn == turnNumber)
                    {
                        continue;
                    }

                    _pendingCombat.PenNibTurnEndChargeRecordedTurns[player] = turnNumber;
                    var agg = GetOrCreatePendingRelicAggregateLocked(PenNibRelicId);
                    RecordPenNibTurnEndChargeForTest(agg, penNib.AttacksPlayed);
                }
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordPenNibTurnEnded failed: {e.Message}");
            }
        }
    }

    internal static void RecordPenNibBaseDamageAddedForTest(RelicAggregate agg, decimal baseDamageAdded)
        => AddPenNibBaseDamageAdded(agg, baseDamageAdded);

    internal static void RecordPenNibAttackPlayedForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.PenNibAttacksPlayed += Math.Max(0, count);
    }

    internal static void RecordPenNibTurnEndChargeForTest(RelicAggregate agg, int charge)
    {
        if (agg == null || charge < 0) return;

        charge %= 10;
        agg.PenNibTurnEndChargeTotal += charge;
        agg.PenNibTurnEndChargeCount += 1;
        if (charge == 8)
            agg.PenNibTurnsEndedOn8Charges += 1;
        else if (charge == 9)
            agg.PenNibTurnsEndedOn9Charges += 1;
    }

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

    /// <summary>
    /// Record the extra block contributed by Unmovable's power. This is a
    /// run-level mechanic stat, not attribution back to the physical Unmovable
    /// card instance that created the power.
    /// </summary>
    public static void RecordUnmovablePowerExtraBlock(Creature? target, decimal extraBlock)
    {
        if (target == null || !target.IsPlayer || extraBlock <= 0m) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayerCreature(target)) return;
                if (!ShouldTrackCardStatsDuringCombatLocked()) return;

                _pendingCombat ??= new PendingCombat();
                RecordUnmovablePowerExtraBlockForTest(_pendingCombat.MetaStats, extraBlock);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordUnmovablePowerExtraBlock failed: {e.Message}");
            }
        }
    }

    internal static void RecordUnmovablePowerExtraBlockForTest(RunMetaStats metaStats, decimal extraBlock)
    {
        if (metaStats == null || extraBlock <= 0m) return;
        metaStats.ExtraBlockGainedFromUnmovablePower += extraBlock;
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

    public static bool BeginLargeCapsulePickup(
        LargeCapsule relic,
        out Player? player,
        out IReadOnlyCollection<RelicModel>? relicsBeforePickup)
    {
        player = null;
        relicsBeforePickup = null;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic)) return false;
                if (!IsTrackedPlayer(relic.Owner)) return false;

                player = relic.Owner;
                relicsBeforePickup = SnapshotRelics(player);
                return true;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"BeginLargeCapsulePickup failed: {e.Message}");
                player = null;
                relicsBeforePickup = null;
                return false;
            }
        }
    }

    public static void CompleteLargeCapsulePickup(
        Player? player,
        IReadOnlyCollection<RelicModel>? relicsBeforePickup,
        bool succeeded)
    {
        if (player == null || relicsBeforePickup == null) return;

        lock (_lock)
        {
            try
            {
                if (!succeeded || !IsTrackedPlayer(player)) return;

                var before = relicsBeforePickup as HashSet<RelicModel>
                    ?? new HashSet<RelicModel>(relicsBeforePickup, ReferenceEqualityComparer.Instance);
                var relicsGranted = NewRelicsSince(player, before).ToList();
                if (relicsGranted.Count == 0) return;

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(LargeCapsuleRelicId);
                foreach (var grantedRelic in relicsGranted)
                {
                    RecordLargeCapsuleRelicObtainedForTest(
                        agg,
                        grantedRelic.Id.ToString(),
                        GetRelicDisplayName(grantedRelic));
                }

                RefreshCurrentRunMetadataLocked();
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"CompleteLargeCapsulePickup failed: {e.Message}");
            }
        }
    }

    public static bool BeginNeowsBonesPickup(
        NeowsBones relic,
        out Player? player,
        out IReadOnlyCollection<RelicModel>? relicsBeforePickup,
        out IReadOnlyCollection<CardModel>? deckBeforePickup)
    {
        player = null;
        relicsBeforePickup = null;
        deckBeforePickup = null;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic)) return false;
                if (!IsTrackedPlayer(relic.Owner)) return false;

                player = relic.Owner;
                relicsBeforePickup = SnapshotRelics(player);
                deckBeforePickup = SnapshotDeckCards(player);
                return true;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"BeginNeowsBonesPickup failed: {e.Message}");
                player = null;
                relicsBeforePickup = null;
                deckBeforePickup = null;
                return false;
            }
        }
    }

    public static void CompleteNeowsBonesPickup(
        Player? player,
        IReadOnlyCollection<RelicModel>? relicsBeforePickup,
        IReadOnlyCollection<CardModel>? deckBeforePickup,
        bool succeeded)
    {
        if (player == null || relicsBeforePickup == null || deckBeforePickup == null) return;

        lock (_lock)
        {
            try
            {
                if (!succeeded || !IsTrackedPlayer(player)) return;

                var relicsBefore = relicsBeforePickup as HashSet<RelicModel>
                    ?? new HashSet<RelicModel>(relicsBeforePickup, ReferenceEqualityComparer.Instance);
                var deckBefore = deckBeforePickup as HashSet<CardModel>
                    ?? new HashSet<CardModel>(deckBeforePickup, ReferenceEqualityComparer.Instance);

                var relicsGranted = NewRelicsSince(player, relicsBefore).ToList();
                var cursesGranted = NewDeckCardsSince(player, deckBefore)
                    .Where(card => card.Type == CardType.Curse)
                    .ToList();
                if (relicsGranted.Count == 0 && cursesGranted.Count == 0) return;

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(NeowsBonesRelicId);
                foreach (var grantedRelic in relicsGranted)
                {
                    RecordNeowsBonesRelicObtainedForTest(
                        agg,
                        grantedRelic.Id.ToString(),
                        GetRelicDisplayName(grantedRelic));
                }

                foreach (var curse in cursesGranted)
                {
                    RecordNeowsBonesCurseGrantedForTest(
                        agg,
                        curse.Id.ToString(),
                        GetCardDisplayName(curse));
                }

                RefreshCurrentRunMetadataLocked();
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"CompleteNeowsBonesPickup failed: {e.Message}");
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
                RecordBrilliantScarfTurnForPlayerLocked(player);

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
                agg.BrilliantScarfEnergySavedForTurnAverage += energySaved;
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
        int combats = 0,
        int turns = 0)
    {
        if (agg == null) return;
        agg.DiscountCombats += Math.Max(0, combats);
        agg.DiscountTurns += Math.Max(0, turns);
        agg.DiscountsOffered += Math.Max(0, offers);
        agg.DiscountsTaken += Math.Max(0, taken);
        var observedEnergySaved = Math.Max(0, energySaved);
        agg.EnergySavedByDiscount += observedEnergySaved;
        agg.BrilliantScarfEnergySavedForTurnAverage += observedEnergySaved;
    }

    public static void RecordBrilliantScarfTurnStarted(Player player)
    {
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(player)) return;
                RecordBrilliantScarfTurnForPlayerLocked(player);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordBrilliantScarfTurnStarted failed: {e.Message}");
            }
        }
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

    /// <summary>
    /// Record map-point stats from the original point type before the game
    /// resolves it into a concrete room. This keeps Juzu's ? count and Cursed
    /// Pearl's first-shop floor count tied to the visible map node.
    /// </summary>
    public static void RecordMapPointEntered(MapPointType pointType, bool saveGame)
    {
        if (!saveGame) return;

        lock (_lock)
        {
            try
            {
                var player = GetTrackedRunPlayerLocked();
                if (player == null) return;

                var changed = false;
                if (pointType == MapPointType.Unknown && PlayerHasJuzuBracelet(player))
                {
                    var agg = GetOrCreateCurrentRunRelicAggregateLocked(JuzuBraceletRelicId);
                    RecordJuzuQuestionSiteEnteredForTest(agg);
                    changed = true;
                }

                if (pointType == MapPointType.Shop && PlayerHasCursedPearl(player))
                {
                    var agg = GetOrCreateCurrentRunRelicAggregateLocked(CursedPearlRelicId);
                    changed |= RecordCursedPearlFloorsBeforeFirstShopForTest(
                        agg,
                        CurrentRunFloorLocked() ?? 0);
                }

                if (changed)
                    SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordMapPointEntered failed: {e.Message}");
            }
        }
    }

    internal static void RecordJuzuQuestionSiteEnteredForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null || count <= 0) return;
        agg.QuestionMarkSitesEntered += count;
    }

    /// <summary>
    /// Snapshot Dowsing's own saved room counter after it changes. The quest
    /// card, not Dowsing Rod, owns the authoritative five-room countdown.
    /// </summary>
    public static void RecordDowsingRoomsEntered(Dowsing? dowsing)
    {
        if (dowsing == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedCard(dowsing)) return;
                if (!RefreshDowsingRoomsRemainingIfOwnedLocked(dowsing.Owner, dowsing)) return;
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordDowsingRoomsEntered failed: {e.Message}");
            }
        }
    }

    internal static bool RecordDowsingRoomsEnteredForTest(
        RelicAggregate agg,
        int roomsEntered)
    {
        if (agg == null) return false;

        var roomsRemaining = Math.Clamp(Dowsing.maxRooms - roomsEntered, 0, Dowsing.maxRooms);
        if (agg.DowsingQuestionRoomsRemaining == roomsRemaining) return false;
        agg.DowsingQuestionRoomsRemaining = roomsRemaining;
        return true;
    }

    internal static int? GetLiveDowsingRoomsRemaining()
    {
        lock (_lock)
        {
            try
            {
                var player = GetTrackedRunPlayerLocked();
                if (player == null || !PlayerHasDowsingRod(player)) return null;

                var roomsEntered = GetLiveDowsingRoomsEnteredLocked(player);
                return roomsEntered.HasValue
                    ? Math.Clamp(Dowsing.maxRooms - roomsEntered.Value, 0, Dowsing.maxRooms)
                    : null;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"GetLiveDowsingRoomsRemaining failed: {e.Message}");
                return null;
            }
        }
    }

    internal static bool RecordCursedPearlFloorsBeforeFirstShopForTest(RelicAggregate agg, int floorsAscended)
    {
        if (agg == null || agg.FloorsAscendedBeforeFirstShop.HasValue) return false;

        agg.FloorsAscendedBeforeFirstShop = Math.Max(0, floorsAscended);
        return true;
    }

    /// <summary>
    /// Record the first actual merchant room reached after Signet Ring was
    /// obtained. Hook.AfterRoomEntered runs after map history has appended the
    /// destination, so TotalFloor is the reached shop floor rather than the
    /// floor the player just left. RelicModel.FloorAddedToDeck is the game's
    /// durable pickup-floor snapshot and survives save/continue and hot reload.
    /// </summary>
    public static void RecordSignetRingShopReached(IRunState runState, AbstractRoom room)
    {
        if (runState == null || room is not MerchantRoom) return;

        lock (_lock)
        {
            try
            {
                var player = GetTrackedRunPlayerLocked();
                if (player == null || !ReferenceEquals(player.RunState, runState)) return;

                var signetRing = player.Relics.OfType<SignetRing>().FirstOrDefault();
                if (signetRing == null) return;

                var pickupFloor = RelicFloorAddedToDeckIncludingRunStart(signetRing);
                if (!pickupFloor.HasValue) return;

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(SignetRingRelicId);
                if (!RecordSignetRingFloorsToNextShopForTest(agg, pickupFloor.Value, runState.TotalFloor))
                    return;

                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordSignetRingShopReached failed: {e.Message}");
            }
        }
    }

    internal static bool RecordSignetRingFloorsToNextShopForTest(
        RelicAggregate agg,
        int pickupFloor,
        int shopFloor)
    {
        if (agg == null || agg.FloorsTraveledUntilNextShop.HasValue) return false;

        agg.FloorsTraveledUntilNextShop = Math.Max(0, shopFloor - pickupFloor);
        return true;
    }

    /// <summary>
    /// Persist the original map-point category reached by a confirmed Winged
    /// Boots charge. TimesUsed is the authoritative use number and is saved by
    /// the game itself.
    /// </summary>
    public static void RecordWingedBootsDestination(
        WingedBoots relic,
        int useNumber,
        MapPointType pointType)
    {
        if (relic == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic) || !IsTrackedPlayer(relic.Owner)) return;

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(WingedBootsRelicId);
                if (!RecordWingedBootsDestinationForTest(agg, useNumber, pointType)) return;

                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordWingedBootsDestination failed: {e.Message}");
            }
        }
    }

    internal static bool RecordWingedBootsDestinationForTest(
        RelicAggregate agg,
        int useNumber,
        MapPointType pointType)
    {
        if (agg == null || useNumber is < 1 or > 3) return false;

        agg.WingedBootsDestinations ??= new List<WingedBootsDestinationAggregate>();
        if (agg.WingedBootsDestinations.Any(entry => entry.UseNumber == useNumber))
            return false;

        agg.WingedBootsDestinations.Add(new WingedBootsDestinationAggregate
        {
            UseNumber = useNumber,
            Destination = WingedBootsDestinationId(pointType),
        });
        agg.WingedBootsDestinations.Sort((left, right) => left.UseNumber.CompareTo(right.UseNumber));
        return true;
    }

    internal static string FormatWingedBootsDestination(string? destination)
        => destination switch
        {
            "combat" => "combat",
            "shop" => "shop",
            "question_mark" => "?",
            "elite" => "elite",
            "treasure" => "treasure",
            "rest_site" => "rest site",
            "boss" => "boss",
            "ancient" => "ancient",
            "unassigned" => "unknown",
            _ => "unknown",
        };

    private static string WingedBootsDestinationId(MapPointType pointType)
        => pointType switch
        {
            MapPointType.Monster => "combat",
            MapPointType.Shop => "shop",
            MapPointType.Unknown => "question_mark",
            MapPointType.Elite => "elite",
            MapPointType.Treasure => "treasure",
            MapPointType.RestSite => "rest_site",
            MapPointType.Boss => "boss",
            MapPointType.Ancient => "ancient",
            _ => "unassigned",
        };

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

    public static bool BeginArcaneScrollPickup(
        ArcaneScroll relic,
        out Player? player,
        out IReadOnlyCollection<CardModel>? deckBeforePickup)
    {
        player = null;
        deckBeforePickup = null;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic)) return false;
                if (!IsTrackedPlayer(relic.Owner)) return false;

                player = relic.Owner;
                deckBeforePickup = SnapshotDeckCards(player);
                return true;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"BeginArcaneScrollPickup failed: {e.Message}");
                player = null;
                deckBeforePickup = null;
                return false;
            }
        }
    }

    public static void CompleteArcaneScrollPickup(
        Player? player,
        IReadOnlyCollection<CardModel>? deckBeforePickup,
        bool succeeded)
    {
        if (player == null || deckBeforePickup == null) return;

        lock (_lock)
        {
            try
            {
                if (!succeeded || !IsTrackedPlayer(player)) return;

                var before = deckBeforePickup as HashSet<CardModel>
                    ?? new HashSet<CardModel>(deckBeforePickup, ReferenceEqualityComparer.Instance);
                var grantedRareCards = NewDeckCardsSince(player, before)
                    .Where(card => card.Rarity == CardRarity.Rare)
                    .ToList();
                if (grantedRareCards.Count == 0) return;

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(ArcaneScrollRelicId);
                foreach (var card in grantedRareCards)
                {
                    RecordArcaneScrollRareReceivedForTest(
                        agg,
                        card.Id.ToString(),
                        GetCardDisplayName(card));
                }

                RefreshCurrentRunMetadataLocked();
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"CompleteArcaneScrollPickup failed: {e.Message}");
            }
        }
    }

    internal static void RecordArcaneScrollRareReceivedForTest(RelicAggregate agg, string? cardId, string? displayName)
    {
        if (agg == null || string.IsNullOrWhiteSpace(cardId)) return;
        AddRelicCardGranted(agg.CardsGranted, cardId, displayName ?? "", 1);
    }

    private static IReadOnlyCollection<CardModel> SnapshotDeckCards(Player player)
    {
        var cards = new HashSet<CardModel>(ReferenceEqualityComparer.Instance);

        try
        {
            foreach (var card in player.Deck.Cards)
            {
                if (card != null)
                    cards.Add(card);
            }
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"SnapshotDeckCards failed: {e.Message}");
        }

        return cards;
    }

    private static IReadOnlyList<CardModel> NewDeckCardsSince(Player player, HashSet<CardModel> deckBefore)
    {
        var cards = new List<CardModel>();

        try
        {
            foreach (var card in player.Deck.Cards)
            {
                if (card != null && !deckBefore.Contains(card))
                    cards.Add(card);
            }
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"NewDeckCardsSince failed: {e.Message}");
        }

        return cards;
    }

    private static IReadOnlyCollection<RelicModel> SnapshotRelics(Player player)
    {
        var relics = new HashSet<RelicModel>(ReferenceEqualityComparer.Instance);

        try
        {
            foreach (var relic in player.Relics)
            {
                if (relic != null)
                    relics.Add(relic);
            }
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"SnapshotRelics failed: {e.Message}");
        }

        return relics;
    }

    private static IReadOnlyList<RelicModel> NewRelicsSince(Player player, HashSet<RelicModel> relicsBefore)
    {
        var relics = new List<RelicModel>();

        try
        {
            foreach (var relic in player.Relics)
            {
                if (relic != null && !relicsBefore.Contains(relic))
                    relics.Add(relic);
            }
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"NewRelicsSince failed: {e.Message}");
        }

        return relics;
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
    /// Snapshot the cards Pael's Claw actually gave Goopy after its pickup
    /// callback completes. Pickup is a map event, so persist it directly.
    /// </summary>
    public static void RecordPaelsClawObtained(PaelsClaw relic)
    {
        if (relic?.Owner == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic) || !IsTrackedPlayer(relic.Owner)) return;
                EnsureLazyCurrentRunLocked();
                if (!RefreshPaelsClawSnapshotIfOwnedLocked(relic.Owner)) return;

                RefreshCurrentRunMetadataLocked();
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordPaelsClawObtained failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record the observed permanent Goopy amount gained from the enchantment's
    /// own completed post-play callback.
    /// </summary>
    public static void RecordPaelsClawGoopyEnhancement(
        CardModel card,
        int startingGoopyAmount,
        int resultingGoopyAmount)
    {
        if (card?.Owner is not Player player) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(player) || !PlayerHasPaelsClaw(player)) return;
                if (CombatManager.Instance?.IsInProgress != true) return;

                _pendingCombat ??= new PendingCombat();
                RecordPaelsClawCombatForPlayerLocked(player);
                RecordPaelsClawTurnForPlayerLocked(player);

                var agg = GetOrCreatePendingRelicAggregateLocked(PaelsClawRelicId);
                RecordPaelsClawEnhancementForTest(
                    agg,
                    startingGoopyAmount,
                    resultingGoopyAmount);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordPaelsClawGoopyEnhancement failed: {e.Message}");
            }
        }
    }

    public static void RecordPaelsClawTurnStarted(Player player)
    {
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(player)) return;
                _pendingCombat ??= new PendingCombat();
                RecordPaelsClawTurnForPlayerLocked(player);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordPaelsClawTurnStarted failed: {e.Message}");
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
    /// Record the direct artifact Pael's Wing adds to its owner's inventory
    /// after a completed pair of sacrifices.
    /// </summary>
    public static void RecordPaelsWingArtifactGained(RelicModel artifactGained)
    {
        if (artifactGained == null) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateCurrentRunRelicAggregateLocked(PaelsWingRelicId);
                RecordPaelsWingArtifactGainedForTest(
                    agg,
                    artifactGained.Id.ToString(),
                    GetRelicDisplayName(artifactGained));
                RefreshCurrentRunMetadataLocked();
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordPaelsWingArtifactGained failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record the final physical deck card returned by Pael's Tooth after its
    /// combat-end callback succeeds. The caller observes the new raw deck
    /// instance after upgrade and deck-add modifiers have finished. Route it
    /// through pending combat until the normal CombatEnded promotion.
    /// </summary>
    public static void RecordPaelsToothCardReturned(CardModel cardReturned)
    {
        if (cardReturned == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(cardReturned.Owner)) return;
                if (_currentRun == null && _pendingCombat == null) return;

                var persistDirectlyToRun = _pendingCombat == null;
                var agg = GetOrCreateRelicAggregateForCurrentContextLocked(PaelsToothRelicId);
                RecordPaelsToothCardReturnedForTest(
                    agg,
                    GetCardIdForStats(cardReturned),
                    GetCardDisplayNameForStats(cardReturned),
                    cardReturned.CurrentUpgradeLevel);
                if (persistDirectlyToRun)
                {
                    RefreshCurrentRunMetadataLocked();
                    SaveCurrentRun();
                }
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordPaelsToothCardReturned failed: {e.Message}");
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
    /// Record the observed maximum-HP cost of Brightest Flame after its full
    /// async OnPlay callback resolves. The game clamps LoseMaxHp at one max HP,
    /// so the before/after delta is the truth rather than the card's requested
    /// amount.
    /// </summary>
    public static void RecordBrightestFlameMaxHpLost(
        BrightestFlame card,
        int previousMaxHp,
        int currentMaxHp)
    {
        if (card == null || currentMaxHp >= previousMaxHp) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedCard(card)) return;
                if (!ShouldTrackCardStatsDuringCombatLocked()) return;

                _pendingCombat ??= new PendingCombat();
                var instanceId = GetOrAssignInstanceId(card);
                var agg = GetOrCreateAggregate(_pendingCombat, instanceId);
                RecordBrightestFlameMaxHpLostForTest(agg, previousMaxHp, currentMaxHp);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordBrightestFlameMaxHpLost failed: {e.Message}");
            }
        }
    }

    internal static void RecordBrightestFlameMaxHpLostForTest(
        CardAggregate agg,
        int previousMaxHp,
        int currentMaxHp)
    {
        if (agg == null || currentMaxHp >= previousMaxHp) return;
        agg.TotalMaxHpLost += Math.Max(0, previousMaxHp - currentMaxHp);
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
                if (!ShouldTrackCardStatsDuringCombatLocked()) return;

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
                if (!ShouldTrackCardStatsDuringCombatLocked()) return;

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
                if (!ShouldTrackCardStatsDuringCombatLocked()) return;

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
        if (healedCreature == null || attemptedHealing <= 0m) return;

        lock (_lock)
        {
            // CombatSetUp establishes Pantograph's activation before the
            // game's BeforeCombatStart hook runs. Keep the relic patch as a
            // fallback for unusual setup/reload ordering, but do not let both
            // observations count the same boss combat twice.
            if (_pendingCombat != null
                && !_pendingCombat.PantographActivationCountedCreatures.Add(healedCreature))
            {
                return;
            }

            RecordRelicHealingTrigger(
                PantographRelicId,
                healedCreature,
                attemptedHealing,
                nameof(RecordPantographTrigger));
        }
    }

    /// <summary>
    /// Establish Pantograph's activation and healing observation window once
    /// per boss combat. CombatSetUp fires once for each Glory boss, before the
    /// game's BeforeCombatStart callbacks, so chained bosses cannot collapse
    /// into one activation.
    /// </summary>
    private static void RecordPantographCombatStartForTrackedPlayerLocked(CombatState state)
    {
        if (_pendingCombat == null || state?.Players == null) return;

        foreach (var player in state.Players)
        {
            if (!IsTrackedPlayer(player) || player?.Creature == null || player.Creature.IsDead)
                continue;

            var pantograph = player.Relics?.OfType<Pantograph>().FirstOrDefault();
            if (pantograph == null || !IsTrackedRelic(pantograph))
                continue;
            if (pantograph.Owner?.RunState?.CurrentRoom?.RoomType != RoomType.Boss)
                continue;

            decimal attemptedHealing = pantograph.DynamicVars?.Heal?.BaseValue ?? 0m;
            RecordPantographTrigger(player.Creature, attemptedHealing);
        }
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
    /// Record Lizard Tail's one-shot death prevention heal and activation
    /// floor. The pickup floor is restamped only when it was not already saved
    /// at obtain time.
    /// </summary>
    public static void RecordLizardTailTrigger(LizardTail relic, Creature healedCreature, decimal attemptedHealing)
    {
        if (!IsLizardTailStatsRelic(relic) || healedCreature == null) return;
        if (!ReferenceEquals(healedCreature, relic.Owner?.Creature)) return;
        if (!IsTrackedRelic(relic)) return;

        RecordRelicHealingTrigger(
            LizardTailRelicId,
            healedCreature,
            attemptedHealing,
            nameof(RecordLizardTailTrigger),
            configureAggregate: agg =>
            {
                RecordRelicFloorAcquiredForTest(agg, RelicFloorAddedToDeck(relic) ?? CurrentRunFloorLocked());
                RecordRelicFloorActivatedForTest(agg, CurrentRunFloorLocked());
            });
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
    /// Arm Fragrant Mushroom pickup attribution. Actual upgraded cards are
    /// observed from <see cref="RecordUpgrade"/> while the pickup task resolves.
    /// </summary>
    public static bool BeginFragrantMushroomPickup(RelicModel relic, out Player? player)
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
                _pendingFragrantMushroomPickups[player] = new PendingFragrantMushroomPickup();
                return true;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"BeginFragrantMushroomPickup failed: {e.Message}");
                player = null;
                return false;
            }
        }
    }

    public static void CompleteFragrantMushroomPickup(Player? player, bool succeeded)
    {
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                if (!_pendingFragrantMushroomPickups.Remove(player, out var pending)) return;
                if (!succeeded) return;

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(FragrantMushroomRelicId);
                RecordFragrantMushroomUpgradesForTest(agg, pending.UpgradedCards);
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"CompleteFragrantMushroomPickup failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Arm a narrowly scoped Fishing Rod attribution window around its native
    /// end-of-combat callback. The actual upgraded card is observed by
    /// <see cref="RecordUpgrade"/> when Fishing Rod calls CardCmd.Upgrade.
    /// </summary>
    public static bool BeginFishingRodUpgrade(
        FishingRod relic,
        CombatRoom room,
        out Player? player)
    {
        player = null;
        if (relic?.Owner == null || room?.Encounter?.RoomType != RoomType.Monster)
            return false;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic)) return false;
                if (!IsTrackedPlayer(relic.Owner)) return false;

                player = relic.Owner;
                _pendingFishingRodUpgrades[player] = new PendingFishingRodUpgrade();
                return true;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"BeginFishingRodUpgrade failed: {e.Message}");
                player = null;
                return false;
            }
        }
    }

    public static void CompleteFishingRodUpgrade(Player? player, bool succeeded)
    {
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                if (!_pendingFishingRodUpgrades.Remove(player, out var pending)) return;
                if (!succeeded || pending.UpgradedCards.Count == 0) return;

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(FishingRodRelicId);
                RecordFishingRodUpgradesForTest(agg, pending.UpgradedCards);
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"CompleteFishingRodUpgrade failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Arm an attribution window around War Hammer's Elite-victory callback.
    /// The callback upgrades permanent deck cards synchronously, so
    /// <see cref="RecordUpgrade"/> can capture both their display names and
    /// stable instance ids before combat promotion.
    /// </summary>
    public static bool BeginWarHammerActivation(
        WarHammer relic,
        CombatRoom room,
        out Player? player)
    {
        player = null;
        if (relic?.Owner == null || room == null || room.RoomType != RoomType.Elite)
            return false;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic)) return false;
                if (!IsTrackedPlayer(relic.Owner)) return false;

                player = relic.Owner;
                _pendingCombat ??= new PendingCombat();
                RecordWarHammerCombatForPlayerLocked(player);
                RecordWarHammerTurnForPlayerLocked(player);
                _pendingWarHammerActivations[player] = new PendingWarHammerActivation();
                return true;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"BeginWarHammerActivation failed: {e.Message}");
                player = null;
                return false;
            }
        }
    }

    public static void CompleteWarHammerActivation(Player? player, bool succeeded)
    {
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                if (!_pendingWarHammerActivations.Remove(player, out var pending)) return;
                if (!succeeded) return;

                var agg = GetOrCreatePendingRelicAggregateLocked(WarHammerRelicId);
                RecordWarHammerActivationForTest(
                    agg,
                    pending.UpgradedCards,
                    pending.UpgradedCardInstanceIds);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"CompleteWarHammerActivation failed: {e.Message}");
            }
        }
    }

    public static void RecordWarHammerTurnStarted(Player player)
    {
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(player)) return;
                _pendingCombat ??= new PendingCombat();
                RecordWarHammerTurnForPlayerLocked(player);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordWarHammerTurnStarted failed: {e.Message}");
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
    /// Record Mango's observed pickup max-HP gain after its async pickup
    /// effect resolves.
    /// </summary>
    public static void RecordMangoMaxHpGained(
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
                var agg = GetOrCreateCurrentRunRelicAggregateLocked(MangoRelicId);
                RecordMangoMaxHpGainedForTest(agg, maxHpGained, originalMaxHp, newMaxHp);
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordMangoMaxHpGained failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record Nutritious Oyster's observed pickup max-HP gain after its async
    /// pickup effect resolves.
    /// </summary>
    public static void RecordNutritiousOysterMaxHpGained(
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
                var agg = GetOrCreateCurrentRunRelicAggregateLocked(NutritiousOysterRelicId);
                RecordNutritiousOysterMaxHpGainedForTest(agg, maxHpGained, originalMaxHp, newMaxHp);
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordNutritiousOysterMaxHpGained failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record one completed Stone Humidifier rest-site trigger using the
    /// owner's observed max HP immediately before and after the async game
    /// command. This is saved outside the combat buffer because rest sites are
    /// run-map events.
    /// </summary>
    public static void RecordStoneHumidifierMaxHpGain(
        Creature creature,
        decimal startingMaxHp,
        decimal resultingMaxHp)
    {
        if (creature?.Player == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(creature.Player)) return;

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(StoneHumidifierRelicId);
                RecordStoneHumidifierMaxHpGainForTest(agg, startingMaxHp, resultingMaxHp);
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordStoneHumidifierMaxHpGain failed: {e.Message}");
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
    /// Record Lizard Tail's pickup floor as soon as the relic enters the run,
    /// so a never-used tail still has durable run-history context.
    /// </summary>
    public static void RecordLizardTailObtained(RelicModel relic, Player player)
    {
        if (!IsLizardTailStatsRelic(relic) || player == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(player)) return;

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(LizardTailRelicId);
                RecordRelicFloorAcquiredForTest(agg, RelicFloorAddedToDeck(relic) ?? CurrentRunFloorLocked());
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordLizardTailObtained failed: {e.Message}");
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
    /// Record one completed Lucky Fysh permanent-deck callback and the actual
    /// gold added to its tracked owner's balance.
    /// </summary>
    public static void RecordLuckyFyshCardAdded(
        LuckyFysh relic,
        Player owner,
        int initialGold,
        int currentGold)
    {
        if (relic == null || owner == null) return;

        lock (_lock)
        {
            try
            {
                if (!ReferenceEquals(relic.Owner, owner)
                    || !IsTrackedRelic(relic)
                    || !IsTrackedPlayer(owner))
                    return;

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(LuckyFyshRelicId);
                RecordLuckyFyshCardAddedForTest(agg, initialGold, currentGold);
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordLuckyFyshCardAdded failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record Book of Five Rings at the successful relic-obtain boundary so
    /// its cards-per-floor denominator exists even before the first card is
    /// added to the deck.
    /// </summary>
    public static void RecordBookOfFiveRingsObtained(BookOfFiveRings relic, Player player)
    {
        if (relic == null || player == null) return;

        lock (_lock)
        {
            try
            {
                if (!ReferenceEquals(relic.Owner, player)
                    || !IsTrackedRelic(relic)
                    || !IsTrackedPlayer(player))
                    return;

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(BookOfFiveRingsRelicId);
                RecordRelicFloorAcquiredForTest(
                    agg,
                    RelicFloorAddedToDeck(relic) ?? CurrentRunFloorLocked());
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordBookOfFiveRingsObtained failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record a confirmed permanent-deck addition from Book of Five Rings's
    /// owner-specific pile callback. When the native saved counter reaches its
    /// five-card threshold, also arm the shared observed-healing ledger before
    /// the relic starts its heal command.
    /// </summary>
    public static void RecordBookOfFiveRingsCardAdded(
        BookOfFiveRings relic,
        bool triggered,
        decimal attemptedHealing)
    {
        if (relic?.Owner?.Creature == null) return;

        Creature? healedCreature = null;
        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic) || !IsTrackedPlayer(relic.Owner)) return;

                var persistDirectlyToRun = _pendingCombat == null;
                var agg = GetOrCreateRelicAggregateForCurrentContextLocked(BookOfFiveRingsRelicId);
                RecordRelicFloorAcquiredForTest(
                    agg,
                    RelicFloorAddedToDeck(relic) ?? CurrentRunFloorLocked());
                RecordBookOfFiveRingsCardAddedForTest(agg);

                if (!triggered && persistDirectlyToRun)
                    SaveCurrentRun();

                if (triggered)
                    healedCreature = relic.Owner.Creature;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordBookOfFiveRingsCardAdded failed: {e.Message}");
                return;
            }
        }

        if (healedCreature == null) return;
        RecordRelicHealingTrigger(
            BookOfFiveRingsRelicId,
            healedCreature,
            attemptedHealing,
            nameof(RecordBookOfFiveRingsCardAdded),
            configureAggregate: agg =>
                RecordRelicFloorAcquiredForTest(
                    agg,
                    RelicFloorAddedToDeck(relic) ?? CurrentRunFloorLocked()));
    }

    /// <summary>
    /// Count one completed outer card reward skip while the tracked player
    /// owns Book of Five Rings.
    /// </summary>
    public static void RecordBookOfFiveRingsCardRewardSkipped(CardReward reward)
    {
        var player = reward?.Player;
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(player)) return;

                var relic = player.Relics?
                    .OfType<BookOfFiveRings>()
                    .FirstOrDefault(candidate => IsTrackedRelic(candidate));
                if (relic == null) return;

                var persistDirectlyToRun = _pendingCombat == null;
                var agg = GetOrCreateRelicAggregateForCurrentContextLocked(BookOfFiveRingsRelicId);
                RecordRelicFloorAcquiredForTest(
                    agg,
                    RelicFloorAddedToDeck(relic) ?? CurrentRunFloorLocked());
                RecordBookOfFiveRingsCardRewardSkippedForTest(agg);

                if (persistDirectlyToRun)
                    SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordBookOfFiveRingsCardRewardSkipped failed: {e.Message}");
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
    /// deck split and projecting the live combat-card split.
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

                result ??= new RelicAggregate();
                var player = GetTrackedRunPlayerLocked();
                if (player != null && PlayerHasMiniatureCannon(player))
                {
                    SetMiniatureCannonCombatCountsForTest(
                        result,
                        player.PlayerCombatState?.AllCards);
                }

                return result;
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

    internal static void RecordPaelsClawSnapshotForTest(
        RelicAggregate agg,
        int goopyCards,
        int earnedEnhancements)
    {
        if (agg == null) return;

        agg.PaelsClawGoopyCards = Math.Max(
            agg.PaelsClawGoopyCards,
            Math.Max(0, goopyCards));
        agg.PaelsClawGoopyEnhancements = Math.Max(
            agg.PaelsClawGoopyEnhancements,
            Math.Max(0, earnedEnhancements));
    }

    internal static void RecordPaelsClawGoopyCardPlayedForTest(
        RelicAggregate agg,
        int count = 1)
    {
        if (agg == null) return;
        agg.PaelsClawGoopyCardsPlayed += Math.Max(0, count);
    }

    internal static void RecordPaelsClawEnhancementForTest(
        RelicAggregate agg,
        int startingGoopyAmount,
        int resultingGoopyAmount)
    {
        if (agg == null) return;
        agg.PaelsClawGoopyEnhancements += Math.Max(
            0,
            resultingGoopyAmount - startingGoopyAmount);
    }

    internal static void RecordPaelsClawTurnForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.PaelsClawTurns += Math.Max(0, count);
    }

    internal static void RecordPaelsClawCombatForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.PaelsClawCombats += Math.Max(0, count);
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

    internal static void RecordRelicFloorAcquiredForTest(RelicAggregate agg, int? floor)
    {
        if (agg == null || !floor.HasValue || floor.Value <= 0) return;
        agg.FloorAcquired ??= floor.Value;
    }

    internal static void RecordRelicFloorActivatedForTest(RelicAggregate agg, int? floor)
    {
        if (agg == null || !floor.HasValue || floor.Value <= 0) return;
        agg.FloorActivated = floor.Value;
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

    internal static void RecordMangoMaxHpGainedForTest(
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

    internal static void RecordNutritiousOysterMaxHpGainedForTest(
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

    internal static void RecordStoneHumidifierMaxHpGainForTest(
        RelicAggregate agg,
        decimal startingMaxHp,
        decimal resultingMaxHp)
    {
        if (agg == null) return;

        var starting = Math.Max(0m, startingMaxHp);
        var resulting = Math.Max(0m, resultingMaxHp);

        agg.Activations += 1;
        agg.MaxHpGained += Math.Max(0m, resulting - starting);
        agg.MaxHpActivations ??= new List<RelicMaxHpActivationAggregate>();
        agg.MaxHpActivations.Add(new RelicMaxHpActivationAggregate
        {
            StartingHp = starting,
            ResultingHp = resulting,
        });
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

    internal static void RecordRazorToothUpgradeForTest(
        RelicAggregate agg,
        int previousUpgradeLevel,
        int currentUpgradeLevel)
    {
        if (agg == null || currentUpgradeLevel <= previousUpgradeLevel) return;
        agg.CardsUpgraded += 1;
    }

    internal static void RecordRazorToothCombatForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.RazorToothCombats += Math.Max(0, count);
    }

    internal static void RecordRazorToothTurnForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.RazorToothTurns += Math.Max(0, count);
    }

    internal static void RecordRazorToothUpgradedCardPlayForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.RazorToothUpgradedCardPlays += Math.Max(0, count);
    }

    internal static void RecordRazorToothUpgradedCardDrawForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.RazorToothUpgradedCardDraws += Math.Max(0, count);
    }

    internal static void RecordWhetstoneUpgradesForTest(
        RelicAggregate agg,
        IEnumerable<string>? upgradedCards)
        => RecordRelicUpgradedCards(agg, upgradedCards);

    internal static void RecordWarPaintUpgradesForTest(
        RelicAggregate agg,
        IEnumerable<string>? upgradedCards)
        => RecordRelicUpgradedCards(agg, upgradedCards);

    internal static void RecordFragrantMushroomUpgradesForTest(
        RelicAggregate agg,
        IEnumerable<string>? upgradedCards)
        => RecordRelicUpgradedCards(agg, upgradedCards);

    internal static void RecordFishingRodUpgradesForTest(
        RelicAggregate agg,
        IEnumerable<string>? upgradedCards)
        => RecordRelicUpgradedCards(agg, upgradedCards);

    internal static void RecordWarHammerActivationForTest(
        RelicAggregate agg,
        IEnumerable<string>? upgradedCards,
        IEnumerable<string>? upgradedCardInstanceIds)
    {
        if (agg == null) return;

        agg.Activations++;
        RecordRelicUpgradedCards(agg, upgradedCards);
        AddUniqueWarHammerCardInstanceIds(agg, upgradedCardInstanceIds);
    }

    internal static void RecordWarHammerCombatForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.WarHammerCombats += Math.Max(0, count);
    }

    internal static void RecordWarHammerTurnForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.WarHammerTurns += Math.Max(0, count);
    }

    internal static void RecordWarHammerUpgradedCardPlayForTest(
        RelicAggregate agg,
        int count = 1)
    {
        if (agg == null) return;
        agg.WarHammerUpgradedCardPlays += Math.Max(0, count);
    }

    internal static void RecordEggUpgradedCardOfferedForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.UpgradedCardsOffered += Math.Max(0, count);
    }

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

    internal static void RecordLuckyFyshCardAddedForTest(
        RelicAggregate agg,
        int initialGold,
        int currentGold)
    {
        if (agg == null) return;

        agg.CardsAddedToDeck++;
        agg.GoldGained += Math.Max(0, currentGold - initialGold);
    }

    internal static void RecordBookOfFiveRingsCardAddedForTest(
        RelicAggregate agg,
        int count = 1)
    {
        if (agg == null) return;
        agg.CardsAddedToDeck += Math.Max(0, count);
    }

    internal static void RecordBookOfFiveRingsCardRewardSkippedForTest(
        RelicAggregate agg,
        int count = 1)
    {
        if (agg == null) return;
        agg.CardRewardsSkipped += Math.Max(0, count);
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

    internal static void RecordNutritiousSoupEnchantedStrikePlayedForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null || count <= 0) return;
        agg.NutritiousSoupEnchantedStrikesPlayed += count;
    }

    internal static void RecordUnsettlingLampCombatForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.Activations += Math.Max(0, count);
    }

    internal static void RecordUnsettlingLampDebuffForTest(
        RelicAggregate agg,
        string effectId,
        string displayName,
        decimal amount,
        string? iconPath = null)
    {
        if (agg == null || string.IsNullOrWhiteSpace(effectId) || amount <= 0m) return;
        RecordUnsettlingLampDebuffApplied(agg, effectId, displayName, iconPath, amount);
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

    internal static void SetMiniatureCannonDeckCountsForTest(
        RelicAggregate agg,
        int upgradedAttacksInDeck,
        int nonUpgradedAttacksInDeck = 0)
    {
        if (agg == null) return;
        agg.MiniatureCannonUpgradedAttacksInDeck = Math.Max(0, upgradedAttacksInDeck);
        agg.MiniatureCannonNonUpgradedAttacksInDeck = Math.Max(0, nonUpgradedAttacksInDeck);
    }

    internal static void SetMiniatureCannonCombatCountsForTest(
        RelicAggregate agg,
        IEnumerable<CardModel>? combatCards)
    {
        if (agg == null) return;

        var upgradedAttacks = 0;
        var nonUpgradedAttacks = 0;
        if (combatCards != null)
        {
            foreach (var combatCard in combatCards)
            {
                if (IsMiniatureCannonUpgradedAttackCard(combatCard))
                    upgradedAttacks++;
                else if (IsMiniatureCannonNonUpgradedAttackCard(combatCard))
                    nonUpgradedAttacks++;
            }
        }

        agg.MiniatureCannonUpgradedAttacksInCombat = upgradedAttacks;
        agg.MiniatureCannonNonUpgradedAttacksInCombat = nonUpgradedAttacks;
    }

    internal static void RecordVajraAttackPlayedForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.VajraAttacksPlayed += Math.Max(0, count);
    }

    internal static void RecordVajraAttackHitForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.VajraAttackHits += Math.Max(0, count);
    }

    internal static void RecordEmberTeaAttackPlayedForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.EmberTeaAttacksPlayedWhileActive += Math.Max(0, count);
    }

    internal static void RecordEmberTeaAttackHitForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.EmberTeaHitsWhileActive += Math.Max(0, count);
    }

    internal static void RecordEmberTeaActiveTurnForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.EmberTeaActiveTurns += Math.Max(0, count);
    }

    internal static void RecordEmberTeaActiveCombatForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.EmberTeaActiveCombats += Math.Max(0, count);
    }

    internal static void RecordToastyMittensForTest(
        RelicAggregate agg,
        int cardsExhausted,
        decimal strengthAdded,
        int combats)
    {
        if (agg == null) return;
        agg.ToastyMittensCardsExhausted += Math.Max(0, cardsExhausted);
        agg.StrengthAdded += Math.Max(0m, strengthAdded);
        agg.ToastyMittensCombats += Math.Max(0, combats);
    }

    /// <summary>
    /// Open an async-flow-local window around Toasty Mittens' own hand-draw
    /// callback. Nested shuffle/exhaust hooks inherit the window, while the
    /// caller's context is restored as soon as the callback returns its Task.
    /// </summary>
    internal static PendingToastyMittensActivation? BeginToastyMittensActivation(
        ToastyMittens relic,
        Player player)
    {
        if (relic?.Owner == null || player == null) return null;

        lock (_lock)
        {
            try
            {
                if (_pendingCombat == null) return null;
                if (!ReferenceEquals(relic.Owner, player)) return null;
                if (!IsTrackedRelic(relic) || !IsTrackedPlayer(player)) return null;

                var frame = new PendingToastyMittensActivation(
                    relic,
                    player,
                    _toastyMittensActivation.Value);
                _toastyMittensActivation.Value = frame;
                return frame;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"BeginToastyMittensActivation failed: {e.Message}");
                return null;
            }
        }
    }

    internal static void RestoreToastyMittensActivation(
        PendingToastyMittensActivation? frame)
    {
        if (frame != null && ReferenceEquals(_toastyMittensActivation.Value, frame))
            _toastyMittensActivation.Value = frame.Previous;
    }

    internal static void CompleteToastyMittensActivation(
        PendingToastyMittensActivation? frame)
    {
        if (frame == null) return;

        lock (_lock)
        {
            try
            {
                if (_pendingCombat == null
                    || !IsTrackedRelic(frame.Relic)
                    || !IsTrackedPlayer(frame.Player))
                {
                    return;
                }

                var agg = GetOrCreatePendingRelicAggregateLocked(ToastyMittensRelicId);
                RecordToastyMittensForTest(
                    agg,
                    cardsExhausted: frame.CardExhausted ? 1 : 0,
                    strengthAdded: frame.LastStrengthReceived ?? 0m,
                    combats: 0);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"CompleteToastyMittensActivation failed: {e.Message}");
            }
        }
    }

    private static void ObserveToastyMittensCardExhaustedLocked(CardModel exhaustedCard)
    {
        var frame = _toastyMittensActivation.Value;
        if (frame == null
            || frame.CardExhausted
            || exhaustedCard == null
            || !ReferenceEquals(exhaustedCard.Owner, frame.Player))
        {
            return;
        }

        frame.CardExhausted = true;
    }

    private static void RecordToastyMittensStrengthReceivedLocked(
        PowerModel power,
        Creature target,
        Creature? applier,
        decimal amount)
    {
        var frame = _toastyMittensActivation.Value;
        if (frame == null
            || power is not StrengthPower
            || !ReferenceEquals(target, frame.Player.Creature)
            || !ReferenceEquals(applier, frame.Player.Creature))
        {
            return;
        }

        // Toasty Mittens applies its own Strength as the callback's final
        // operation. Keep the last matching observed amount, then commit it
        // only if the full callback completes successfully.
        frame.LastStrengthReceived = Math.Max(0m, amount);
    }

    /// <summary>
    /// Mark Ember Tea active for the current combat after its room-entry
    /// callback successfully consumes a charge.
    /// </summary>
    public static void RecordEmberTeaCombatActivated(EmberTea relic)
    {
        if (relic?.Owner == null) return;

        lock (_lock)
        {
            try
            {
                if (_pendingCombat == null) return;
                if (!IsTrackedRelic(relic) || !IsTrackedPlayer(relic.Owner)) return;
                if (!_pendingCombat.EmberTeaActivePlayers.Add(relic.Owner)) return;

                var agg = GetOrCreatePendingRelicAggregateLocked(EmberTeaRelicId);
                RecordEmberTeaActiveCombatForTest(agg);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordEmberTeaCombatActivated failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Count one distinct turn in a combat where Ember Tea consumed a charge.
    /// </summary>
    public static void RecordEmberTeaActiveTurnStarted(Player player)
    {
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(player)) return;
                RecordEmberTeaActiveTurnForPlayerLocked(player);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordEmberTeaActiveTurnStarted failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record a Kunai-owned attack play and return whether that play is expected
    /// to activate the relic. The postfix observes the actual Dexterity delta.
    /// </summary>
    public static bool RecordKunaiAttackPlayedAndShouldObserveActivation(
        Kunai relic,
        CardPlay cardPlay,
        out Creature? ownerCreature,
        out int dexterityBefore)
    {
        ownerCreature = null;
        dexterityBefore = 0;

        if (relic?.Owner == null || cardPlay?.Card == null) return false;
        if (cardPlay.Card.Type != CardType.Attack) return false;

        var owner = relic.Owner;
        if (cardPlay.Card.Owner != null && !ReferenceEquals(cardPlay.Card.Owner, owner)) return false;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic)) return false;
                if (!IsTrackedPlayer(owner)) return false;

                _pendingCombat ??= new PendingCombat();
                var agg = GetOrCreatePendingRelicAggregateLocked(KunaiRelicId);
                RecordKunaiAttackPlayedForTest(agg);

                var cardsPerActivation = KunaiCardsPerActivation(relic);
                if (cardsPerActivation <= 0) return false;

                var chargeBefore = KunaiCharge(relic);
                if (chargeBefore + 1 < cardsPerActivation) return false;

                ownerCreature = owner.Creature;
                if (ownerCreature == null) return false;
                dexterityBefore = CurrentDexterity(ownerCreature);
                return true;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordKunaiAttackPlayedAndShouldObserveActivation failed: {e.Message}");
                ownerCreature = null;
                dexterityBefore = 0;
                return false;
            }
        }
    }

    public static void RecordKunaiActivation(int dexterityGained)
    {
        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateForCurrentContextLocked(KunaiRelicId);
                RecordKunaiActivationForTest(agg, dexterityGained);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordKunaiActivation failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Snapshot Kunai's live attack counter at the end of each tracked player
    /// turn before the relic resets it at the next player turn start.
    /// </summary>
    public static void RecordKunaiTurnEnded(IEnumerable<Creature>? participants)
    {
        if (participants == null) return;

        lock (_lock)
        {
            try
            {
                _pendingCombat ??= new PendingCombat();

                foreach (var creature in participants)
                {
                    var player = creature?.Player;
                    if (player == null || !IsTrackedPlayer(player)) continue;
                    if (!TryGetKunai(player, out var kunai) || kunai == null) continue;

                    var turnNumber = player.PlayerCombatState?.TurnNumber ?? 0;
                    if (turnNumber <= 0) continue;
                    if (_pendingCombat.KunaiTurnEndChargeRecordedTurns.TryGetValue(player, out var recordedTurn)
                        && recordedTurn == turnNumber)
                    {
                        continue;
                    }

                    _pendingCombat.KunaiTurnEndChargeRecordedTurns[player] = turnNumber;
                    var agg = GetOrCreatePendingRelicAggregateLocked(KunaiRelicId);
                    RecordKunaiTurnEndChargeForTest(agg, KunaiCharge(kunai));
                }
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordKunaiTurnEnded failed: {e.Message}");
            }
        }
    }

    internal static void RecordKunaiAttackPlayedForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.KunaiAttacksPlayed += Math.Max(0, count);
    }

    internal static void RecordKunaiActivationForTest(RelicAggregate agg, int dexterityGained)
    {
        if (agg == null) return;
        agg.Activations += 1;
        agg.KunaiDexterityGained += Math.Max(0, dexterityGained);
    }

    internal static void RecordKunaiTurnEndChargeForTest(RelicAggregate agg, int charge)
    {
        if (agg == null || charge < 0) return;

        charge %= 3;
        agg.KunaiTurnEndChargeTotal += charge;
        agg.KunaiTurnEndChargeCount += 1;
        if (charge == 1)
            agg.KunaiTurnsEndedAt1Charge += 1;
        else if (charge == 2)
            agg.KunaiTurnsEndedAt2Charges += 1;
    }

    /// <summary>
    /// Record a Kusarigama-owned Attack and arm the exact damage command when
    /// this play reaches the repeatable three-Attack threshold.
    /// </summary>
    public static bool RecordKusarigamaAttackPlayedAndShouldArmDamageAttribution(
        Kusarigama relic,
        CardPlay cardPlay,
        out Creature? dealer)
    {
        dealer = null;
        if (relic?.Owner == null || cardPlay?.Card == null) return false;
        if (cardPlay.Card.Type != CardType.Attack) return false;
        if (cardPlay.Card.Owner != null && !ReferenceEquals(cardPlay.Card.Owner, relic.Owner)) return false;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic) || !IsTrackedPlayer(relic.Owner)) return false;
                if (CombatManager.Instance?.IsInProgress != true) return false;

                _pendingCombat ??= new PendingCombat();
                var agg = GetOrCreatePendingRelicAggregateLocked(KusarigamaRelicId);
                RecordKusarigamaAttackPlayedForTest(agg);

                var threshold = KusarigamaCardsPerActivation(relic);
                if (threshold <= 0 || KusarigamaCharge(relic) + 1 < threshold) return false;

                dealer = relic.Owner.Creature;
                if (dealer?.CombatState?.HittableEnemies?.Any() != true)
                {
                    dealer = null;
                    return false;
                }

                RecordKusarigamaActivationForTest(agg);
                _pendingKusarigamaDamageAttributions.Add(dealer);
                return true;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordKusarigamaAttackPlayedAndShouldArmDamageAttribution failed: {e.Message}");
                dealer = null;
                return false;
            }
        }
    }

    public static bool TryConsumeKusarigamaDamageAttribution(Creature dealer)
    {
        if (dealer == null) return false;

        lock (_lock)
        {
            try
            {
                return ConsumePendingCreatureAttribution(_pendingKusarigamaDamageAttributions, dealer);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"TryConsumeKusarigamaDamageAttribution failed: {e.Message}");
                return false;
            }
        }
    }

    public static void DisarmKusarigamaDamageAttribution(Creature? dealer)
    {
        if (dealer == null) return;
        lock (_lock)
            ConsumePendingCreatureAttribution(_pendingKusarigamaDamageAttributions, dealer);
    }

    public static void RecordKusarigamaDamage(IEnumerable<DamageResult>? results)
    {
        if (results == null) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateForCurrentContextLocked(KusarigamaRelicId);
                AddRelicDamageResultsLocked(agg, results);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordKusarigamaDamage failed: {e.Message}");
            }
        }
    }

    internal static void RecordKusarigamaAttackPlayedForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.KusarigamaAttacksPlayed += Math.Max(0, count);
    }

    internal static void RecordKusarigamaActivationForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.Activations += Math.Max(0, count);
    }

    internal static void RecordKusarigamaDamageForTest(
        RelicAggregate agg,
        IEnumerable<(int BlockedDamage, int UnblockedDamage, int OverkillDamage, bool WasTargetKilled)> results)
    {
        if (agg == null || results == null) return;
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

    internal static void RecordKusarigamaTurnEndChargeForTest(RelicAggregate agg, int charge)
    {
        if (agg == null || charge < 0) return;

        charge %= 3;
        agg.KusarigamaTurnEndChargeTotal += charge;
        agg.KusarigamaTurnEndChargeCount += 1;
        if (charge == 1)
            agg.KusarigamaTurnsEndedAt1Charge += 1;
        else if (charge == 2)
            agg.KusarigamaTurnsEndedAt2Charges += 1;
    }

    /// <summary>
    /// Record an Ornamental Fan-owned Attack and arm its exact block command
    /// when this play reaches the repeatable three-Attack threshold.
    /// </summary>
    public static bool RecordOrnamentalFanAttackPlayedAndShouldArmBlockAttribution(
        OrnamentalFan relic,
        CardPlay cardPlay,
        out Creature? ownerCreature)
    {
        ownerCreature = null;
        if (relic?.Owner == null || cardPlay?.Card == null) return false;
        if (cardPlay.Card.Type != CardType.Attack) return false;
        if (cardPlay.Card.Owner != null && !ReferenceEquals(cardPlay.Card.Owner, relic.Owner)) return false;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic) || !IsTrackedPlayer(relic.Owner)) return false;
                if (CombatManager.Instance?.IsInProgress != true) return false;

                _pendingCombat ??= new PendingCombat();
                var agg = GetOrCreatePendingRelicAggregateLocked(OrnamentalFanRelicId);
                RecordOrnamentalFanAttackPlayedForTest(agg);

                var threshold = OrnamentalFanCardsPerActivation(relic);
                if (threshold <= 0 || OrnamentalFanCharge(relic) + 1 < threshold) return false;

                ownerCreature = relic.Owner.Creature;
                if (ownerCreature == null) return false;

                RecordOrnamentalFanActivationForTest(agg);
                _pendingOrnamentalFanBlockAttributions.Add(ownerCreature);
                return true;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordOrnamentalFanAttackPlayedAndShouldArmBlockAttribution failed: {e.Message}");
                ownerCreature = null;
                return false;
            }
        }
    }

    public static bool TryConsumeOrnamentalFanBlockAttribution(Creature creature)
    {
        if (creature == null) return false;

        lock (_lock)
        {
            try
            {
                return ConsumePendingCreatureAttribution(_pendingOrnamentalFanBlockAttributions, creature);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"TryConsumeOrnamentalFanBlockAttribution failed: {e.Message}");
                return false;
            }
        }
    }

    public static void DisarmOrnamentalFanBlockAttribution(Creature? creature)
    {
        if (creature == null) return;
        lock (_lock)
            ConsumePendingCreatureAttribution(_pendingOrnamentalFanBlockAttributions, creature);
    }

    public static void RecordOrnamentalFanBlockGained(decimal amount)
    {
        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateForCurrentContextLocked(OrnamentalFanRelicId);
                RecordOrnamentalFanBlockGainedForTest(agg, amount);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordOrnamentalFanBlockGained failed: {e.Message}");
            }
        }
    }

    internal static void RecordOrnamentalFanAttackPlayedForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.OrnamentalFanAttacksPlayed += Math.Max(0, count);
    }

    internal static void RecordOrnamentalFanActivationForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.Activations += Math.Max(0, count);
    }

    internal static void RecordOrnamentalFanBlockGainedForTest(RelicAggregate agg, decimal amount)
    {
        if (agg == null || amount <= 0m) return;
        agg.AdditionalBlockGained += (int)amount;
    }

    internal static void RecordOrnamentalFanTurnEndChargeForTest(RelicAggregate agg, int charge)
    {
        if (agg == null || charge < 0) return;

        charge %= 3;
        agg.OrnamentalFanTurnEndChargeTotal += charge;
        agg.OrnamentalFanTurnEndChargeCount += 1;
        if (charge == 0)
            agg.OrnamentalFanTurnsEndedAt0Charges += 1;
        else if (charge == 1)
            agg.OrnamentalFanTurnsEndedAt1Charge += 1;
        else if (charge == 2)
            agg.OrnamentalFanTurnsEndedAt2Charges += 1;
    }

    /// <summary>
    /// Record a Shuriken-owned Attack and snapshot Strength when this play
    /// reaches the repeatable three-Attack threshold.
    /// </summary>
    public static bool RecordShurikenAttackPlayedAndShouldObserveActivation(
        Shuriken relic,
        CardPlay cardPlay,
        out Creature? ownerCreature,
        out decimal strengthBefore)
    {
        ownerCreature = null;
        strengthBefore = 0m;
        if (relic?.Owner == null || cardPlay?.Card == null) return false;
        if (cardPlay.Card.Type != CardType.Attack) return false;
        if (cardPlay.Card.Owner != null && !ReferenceEquals(cardPlay.Card.Owner, relic.Owner)) return false;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic) || !IsTrackedPlayer(relic.Owner)) return false;
                if (CombatManager.Instance?.IsInProgress != true) return false;

                _pendingCombat ??= new PendingCombat();
                var agg = GetOrCreatePendingRelicAggregateLocked(ShurikenRelicId);
                RecordShurikenAttackPlayedForTest(agg);

                var threshold = ShurikenCardsPerActivation(relic);
                if (threshold <= 0 || ShurikenCharge(relic) + 1 < threshold) return false;

                ownerCreature = relic.Owner.Creature;
                if (ownerCreature == null) return false;
                strengthBefore = CurrentStrength(ownerCreature);
                return true;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordShurikenAttackPlayedAndShouldObserveActivation failed: {e.Message}");
                ownerCreature = null;
                strengthBefore = 0m;
                return false;
            }
        }
    }

    public static void RecordShurikenActivation(decimal strengthGained)
    {
        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateForCurrentContextLocked(ShurikenRelicId);
                RecordShurikenActivationForTest(agg, strengthGained);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordShurikenActivation failed: {e.Message}");
            }
        }
    }

    internal static void RecordShurikenAttackPlayedForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.ShurikenAttacksPlayed += Math.Max(0, count);
    }

    internal static void RecordShurikenActivationForTest(RelicAggregate agg, decimal strengthGained)
    {
        if (agg == null) return;
        agg.Activations += 1;
        agg.StrengthAdded += Math.Max(0m, strengthGained);
    }

    internal static void RecordShurikenTurnEndChargeForTest(RelicAggregate agg, int charge)
    {
        if (agg == null || charge < 0) return;

        charge %= 3;
        agg.ShurikenTurnEndChargeTotal += charge;
        agg.ShurikenTurnEndChargeCount += 1;
        if (charge == 1)
            agg.ShurikenTurnsEndedAt1Charge += 1;
        else if (charge == 2)
            agg.ShurikenTurnsEndedAt2Charges += 1;
    }

    /// <summary>
    /// Snapshot the three missing Kunai-style Attack counters at player turn
    /// end before their game models reset them.
    /// </summary>
    public static void RecordUnlimitedAttackChargeRelicsTurnEnded(IEnumerable<Creature>? participants)
    {
        if (participants == null) return;

        lock (_lock)
        {
            try
            {
                _pendingCombat ??= new PendingCombat();

                foreach (var creature in participants)
                {
                    var player = creature?.Player;
                    if (player == null || !IsTrackedPlayer(player)) continue;

                    var turnNumber = player.PlayerCombatState?.TurnNumber ?? 0;
                    if (turnNumber <= 0) continue;

                    RecordKusarigamaTurnEndChargeForPlayerLocked(player, turnNumber);
                    RecordOrnamentalFanTurnEndChargeForPlayerLocked(player, turnNumber);
                    RecordShurikenTurnEndChargeForPlayerLocked(player, turnNumber);
                }
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordUnlimitedAttackChargeRelicsTurnEnded failed: {e.Message}");
            }
        }
    }

    private static void RecordKusarigamaTurnEndChargeForPlayerLocked(Player player, int turnNumber)
    {
        if (_pendingCombat == null || !TryGetKusarigama(player, out var relic) || relic == null) return;
        if (_pendingCombat.KusarigamaTurnEndChargeRecordedTurns.TryGetValue(player, out var recordedTurn)
            && recordedTurn == turnNumber)
        {
            return;
        }

        _pendingCombat.KusarigamaTurnEndChargeRecordedTurns[player] = turnNumber;
        var agg = GetOrCreatePendingRelicAggregateLocked(KusarigamaRelicId);
        RecordKusarigamaTurnEndChargeForTest(agg, KusarigamaCharge(relic));
    }

    private static void RecordOrnamentalFanTurnEndChargeForPlayerLocked(Player player, int turnNumber)
    {
        if (_pendingCombat == null || !TryGetOrnamentalFan(player, out var relic) || relic == null) return;
        if (_pendingCombat.OrnamentalFanTurnEndChargeRecordedTurns.TryGetValue(player, out var recordedTurn)
            && recordedTurn == turnNumber)
        {
            return;
        }

        _pendingCombat.OrnamentalFanTurnEndChargeRecordedTurns[player] = turnNumber;
        var agg = GetOrCreatePendingRelicAggregateLocked(OrnamentalFanRelicId);
        RecordOrnamentalFanTurnEndChargeForTest(agg, OrnamentalFanCharge(relic));
    }

    private static void RecordShurikenTurnEndChargeForPlayerLocked(Player player, int turnNumber)
    {
        if (_pendingCombat == null || !TryGetShuriken(player, out var relic) || relic == null) return;
        if (_pendingCombat.ShurikenTurnEndChargeRecordedTurns.TryGetValue(player, out var recordedTurn)
            && recordedTurn == turnNumber)
        {
            return;
        }

        _pendingCombat.ShurikenTurnEndChargeRecordedTurns[player] = turnNumber;
        var agg = GetOrCreatePendingRelicAggregateLocked(ShurikenRelicId);
        RecordShurikenTurnEndChargeForTest(agg, ShurikenCharge(relic));
    }

    public static void RecordPaperPhrogVulnerableBonus(
        PaperPhrog relic,
        decimal damageAdded,
        int enhancedAttacks = 1)
    {
        if (relic?.Owner == null || damageAdded <= 0m || enhancedAttacks <= 0) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic)) return;
                if (!IsTrackedPlayer(relic.Owner)) return;

                _pendingCombat ??= new PendingCombat();
                RecordPaperPhrogCombatForPlayerLocked(relic.Owner);
                RecordPaperPhrogTurnForPlayerLocked(relic.Owner);

                var agg = GetOrCreatePendingRelicAggregateLocked(PaperPhrogRelicId);
                RecordPaperPhrogVulnerableBonusForTest(agg, damageAdded, enhancedAttacks);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordPaperPhrogVulnerableBonus failed: {e.Message}");
            }
        }
    }

    public static void RecordPaperPhrogTurnStarted(Player player)
    {
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(player)) return;
                _pendingCombat ??= new PendingCombat();
                RecordPaperPhrogTurnForPlayerLocked(player);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordPaperPhrogTurnStarted failed: {e.Message}");
            }
        }
    }

    public static void RecordRegaliteCardCreatedAndArmBlockAttribution(Regalite relic, Player creator)
    {
        if (relic?.Owner == null || creator == null) return;
        if (!ReferenceEquals(relic.Owner, creator)) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic)) return;
                if (!IsTrackedPlayer(creator)) return;

                _pendingCombat ??= new PendingCombat();
                RecordRegaliteCombatForPlayerLocked(creator);
                RecordRegaliteTurnForPlayerLocked(creator);

                var agg = GetOrCreatePendingRelicAggregateLocked(RegaliteRelicId);
                RecordRegaliteCardCreatedForTest(agg);
                _pendingCombat.Windows.Arm(
                    RegaliteRelicId,
                    AttributionEventKind.PlayerBlockGain,
                    CurrentHistoryCountLocked(),
                    maxHistoryAdvance: -1);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordRegaliteCardCreatedAndArmBlockAttribution failed: {e.Message}");
            }
        }
    }

    public static void RecordRegaliteTurnStarted(Player player)
    {
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(player)) return;
                _pendingCombat ??= new PendingCombat();
                RecordRegaliteTurnForPlayerLocked(player);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordRegaliteTurnStarted failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Count one owner card play that meets Intimidating Helmet's exact
    /// play-time EnergyValue threshold, then arm the relic's immediately
    /// following BlockVar gain-block command for observed-result attribution.
    /// </summary>
    public static bool RecordIntimidatingHelmetCardPlayedAndArmBlockAttribution(
        IntimidatingHelmet relic,
        CardPlay cardPlay,
        out Creature? ownerCreature)
    {
        ownerCreature = null;
        if (relic?.Owner == null || cardPlay?.Card == null) return false;
        if (!ReferenceEquals(cardPlay.Card.Owner, relic.Owner)) return false;
        if (!IntimidatingHelmetEnergyValueQualifiesForTest(
                cardPlay.Resources.EnergyValue,
                relic.DynamicVars.Energy.IntValue))
        {
            return false;
        }

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic) || !IsTrackedPlayer(relic.Owner)) return false;
                if (CombatManager.Instance?.IsInProgress != true) return false;

                ownerCreature = relic.Owner.Creature;
                if (ownerCreature == null) return false;

                _pendingCombat ??= new PendingCombat();
                RecordIntimidatingHelmetCombatForPlayerLocked(relic.Owner);
                RecordIntimidatingHelmetTurnForPlayerLocked(relic.Owner);

                var agg = GetOrCreatePendingRelicAggregateLocked(IntimidatingHelmetRelicId);
                RecordIntimidatingHelmetActivationForTest(agg);
                _pendingIntimidatingHelmetBlockAttributions.Add(ownerCreature);
                return true;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordIntimidatingHelmetCardPlayedAndArmBlockAttribution failed: {e.Message}");
                ownerCreature = null;
                return false;
            }
        }
    }

    public static bool TryConsumeIntimidatingHelmetBlockAttribution(Creature creature)
    {
        if (creature == null) return false;

        lock (_lock)
        {
            try
            {
                return ConsumePendingCreatureAttribution(
                    _pendingIntimidatingHelmetBlockAttributions,
                    creature);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"TryConsumeIntimidatingHelmetBlockAttribution failed: {e.Message}");
                return false;
            }
        }
    }

    public static void DisarmIntimidatingHelmetBlockAttribution(Creature? creature)
    {
        if (creature == null) return;
        lock (_lock)
            ConsumePendingCreatureAttribution(_pendingIntimidatingHelmetBlockAttributions, creature);
    }

    public static void RecordIntimidatingHelmetBlockGained(decimal amount)
    {
        if (amount <= 0m) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateForCurrentContextLocked(IntimidatingHelmetRelicId);
                RecordIntimidatingHelmetBlockGainedForTest(agg, amount);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordIntimidatingHelmetBlockGained failed: {e.Message}");
            }
        }
    }

    public static void RecordIntimidatingHelmetTurnStarted(Player player)
    {
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(player)) return;
                _pendingCombat ??= new PendingCombat();
                RecordIntimidatingHelmetTurnForPlayerLocked(player);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordIntimidatingHelmetTurnStarted failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Arm Daughter of the Wind's immediately following block command for an
    /// owner Attack. The command result, not the relic's printed BlockVar, is
    /// the amount credited.
    /// </summary>
    public static bool ArmDaughterOfTheWindBlockAttribution(
        DaughterOfTheWind relic,
        CardPlay cardPlay,
        out Creature? ownerCreature)
    {
        ownerCreature = null;
        if (relic?.Owner == null || cardPlay?.Card == null) return false;
        if (!ReferenceEquals(cardPlay.Card.Owner, relic.Owner)) return false;
        if (cardPlay.Card.Type != CardType.Attack) return false;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic) || !IsTrackedPlayer(relic.Owner)) return false;
                if (CombatManager.Instance?.IsInProgress != true) return false;

                ownerCreature = relic.Owner.Creature;
                if (ownerCreature == null) return false;

                _pendingCombat ??= new PendingCombat();
                RecordDaughterOfTheWindCombatForPlayerLocked(relic.Owner);
                RecordDaughterOfTheWindTurnForPlayerLocked(relic.Owner);
                _pendingDaughterOfTheWindBlockAttributions.Add(ownerCreature);
                return true;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"ArmDaughterOfTheWindBlockAttribution failed: {e.Message}");
                ownerCreature = null;
                return false;
            }
        }
    }

    public static bool TryConsumeDaughterOfTheWindBlockAttribution(Creature creature)
    {
        if (creature == null) return false;

        lock (_lock)
        {
            try
            {
                return ConsumePendingCreatureAttribution(
                    _pendingDaughterOfTheWindBlockAttributions,
                    creature);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"TryConsumeDaughterOfTheWindBlockAttribution failed: {e.Message}");
                return false;
            }
        }
    }

    public static void DisarmDaughterOfTheWindBlockAttribution(Creature? creature)
    {
        if (creature == null) return;
        lock (_lock)
            ConsumePendingCreatureAttribution(_pendingDaughterOfTheWindBlockAttributions, creature);
    }

    public static void RecordDaughterOfTheWindBlockGained(decimal amount)
    {
        if (amount <= 0m) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateForCurrentContextLocked(DaughterOfTheWindRelicId);
                RecordDaughterOfTheWindBlockGainedForTest(agg, amount);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordDaughterOfTheWindBlockGained failed: {e.Message}");
            }
        }
    }

    public static void RecordDaughterOfTheWindTurnStarted(Player player)
    {
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(player)) return;
                _pendingCombat ??= new PendingCombat();
                RecordDaughterOfTheWindTurnForPlayerLocked(player);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordDaughterOfTheWindTurnStarted failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record Mummified Hand from the relic's completed AfterCardPlayed call.
    /// The selected card and its before/after effective energy costs are
    /// captured around the exact SetToFreeThisTurn invocation made by the
    /// relic, rather than inferred from later cost queries.
    /// </summary>
    public static void RecordMummifiedHandTrigger(
        MummifiedHand relic,
        CardPlay cardPlay,
        CardModel? discountedCard,
        decimal discountedCardCostBefore,
        decimal discountedCardCostAfter)
    {
        if (relic?.Owner == null || cardPlay?.Card == null) return;
        if (!ReferenceEquals(cardPlay.Card.Owner, relic.Owner)) return;
        if (cardPlay.Card.Type != CardType.Power) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic) || !IsTrackedPlayer(relic.Owner)) return;
                if (CombatManager.Instance?.IsInProgress != true) return;

                _pendingCombat ??= new PendingCombat();
                RecordMummifiedHandCombatForPlayerLocked(relic.Owner);
                RecordMummifiedHandTurnForPlayerLocked(relic.Owner);

                var agg = GetOrCreatePendingRelicAggregateLocked(MummifiedHandRelicId);
                RecordMummifiedHandTriggerForTest(
                    agg,
                    triggeringPowerCost: cardPlay.Resources.EnergyValue,
                    triggeringPowerEnergySpent: cardPlay.Resources.EnergySpent,
                    discountedCardCostBefore,
                    discountedCardCostAfter,
                    discountedCard?.Type,
                    discountedCard?.Rarity);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordMummifiedHandTrigger failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Record one Sturdy Clamp retention boundary after the relic's async
    /// callback has finished. The starting value is used for excess-over-cap;
    /// the resulting value is the block the player actually retained.
    /// </summary>
    public static void RecordSturdyClampRetention(
        SturdyClamp relic,
        Creature creature,
        int startingBlock,
        int resultingBlock)
    {
        if (relic?.Owner == null || creature?.Player == null) return;
        if (!ReferenceEquals(relic.Owner.Creature, creature)) return;
        if (!ReferenceEquals(relic.Owner, creature.Player)) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic) || !IsTrackedPlayer(relic.Owner)) return;
                if (CombatManager.Instance?.IsInProgress != true) return;

                _pendingCombat ??= new PendingCombat();
                RecordSturdyClampCombatForPlayerLocked(relic.Owner);

                var turnNumber = relic.Owner.PlayerCombatState?.TurnNumber ?? 0;
                if (turnNumber <= 0) return;
                if (_pendingCombat.SturdyClampTurnCountedTurns.TryGetValue(
                        relic.Owner,
                        out var recordedTurn)
                    && recordedTurn == turnNumber)
                {
                    return;
                }

                _pendingCombat.SturdyClampTurnCountedTurns[relic.Owner] = turnNumber;
                var agg = GetOrCreatePendingRelicAggregateLocked(SturdyClampRelicId);
                RecordSturdyClampTurnForTest(agg, startingBlock, resultingBlock);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordSturdyClampRetention failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Stage Ruined Helmet's local contribution while its receiver-side
    /// modifier still exposes both the requested and doubled amounts.
    /// Completion is deferred until the game confirms the power application.
    /// </summary>
    public static void StageRuinedHelmetStrengthGain(
        RuinedHelmet relic,
        PowerModel canonicalPower,
        Creature target,
        decimal requestedAmount,
        decimal modifiedAmount)
    {
        if (relic?.Owner == null || canonicalPower is not StrengthPower || target == null) return;
        if (!ReferenceEquals(relic.Owner.Creature, target)) return;

        var strengthAdded = modifiedAmount - requestedAmount;
        if (strengthAdded <= 0m) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic) || !IsTrackedPlayer(relic.Owner)) return;
                if (CombatManager.Instance?.IsInProgress != true) return;

                _pendingCombat ??= new PendingCombat();
                RecordRuinedHelmetCombatForPlayerLocked(relic.Owner);
                _pendingCombat.PendingRuinedHelmetStrengthGains[relic] = strengthAdded;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"StageRuinedHelmetStrengthGain failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Commit the staged Ruined Helmet bonus from the relic's own
    /// AfterModifyingPowerAmountReceived callback. PowerCmd invokes this only
    /// after the Strength application succeeds.
    /// </summary>
    public static void CompleteRuinedHelmetStrengthGain(RuinedHelmet relic, PowerModel power)
    {
        if (relic == null || power is not StrengthPower) return;

        lock (_lock)
        {
            try
            {
                if (_pendingCombat == null) return;
                if (!_pendingCombat.PendingRuinedHelmetStrengthGains.Remove(
                        relic,
                        out var strengthAdded))
                {
                    return;
                }

                if (!IsTrackedRelic(relic) || !IsTrackedPlayer(relic.Owner)) return;
                if (CombatManager.Instance?.IsInProgress != true) return;

                var agg = GetOrCreatePendingRelicAggregateLocked(RuinedHelmetRelicId);
                RecordRuinedHelmetStrengthGainForTest(agg, strengthAdded);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"CompleteRuinedHelmetStrengthGain failed: {e.Message}");
            }
        }
    }

    public static void RecordMummifiedHandTurnStarted(Player player)
    {
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(player)) return;
                _pendingCombat ??= new PendingCombat();
                RecordMummifiedHandTurnForPlayerLocked(player);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordMummifiedHandTurnStarted failed: {e.Message}");
            }
        }
    }

    internal static void RecordPaperPhrogVulnerableBonusForTest(
        RelicAggregate agg,
        decimal damageAdded,
        int enhancedAttacks = 1)
    {
        if (agg == null) return;
        if (damageAdded <= 0m || enhancedAttacks <= 0) return;
        agg.PaperPhrogDamageAdded += damageAdded;
        agg.PaperPhrogEnhancedAttacks += Math.Max(0, enhancedAttacks);
    }

    internal static void RecordPaperPhrogCombatForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.PaperPhrogCombats += Math.Max(0, count);
    }

    internal static void RecordPaperPhrogTurnForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.PaperPhrogTurns += Math.Max(0, count);
    }

    internal static void RecordRegaliteCardCreatedForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.RegaliteCardsCreated += Math.Max(0, count);
    }

    internal static void RecordRegaliteCombatForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.RegaliteCombats += Math.Max(0, count);
    }

    internal static void RecordRegaliteTurnForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.RegaliteTurns += Math.Max(0, count);
    }

    internal static bool IntimidatingHelmetEnergyValueQualifiesForTest(int energyValue, int threshold = 2)
        => energyValue >= threshold;

    internal static void RecordIntimidatingHelmetActivationForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.Activations += Math.Max(0, count);
    }

    internal static void RecordIntimidatingHelmetBlockGainedForTest(RelicAggregate agg, decimal amount)
    {
        if (agg == null || amount <= 0m) return;
        agg.AdditionalBlockGained += (int)amount;
    }

    internal static void RecordIntimidatingHelmetCombatForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.IntimidatingHelmetCombats += Math.Max(0, count);
    }

    internal static void RecordIntimidatingHelmetTurnForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.IntimidatingHelmetTurns += Math.Max(0, count);
    }

    internal static void RecordDaughterOfTheWindBlockGainedForTest(
        RelicAggregate agg,
        decimal amount)
    {
        if (agg == null || amount <= 0m) return;
        agg.AdditionalBlockGained += (int)amount;
    }

    internal static void RecordDaughterOfTheWindCombatForTest(
        RelicAggregate agg,
        int count = 1)
    {
        if (agg == null) return;
        agg.DaughterOfTheWindCombats += Math.Max(0, count);
    }

    internal static void RecordDaughterOfTheWindTurnForTest(
        RelicAggregate agg,
        int count = 1)
    {
        if (agg == null) return;
        agg.DaughterOfTheWindTurns += Math.Max(0, count);
    }

    internal static void RecordMummifiedHandTriggerForTest(
        RelicAggregate agg,
        int triggeringPowerCost,
        int triggeringPowerEnergySpent,
        decimal discountedCardCostBefore,
        decimal discountedCardCostAfter,
        CardType? discountedCardType,
        CardRarity? discountedCardRarity = null)
    {
        if (agg == null) return;

        var powerCost = Math.Max(0, triggeringPowerCost);
        var energySpent = Math.Max(0, triggeringPowerEnergySpent);
        var costBefore = Math.Max(0m, discountedCardCostBefore);
        var costAfter = Math.Max(0m, discountedCardCostAfter);

        agg.Activations += 1;
        agg.MummifiedHandTriggeringPowerCostTotal += powerCost;
        agg.MummifiedHandDiscountGivenTotal += Math.Max(0m, costBefore - costAfter);

        if (costBefore > 0m)
        {
            agg.MummifiedHandEnergySpentToDiscountedCostRatioTotal += energySpent / costBefore;
            agg.MummifiedHandEnergySpentToDiscountedCostRatioCount += 1;
        }

        switch (discountedCardType)
        {
            case CardType.Power:
                agg.MummifiedHandDiscountedPowers += 1;
                break;
            case CardType.Attack:
                agg.MummifiedHandDiscountedAttacks += 1;
                break;
            case CardType.Skill:
                agg.MummifiedHandDiscountedSkills += 1;
                break;
        }

        switch (discountedCardRarity)
        {
            case CardRarity.Common:
                agg.MummifiedHandDiscountedCommons += 1;
                break;
            case CardRarity.Uncommon:
                agg.MummifiedHandDiscountedUncommons += 1;
                break;
            case CardRarity.Rare:
                agg.MummifiedHandDiscountedRares += 1;
                break;
        }
    }

    internal static void RecordSturdyClampTurnForTest(
        RelicAggregate agg,
        int startingBlock,
        int resultingBlock)
    {
        if (agg == null) return;

        agg.SturdyClampTurns += 1;
        agg.SturdyClampBlockRetained += Math.Max(0, resultingBlock);
        agg.SturdyClampExcessBlockOverTen += Math.Max(0, startingBlock - 10);
    }

    internal static void RecordSturdyClampCombatForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.SturdyClampCombats += Math.Max(0, count);
    }

    internal static void RecordRuinedHelmetStrengthGainForTest(
        RelicAggregate agg,
        decimal strengthGained)
    {
        if (agg == null || strengthGained <= 0m) return;
        agg.Activations += 1;
        agg.StrengthAdded += strengthGained;
    }

    internal static void RecordRuinedHelmetCombatForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.RuinedHelmetCombats += Math.Max(0, count);
    }

    internal static void RecordMummifiedHandCombatForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.MummifiedHandCombats += Math.Max(0, count);
    }

    internal static void RecordMummifiedHandTurnForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.MummifiedHandTurns += Math.Max(0, count);
    }

    /// <summary>
    /// Persist the permanent-deck cards whose Sharp amount changed across
    /// Gnarled Hammer's completed pickup callback.
    /// </summary>
    public static void RecordGnarledHammerSharpCards(
        GnarledHammer relic,
        IEnumerable<CardModel> cards)
    {
        if (relic?.Owner == null || cards == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic) || !IsTrackedPlayer(relic.Owner)) return;

                var cardNames = cards
                    .Where(card =>
                        card != null
                        && ReferenceEquals(card.Owner, relic.Owner)
                        && card.Pile?.Type == PileType.Deck
                        && card.Enchantment is Sharp)
                    .Select(GetCardDisplayNameForStats)
                    .Where(cardName => !string.IsNullOrWhiteSpace(cardName))
                    .ToList();
                if (cardNames.Count == 0) return;

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(GnarledHammerRelicId);
                RecordGnarledHammerSharpCardsForTest(agg, cardNames);
                RefreshCurrentRunMetadataLocked();
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordGnarledHammerSharpCards failed: {e.Message}");
            }
        }
    }

    internal static void RecordGnarledHammerSharpCardsForTest(
        RelicAggregate agg,
        IEnumerable<string>? cards)
    {
        if (agg == null || cards == null) return;

        agg.SharpEnchantedCards ??= new List<string>();
        agg.SharpEnchantedCards.AddRange(
            cards.Where(card => !string.IsNullOrWhiteSpace(card)));
    }

    /// <summary>
    /// Persist the exact permanent-deck cards whose Instinct amount changed
    /// across Tri-Boomerang's completed pickup callback.
    /// </summary>
    public static void RecordTriBoomerangInstinctCards(
        TriBoomerang relic,
        IEnumerable<CardModel> cards)
    {
        if (relic?.Owner == null || cards == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic) || !IsTrackedPlayer(relic.Owner)) return;

                var observedCards = cards
                    .Where(card =>
                        card != null
                        && ReferenceEquals(card.Owner, relic.Owner)
                        && card.Pile?.Type == PileType.Deck
                        && card.Enchantment is Instinct)
                    .Select(card => new RelicEnchantedCardAggregate
                    {
                        CardInstanceId = GetOrAssignInstanceId(card),
                        DisplayName = GetCardDisplayNameForStats(card),
                    })
                    .ToList();
                if (observedCards.Count == 0) return;

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(
                    TriBoomerangRelicId);
                RecordTriBoomerangInstinctCardsForTest(agg, observedCards);
                RefreshCurrentRunMetadataLocked();
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug(
                    $"RecordTriBoomerangInstinctCards failed: {e.Message}");
            }
        }
    }

    internal static void RecordTriBoomerangInstinctCardsForTest(
        RelicAggregate agg,
        IEnumerable<RelicEnchantedCardAggregate>? cards)
    {
        if (agg == null || cards == null) return;
        AddUniqueTriBoomerangInstinctCards(agg, cards);
    }

    internal static void RecordTriBoomerangInstinctCardPlayForTest(
        RelicAggregate agg,
        int count = 1)
    {
        if (agg == null) return;
        agg.TriBoomerangInstinctCardPlays += Math.Max(0, count);
    }

    internal static void RecordTriBoomerangCombatForTest(
        RelicAggregate agg,
        int count = 1)
    {
        if (agg == null) return;
        agg.TriBoomerangCombats += Math.Max(0, count);
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

    internal static void RecordLargeCapsuleRelicObtainedForTest(
        RelicAggregate agg,
        string? relicId,
        string? displayName)
    {
        if (agg == null || string.IsNullOrWhiteSpace(relicId)) return;
        AddRelicGranted(agg.RelicsGranted, relicId, displayName ?? "", 1);
    }

    internal static void RecordPaelsWingArtifactGainedForTest(
        RelicAggregate agg,
        string? relicId,
        string? displayName)
    {
        if (agg == null || string.IsNullOrWhiteSpace(relicId)) return;
        AddRelicGranted(agg.RelicsGranted, relicId, displayName ?? "", 1);
    }

    internal static void RecordPaelsToothCardReturnedForTest(
        RelicAggregate agg,
        string? cardId,
        string? displayName,
        int upgradeLevel)
    {
        if (agg == null || string.IsNullOrWhiteSpace(cardId)) return;

        agg.CardsReturned ??= new List<RelicCardReturnAggregate>();
        var normalizedUpgradeLevel = Math.Max(0, upgradeLevel);
        var normalizedDisplayName = displayName ?? "";
        if (string.IsNullOrWhiteSpace(normalizedDisplayName))
        {
            normalizedDisplayName = FormatCardIdForDisplay(cardId);
            if (normalizedUpgradeLevel > 0)
                normalizedDisplayName += new string('+', normalizedUpgradeLevel);
        }

        agg.CardsReturned.Add(new RelicCardReturnAggregate
        {
            CardId = cardId,
            DisplayName = normalizedDisplayName,
            UpgradeLevel = normalizedUpgradeLevel,
        });
    }

    internal static void RecordNeowsBonesRelicObtainedForTest(
        RelicAggregate agg,
        string? relicId,
        string? displayName)
    {
        if (agg == null || string.IsNullOrWhiteSpace(relicId)) return;
        AddRelicGranted(agg.RelicsGranted, relicId, displayName ?? "", 1);
    }

    internal static void RecordNeowsBonesCurseGrantedForTest(
        RelicAggregate agg,
        string? cardId,
        string? displayName)
    {
        if (agg == null || string.IsNullOrWhiteSpace(cardId)) return;
        AddRelicCardGranted(agg.CardsGranted, cardId, displayName ?? "", 1);
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

    internal static bool IsLizardTailStatsRelic(RelicModel? relic)
    {
        try
        {
            return relic is LizardTail
                || string.Equals(
                    relic?.GetType().FullName,
                    "MegaCrit.Sts2.Core.Models.Relics.LizardTail",
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
        bool allowZeroAttempt = false,
        Action<RelicAggregate>? configureAggregate = null)
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
                configureAggregate?.Invoke(agg);
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
    /// Count Art of War's owner energy-reset boundary as one held player turn.
    /// The patch snapshots energy only when this returns true.
    /// </summary>
    public static bool RecordArtOfWarTurnStarted(ArtOfWar relic, Player player)
    {
        if (relic?.Owner == null || player == null) return false;
        if (!ReferenceEquals(relic.Owner, player)) return false;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic) || !IsTrackedPlayer(player)) return false;
                if (CombatManager.Instance?.IsInProgress != true) return false;
                if ((player.PlayerCombatState?.TurnNumber ?? 0) <= 0) return false;

                _pendingCombat ??= new PendingCombat();
                RecordArtOfWarCombatForPlayerLocked(player);
                RecordArtOfWarTurnForPlayerLocked(player);
                return true;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordArtOfWarTurnStarted failed: {e.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Record the positive energy-pool delta observed across Art of War's own
    /// successfully completed AfterEnergyReset callback.
    /// </summary>
    public static void RecordArtOfWarEnergyGain(
        ArtOfWar relic,
        PlayerCombatState combatState,
        int startingEnergy,
        int finalEnergy)
    {
        if (relic?.Owner == null || combatState == null) return;
        if (!ReferenceEquals(relic.Owner, combatState._player)) return;

        var energyGained = Math.Max(0, finalEnergy - startingEnergy);
        if (energyGained <= 0) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic) || !IsTrackedPlayer(relic.Owner)) return;
                if (CombatManager.Instance?.IsInProgress != true) return;

                _pendingCombat ??= new PendingCombat();
                var agg = GetOrCreatePendingRelicAggregateLocked(ArtOfWarRelicId);
                RecordArtOfWarEnergyGainedForTest(agg, energyGained);
                _pendingCombat.ArtOfWarEnergyAddedThisTurn.TryGetValue(
                    relic.Owner,
                    out var energyAddedThisTurn);
                _pendingCombat.ArtOfWarEnergyAddedThisTurn[relic.Owner] =
                    energyAddedThisTurn + energyGained;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordArtOfWarEnergyGain failed: {e.Message}");
            }
        }
    }

    internal static void RecordArtOfWarEnergyGainedForTest(RelicAggregate agg, int amount)
    {
        if (agg == null) return;
        agg.EnergyGenerated += Math.Max(0, amount);
    }

    internal static void RecordArtOfWarCombatForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.EnergyGeneratedCombats += Math.Max(0, count);
    }

    internal static void RecordArtOfWarTurnForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.ArtOfWarTurns += Math.Max(0, count);
    }

    /// <summary>
    /// Retain the exact mutable Lightning orb instances created during
    /// Cracked Core's owner callback. Later orb callbacks are attributed only
    /// when they carry one of these references.
    /// </summary>
    public static void TrackCrackedCoreStartingOrbs(
        CrackedCore relic,
        IEnumerable<OrbModel> orbs)
    {
        var owner = relic?.Owner;
        var orbQueue = owner?.PlayerCombatState?.OrbQueue;
        if (owner == null || orbQueue == null || orbs == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic) || !IsTrackedPlayer(owner)) return;
                if (CombatManager.Instance?.IsInProgress != true) return;

                _pendingCombat ??= new PendingCombat();
                foreach (var orb in orbs)
                {
                    if (orb == null || !ReferenceEquals(orb.Owner, owner)) continue;
                    if (!orbQueue.Orbs.Any(
                            queuedOrb => ReferenceEquals(queuedOrb, orb)))
                    {
                        continue;
                    }
                    _pendingCombat.CrackedCoreStartingOrbs.Add(orb);
                }
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"TrackCrackedCoreStartingOrbs failed: {e.Message}");
            }
        }
    }

    public static bool IsTrackedCrackedCoreStartingOrb(OrbModel orb)
    {
        if (orb == null) return false;

        lock (_lock)
        {
            return _pendingCombat?.CrackedCoreStartingOrbs.Contains(orb) == true;
        }
    }

    public static void RecordCrackedCoreStartingOrbPassive(OrbModel orb)
    {
        if (orb == null) return;

        lock (_lock)
        {
            try
            {
                if (_pendingCombat?.CrackedCoreStartingOrbs.Contains(orb) != true) return;

                var agg = GetOrCreatePendingRelicAggregateLocked(CrackedCoreRelicId);
                RecordCrackedCoreOrbPassiveForTest(agg);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordCrackedCoreStartingOrbPassive failed: {e.Message}");
            }
        }
    }

    public static void RecordCrackedCoreStartingOrbEvoked(OrbModel orb)
    {
        if (orb == null) return;

        lock (_lock)
        {
            try
            {
                if (_pendingCombat?.CrackedCoreStartingOrbs.Contains(orb) != true) return;

                var agg = GetOrCreatePendingRelicAggregateLocked(CrackedCoreRelicId);
                RecordCrackedCoreOrbEvokedForTest(agg);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordCrackedCoreStartingOrbEvoked failed: {e.Message}");
            }
        }
    }

    public static void RecordCrackedCoreStartingOrbsFizzled(IEnumerable<OrbModel> removedOrbs)
    {
        if (removedOrbs == null) return;

        lock (_lock)
        {
            try
            {
                if (_pendingCombat == null) return;

                var fizzled = 0;
                foreach (var orb in removedOrbs)
                {
                    if (orb != null && _pendingCombat.CrackedCoreStartingOrbs.Remove(orb))
                        fizzled++;
                }

                if (fizzled <= 0) return;
                var agg = GetOrCreatePendingRelicAggregateLocked(CrackedCoreRelicId);
                RecordCrackedCoreOrbFizzledForTest(agg, fizzled);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordCrackedCoreStartingOrbsFizzled failed: {e.Message}");
            }
        }
    }

    internal static void RecordCrackedCoreOrbPassiveForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.CrackedCoreOrbPassiveTriggers += Math.Max(0, count);
    }

    internal static void RecordCrackedCoreOrbEvokedForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.CrackedCoreOrbEvokes += Math.Max(0, count);
    }

    internal static void RecordCrackedCoreOrbFizzledForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.CrackedCoreOrbFizzles += Math.Max(0, count);
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
    /// Arm Iron Club's immediate draw attribution when the next owner card play
    /// will wrap its live counter. The actual cards drawn are measured from the
    /// draw command result.
    /// </summary>
    public static void ArmIronClubDrawAttribution(IronClub relic, CardPlay cardPlay)
    {
        if (relic?.Owner == null || cardPlay?.Card == null) return;

        var owner = relic.Owner;
        if (!ReferenceEquals(cardPlay.Card.Owner, owner)) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic)) return;
                if (!IsTrackedPlayer(owner)) return;
                if (CombatManager.Instance?.IsInProgress != true) return;

                var cardsPerTrigger = IronClubCardsPerTrigger(relic);
                if (cardsPerTrigger <= 0) return;
                if ((relic.CardsPlayed + 1) % cardsPerTrigger != 0) return;

                _pendingCombat ??= new PendingCombat();
                RecordIronClubCombatForPlayerLocked(owner);
                _pendingCombat.IronClubDrawsRemaining[owner] =
                    _pendingCombat.IronClubDrawsRemaining.TryGetValue(owner, out var existing)
                        ? existing + 1
                        : 1;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"ArmIronClubDrawAttribution failed: {e.Message}");
            }
        }
    }

    public static bool TryConsumeIronClubDrawAttribution(Player player)
    {
        if (player == null) return false;

        lock (_lock)
        {
            try
            {
                if (_pendingCombat == null) return false;
                if (!_pendingCombat.IronClubDrawsRemaining.TryGetValue(player, out var remaining)
                    || remaining <= 0)
                {
                    return false;
                }

                remaining -= 1;
                if (remaining <= 0)
                    _pendingCombat.IronClubDrawsRemaining.Remove(player);
                else
                    _pendingCombat.IronClubDrawsRemaining[player] = remaining;
                return true;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"TryConsumeIronClubDrawAttribution failed: {e.Message}");
                return false;
            }
        }
    }

    public static void DisarmIronClubDrawAttribution(Player player)
    {
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                _pendingCombat?.IronClubDrawsRemaining.Remove(player);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"DisarmIronClubDrawAttribution failed: {e.Message}");
            }
        }
    }

    public static void RecordIronClubCardsDrawn(int cardsDrawn)
    {
        if (cardsDrawn <= 0) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(IronClubRelicId);
                agg.AdditionalCardsDrawn += cardsDrawn;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordIronClubCardsDrawn failed: {e.Message}");
            }
        }
    }

    internal static void RecordIronClubStatsForTest(
        RelicAggregate agg,
        int combats,
        int cardsDrawn,
        int combatsEndedOn0Charges,
        int combatsEndedOn1Charges,
        int combatsEndedOn2Charges,
        int combatsEndedOn3Charges)
    {
        if (agg == null) return;
        var charge0Combats = Math.Max(0, combatsEndedOn0Charges);
        var charge1Combats = Math.Max(0, combatsEndedOn1Charges);
        var charge2Combats = Math.Max(0, combatsEndedOn2Charges);
        var charge3Combats = Math.Max(0, combatsEndedOn3Charges);

        agg.IronClubCombats += Math.Max(0, combats);
        agg.AdditionalCardsDrawn += Math.Max(0, cardsDrawn);
        agg.IronClubCombatsEndedOn0Charges += charge0Combats;
        agg.IronClubCombatsEndedOn1Charges += charge1Combats;
        agg.IronClubCombatsEndedOn2Charges += charge2Combats;
        agg.IronClubCombatsEndedOn3Charges += charge3Combats;
        agg.IronClubCombatEndChargeTotal +=
            charge1Combats
            + (charge2Combats * 2)
            + (charge3Combats * 3);
        agg.IronClubCombatEndChargeCount +=
            charge0Combats
            + charge1Combats
            + charge2Combats
            + charge3Combats;
    }

    internal static void RecordIronClubCombatEndChargeForTest(RelicAggregate agg, int charge)
    {
        if (agg == null) return;
        if (charge < 0 || charge > 3) return;

        agg.IronClubCombatEndChargeTotal += charge;
        agg.IronClubCombatEndChargeCount += 1;

        switch (charge)
        {
            case 0:
                agg.IronClubCombatsEndedOn0Charges += 1;
                break;
            case 1:
                agg.IronClubCombatsEndedOn1Charges += 1;
                break;
            case 2:
                agg.IronClubCombatsEndedOn2Charges += 1;
                break;
            case 3:
                agg.IronClubCombatsEndedOn3Charges += 1;
                break;
        }
    }

    /// <summary>
    /// Record a Lantern/Very Hot Cocoa/Candelabra/Chandelier owner-specific
    /// activation and arm observed energy attribution for its immediate gain.
    /// </summary>
    public static void RecordTurnEnergyRelicActivationAndArmEnergyAttribution(string relicId, Player? owner)
    {
        if (!IsTurnEnergyRelicId(relicId)) return;
        if (owner == null || !IsTrackedPlayer(owner)) return;

        lock (_lock)
        {
            try
            {
                _pendingCombat ??= new PendingCombat();
                var agg = GetOrCreateRelicAggregateLocked(relicId);
                agg.Activations += 1;
                _pendingCombat.Windows.Arm(
                    relicId,
                    AttributionEventKind.PlayerEnergyGain,
                    CurrentHistoryCountLocked(),
                    ownerId: owner,
                    maxHistoryAdvance: 0);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordTurnEnergyRelicActivationAndArmEnergyAttribution failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Count player rounds ending with unspent energy while the matching
    /// Lantern/Very Hot Cocoa/Candelabra/Chandelier relic is held.
    /// Called from the global before-turn-end hook so the energy pool has not
    /// been cleared yet.
    /// </summary>
    public static void RecordTurnEnergyRelicTurnEndedWithExcessEnergy(ICombatState? combatState, IEnumerable<Creature>? participants)
    {
        if (combatState == null) return;
        if (participants == null) return;

        lock (_lock)
        {
            try
            {
                var roundNumber = combatState.RoundNumber;
                if (roundNumber <= 0) return;

                foreach (var creature in participants)
                {
                    var player = creature?.Player;
                    if (player == null || !IsTrackedPlayer(player)) continue;
                    var playerCombatState = player.PlayerCombatState;
                    if (playerCombatState == null) continue;
                    if (playerCombatState.Energy <= 0) continue;

                    if (IsTurnEnergyRelicExcessRound(LanternRelicId, roundNumber) && PlayerHasLantern(player))
                        RecordTurnEnergyRelicExcessEnergyForPlayerLocked(LanternRelicId, player);
                    if (IsTurnEnergyRelicExcessRound(VeryHotCocoaRelicId, roundNumber) && PlayerHasVeryHotCocoa(player))
                        RecordTurnEnergyRelicExcessEnergyForPlayerLocked(VeryHotCocoaRelicId, player);
                    if (IsTurnEnergyRelicExcessRound(CandelabraRelicId, roundNumber) && PlayerHasCandelabra(player))
                        RecordTurnEnergyRelicExcessEnergyForPlayerLocked(CandelabraRelicId, player);
                    if (IsTurnEnergyRelicExcessRound(ChandelierRelicId, roundNumber) && PlayerHasChandelier(player))
                        RecordTurnEnergyRelicExcessEnergyForPlayerLocked(ChandelierRelicId, player);
                }
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordTurnEnergyRelicTurnEndedWithExcessEnergy failed: {e.Message}");
            }
        }
    }

    private static void RecordTurnEnergyRelicExcessEnergyForPlayerLocked(string relicId, Player player)
    {
        _pendingCombat ??= new PendingCombat();

        var recorded = relicId switch
        {
            LanternRelicId => _pendingCombat.LanternFirstTurnExcessRecordedPlayers.Add(player),
            VeryHotCocoaRelicId => _pendingCombat.VeryHotCocoaFirstTurnExcessRecordedPlayers.Add(player),
            CandelabraRelicId => _pendingCombat.CandelabraSecondTurnExcessRecordedPlayers.Add(player),
            ChandelierRelicId => _pendingCombat.ChandelierThirdTurnExcessRecordedPlayers.Add(player),
            _ => false,
        };
        if (!recorded) return;

        var agg = GetOrCreateRelicAggregateLocked(relicId);
        switch (relicId)
        {
            case LanternRelicId:
            case VeryHotCocoaRelicId:
                agg.FirstTurnsEndedWithExcessEnergy += 1;
                break;
            case CandelabraRelicId:
                agg.SecondTurnsEndedWithExcessEnergy += 1;
                break;
            case ChandelierRelicId:
                agg.ThirdTurnsEndedWithExcessEnergy += 1;
                break;
        }
    }

    private static void RecordTurnEnergyRelicCombatsWithoutEnergyForTrackedPlayerLocked()
    {
        try
        {
            var player = GetTrackedRunPlayerLocked();
            if (player == null) return;

            RecordTurnEnergyRelicCombatWithoutEnergyForPlayerLocked(player, CandelabraRelicId, PlayerHasCandelabra(player));
            RecordTurnEnergyRelicCombatWithoutEnergyForPlayerLocked(player, ChandelierRelicId, PlayerHasChandelier(player));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordTurnEnergyRelicCombatsWithoutEnergyForTrackedPlayerLocked failed: {e.Message}");
        }
    }

    private static void RecordTurnEnergyRelicCombatWithoutEnergyForPlayerLocked(Player player, string relicId, bool held)
    {
        if (_pendingCombat == null || !held) return;

        var gainedEnergy = _pendingCombat.RelicAggregates.TryGetValue(relicId, out var agg)
            && agg.EnergyGenerated > 0;
        if (gainedEnergy) return;

        GetOrCreatePendingRelicAggregateLocked(relicId).CombatsWithoutActivation += 1;
    }

    private static bool IsTurnEnergyRelicId(string relicId)
        => relicId == LanternRelicId
            || relicId == VeryHotCocoaRelicId
            || relicId == CandelabraRelicId
            || relicId == ChandelierRelicId;

    internal static bool IsTurnEnergyRelicExcessRoundForTest(string relicId, int roundNumber)
        => IsTurnEnergyRelicExcessRound(relicId, roundNumber);

    private static bool IsTurnEnergyRelicExcessRound(string relicId, int roundNumber)
        => relicId switch
        {
            LanternRelicId => roundNumber == 1,
            VeryHotCocoaRelicId => roundNumber == 1,
            CandelabraRelicId => roundNumber == 2,
            ChandelierRelicId => roundNumber == 3,
            _ => false,
        };

    private static bool PlayerHasLantern(Player player)
    {
        return PlayerHasRelic(player, LanternRelicId, r => r is Lantern);
    }

    private static bool PlayerHasVeryHotCocoa(Player player)
    {
        return PlayerHasRelic(player, VeryHotCocoaRelicId, r => r is VeryHotCocoa);
    }

    private static bool PlayerHasCandelabra(Player player)
    {
        return PlayerHasRelic(player, CandelabraRelicId, r => r is Candelabra);
    }

    private static bool PlayerHasChandelier(Player player)
    {
        return PlayerHasRelic(player, ChandelierRelicId, r => r is Chandelier);
    }

    private static bool PlayerHasRelic(Player player, string relicId, Func<RelicModel, bool> typeMatch)
    {
        try
        {
            return player.Relics.Any(r =>
                r != null
                && (typeMatch(r) || string.Equals(r.Id.ToString(), relicId, StringComparison.Ordinal)));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Arm Booming Conch's owner-specific Elite combat-start energy
    /// attribution. The actual energy delta is observed later at
    /// PlayerCombatState.GainEnergy.
    /// </summary>
    public static void ArmBoomingConchEnergyAttribution(Player owner)
    {
        if (owner == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(owner)) return;

                _pendingCombat ??= new PendingCombat();
                _pendingCombat.Windows.Arm(
                    BoomingConchRelicId,
                    AttributionEventKind.PlayerEnergyGain,
                    CurrentHistoryCountLocked(),
                    ownerId: owner,
                    maxHistoryAdvance: -1);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"ArmBoomingConchEnergyAttribution failed: {e.Message}");
            }
        }
    }

    public static void DisarmBoomingConchEnergyAttribution()
    {
        lock (_lock)
        {
            try
            {
                _pendingCombat?.Windows.Disarm(BoomingConchRelicId, AttributionEventKind.PlayerEnergyGain);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"DisarmBoomingConchEnergyAttribution failed: {e.Message}");
            }
        }
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

    internal static void RecordPendulumCombatForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.PendulumCombats += Math.Max(0, count);
    }

    internal static void RecordPendulumCombatEndChargeForTest(RelicAggregate agg, int charge)
    {
        if (agg == null) return;
        if (charge < 0 || charge > 2) return;

        agg.PendulumCombatEndChargeTotal += charge;
        agg.PendulumCombatEndChargeCount += 1;

        switch (charge)
        {
            case 0:
                agg.PendulumCombatsEndedOn0Charges += 1;
                break;
            case 1:
                agg.PendulumCombatsEndedOn1Charge += 1;
                break;
            case 2:
                agg.PendulumCombatsEndedOn2Charges += 1;
                break;
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

    /// <summary>
    /// Record Captain's Wheel's owner-specific third-turn block-clear trigger.
    /// The actual block gained is observed from the gain-block command result.
    /// </summary>
    public static void ArmCaptainsWheelAttribution(Creature creature)
    {
        if (creature == null) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(CaptainsWheelRelicId);
                agg.Activations += 1;
                _pendingCaptainsWheelBlockAttributions.Add(creature);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"ArmCaptainsWheelAttribution failed: {e.Message}");
            }
        }
    }

    public static bool TryConsumeCaptainsWheelBlockAttribution(Creature creature)
    {
        if (creature == null) return false;

        lock (_lock)
        {
            try
            {
                return ConsumePendingCreatureAttribution(
                    _pendingCaptainsWheelBlockAttributions,
                    creature);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug(
                    $"TryConsumeCaptainsWheelBlockAttribution failed: {e.Message}");
                return false;
            }
        }
    }

    public static void RecordCaptainsWheelBlockGained(decimal amount)
    {
        if (amount <= 0m) return;

        lock (_lock)
        {
            try
            {
                var agg = GetOrCreateRelicAggregateLocked(CaptainsWheelRelicId);
                agg.AdditionalBlockGained += (int)amount;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordCaptainsWheelBlockGained failed: {e.Message}");
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
    /// Record one completed Seal of Gold activation from its owner-specific
    /// turn-start callback. The callback's own affordability gate establishes
    /// that the relic triggered; before/after snapshots preserve the actual
    /// energy gained and gold lost.
    /// </summary>
    public static void RecordSealOfGoldActivation(
        SealOfGold relic,
        Player? owner,
        int intendedGoldLoss,
        int initialGold,
        int finalGold,
        int initialEnergy,
        int finalEnergy)
    {
        if (relic == null || owner == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic) || !IsTrackedPlayer(owner)) return;
                if (relic.Owner != null && !ReferenceEquals(relic.Owner, owner)) return;

                var agg = GetOrCreateRelicAggregateLocked(SealOfGoldRelicId);
                AccumulateSealOfGoldActivation(
                    agg,
                    intendedGoldLoss,
                    initialGold,
                    finalGold,
                    initialEnergy,
                    finalEnergy);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordSealOfGoldActivation failed: {e.Message}");
            }
        }
    }

    private static void AccumulateSealOfGoldActivation(
        RelicAggregate agg,
        int intendedGoldLoss,
        int initialGold,
        int finalGold,
        int initialEnergy,
        int finalEnergy)
    {
        if (agg == null) return;

        int intended = Math.Max(0, intendedGoldLoss);
        int observedGoldLost = Math.Max(0, initialGold - finalGold);
        int actualGoldLost = Math.Min(intended, observedGoldLost);

        agg.Activations++;
        agg.GoldLost += actualGoldLost;
        agg.GoldLossBlocked += Math.Max(0, intended - actualGoldLost);
        agg.EnergyGenerated += Math.Max(0, finalEnergy - initialEnergy);
    }

    internal static void RecordSealOfGoldActivationForTest(
        RelicAggregate agg,
        int intendedGoldLoss,
        int initialGold,
        int finalGold,
        int initialEnergy,
        int finalEnergy)
        => AccumulateSealOfGoldActivation(
            agg,
            intendedGoldLoss,
            initialGold,
            finalGold,
            initialEnergy,
            finalEnergy);

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
    /// Snapshot the final options when a Fresnel Lens owner's card-reward
    /// selection opens. The same reward can be rerolled, so the snapshot is
    /// refreshable and is counted only when the selection resolves.
    /// </summary>
    public static void NoteFresnelLensRewardOpened(CardReward reward)
    {
        if (reward == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(reward.Player)) return;
                if (!PlayerHasFresnelLens(reward.Player)) return;

                if (!_fresnelLensRewards.ContainsKey(reward))
                    _fresnelLensRewards[reward] = PendingFresnelLensReward.FromReward(reward);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"NoteFresnelLensRewardOpened failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// A Driftwood reroll reuses the same CardReward and screen. Replace the
    /// option snapshot so the eventual taken/skipped result describes the
    /// cards the player could actually choose after rerolling.
    /// </summary>
    public static void RefreshFresnelLensRewardAfterReroll(CardReward reward)
    {
        if (reward == null) return;

        lock (_lock)
        {
            try
            {
                if (!_fresnelLensRewards.ContainsKey(reward)) return;
                _fresnelLensRewards[reward] = PendingFresnelLensReward.FromReward(reward);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RefreshFresnelLensRewardAfterReroll failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Resolve one opened Fresnel Lens card reward. CardReward removes each
    /// successfully obtained card from its option list, so the observed drop
    /// in Nimble options is the number actually taken rather than merely
    /// clicked. Reward stats happen outside combat and persist immediately.
    /// </summary>
    public static void RecordFresnelLensRewardResolved(CardReward reward)
    {
        if (reward == null) return;

        lock (_lock)
        {
            try
            {
                if (!_fresnelLensRewards.Remove(reward, out var pending)) return;
                if (!IsTrackedPlayer(reward.Player)) return;

                var remaining = PendingFresnelLensReward.FromReward(reward).NimbleCards;
                var taken = Math.Max(0, pending.NimbleCards - remaining);
                var agg = GetOrCreateCurrentRunRelicAggregateLocked(FresnelLensRelicId);
                RecordFresnelLensRewardForTest(agg, pending.NimbleCards, taken);
                RefreshCurrentRunMetadataLocked();
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordFresnelLensRewardResolved failed: {e.Message}");
            }
        }
    }

    public static void CancelFresnelLensReward(CardReward reward)
    {
        if (reward == null) return;
        lock (_lock)
            _fresnelLensRewards.Remove(reward);
    }

    /// <summary>
    /// Record Drowning Beacon's observed max-HP cost across the full climb
    /// option. The event loses max HP before obtaining Fresnel Lens, so the
    /// relic's own pickup callbacks cannot recover the original value.
    /// </summary>
    public static void RecordFresnelLensEventMaxHpChanged(
        Player player,
        decimal originalMaxHp,
        decimal newMaxHp)
    {
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(player)) return;
                if (!PlayerHasFresnelLens(player)) return;

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(FresnelLensRelicId);
                RecordFresnelLensMaxHpChangedForTest(agg, originalMaxHp, newMaxHp);
                RefreshCurrentRunMetadataLocked();
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordFresnelLensEventMaxHpChanged failed: {e.Message}");
            }
        }
    }

    internal static void RecordFresnelLensRewardForTest(
        RelicAggregate agg,
        int nimbleCardsOffered,
        int nimbleCardsTaken)
    {
        if (agg == null) return;

        var offered = Math.Max(0, nimbleCardsOffered);
        var taken = Math.Clamp(nimbleCardsTaken, 0, offered);
        agg.NimbleCardsTaken += taken;

        if (offered == 0)
        {
            agg.RewardScreensWithoutNimbleCards += 1;
            return;
        }

        agg.RewardScreensWithNimbleCards += 1;

        if (offered == 2)
            agg.RewardScreensWithTwoNimbleCards += 1;
        else if (offered >= 3)
            agg.RewardScreensWithThreeOrMoreNimbleCards += 1;

        if (taken == 0)
            agg.RewardScreensWithNimbleCardsButNoneTaken += 1;
    }

    internal static void RecordFresnelLensMaxHpChangedForTest(
        RelicAggregate agg,
        decimal originalMaxHp,
        decimal newMaxHp)
        => RecordRelicMaxHpChangeForTest(agg, originalMaxHp, newMaxHp);

    private static bool PlayerHasFresnelLens(Player? player)
    {
        try
        {
            return player?.Relics?.Any(relic => relic is FresnelLens) == true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Open the narrow synchronous registration window around Orrery's
    /// AfterObtained method. Orrery constructs its five CardReward objects and
    /// passes them to RewardsCmd.OfferCustom before its first await.
    /// </summary>
    public static bool BeginOrreryRewardRegistration(Orrery relic)
    {
        if (relic?.Owner == null) return false;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedRelic(relic)) return false;
                _orreryRewardRegistrationRelic = relic;
                return true;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"BeginOrreryRewardRegistration failed: {e.Message}");
                return false;
            }
        }
    }

    public static void EndOrreryRewardRegistration()
    {
        lock (_lock)
        {
            _orreryRewardRegistrationRelic = null;
        }
    }

    /// <summary>
    /// Bind Orrery's exact custom CardReward instances in creation order. The
    /// pending entries are persisted immediately so their numbering survives a
    /// Core reload while the reward page is open.
    /// </summary>
    public static void RegisterOrreryCustomRewards(Player player, List<Reward> rewards)
    {
        if (player == null || rewards == null) return;

        lock (_lock)
        {
            try
            {
                var relic = _orreryRewardRegistrationRelic;
                if (relic?.Owner == null
                    || !ReferenceEquals(relic.Owner, player)
                    || !IsTrackedRelic(relic))
                    return;

                var cardRewards = rewards
                    .OfType<CardReward>()
                    .Where(reward => ReferenceEquals(reward.Player, player))
                    .ToList();
                var expectedRewards = Math.Max(1, relic.DynamicVars.Cards.IntValue);
                if (cardRewards.Count != expectedRewards)
                {
                    CoreMain.LogDebug(
                        $"RegisterOrreryCustomRewards expected {expectedRewards} card rewards, " +
                        $"observed {cardRewards.Count}; registration skipped.");
                    return;
                }

                var floor = CurrentRunFloorLocked();
                var agg = GetOrCreateCurrentRunRelicAggregateLocked(OrreryRelicId);
                for (var index = 0; index < cardRewards.Count; index++)
                {
                    var pending = new PendingOrreryReward(index + 1, floor, player);
                    _orreryRewards[cardRewards[index]] = pending;
                    RecordOrreryRewardForTest(
                        agg,
                        BuildOrreryRewardAggregateLocked(pending, "pending"));
                }

                RefreshCurrentRunMetadataLocked();
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RegisterOrreryCustomRewards failed: {e.Message}");
            }
        }
    }

    public static bool IsTrackedOrreryReward(CardReward reward)
    {
        if (reward == null) return false;

        lock (_lock)
        {
            return _orreryRewards.ContainsKey(reward);
        }
    }

    /// <summary>
    /// Capture the generated/rerolled option signature for hot-reload
    /// re-association. Rerolling changes the options but not the Orrery reward
    /// number or its eventual handling.
    /// </summary>
    public static void RefreshOrreryRewardOptions(CardReward reward)
    {
        if (reward == null) return;

        lock (_lock)
        {
            try
            {
                if (!_orreryRewards.TryGetValue(reward, out var pending)
                    && !TryRestoreOrreryRewardLocked(reward, out pending))
                    return;

                CaptureOrreryOfferedCardsLocked(pending, reward);
                var agg = GetOrCreateCurrentRunRelicAggregateLocked(OrreryRelicId);
                RecordOrreryRewardForTest(
                    agg,
                    BuildOrreryRewardAggregateLocked(pending, "pending"));
                RefreshCurrentRunMetadataLocked();
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RefreshOrreryRewardOptions failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Snapshot the physical deck immediately before an Orrery card reward
    /// opens. A successful CardReward completion can then identify the final
    /// card object that actually entered the deck, including replacements.
    /// </summary>
    public static void NoteOrreryRewardOpened(CardReward reward)
    {
        if (reward == null) return;

        lock (_lock)
        {
            try
            {
                if (!_orreryRewards.TryGetValue(reward, out var pending)
                    && !TryRestoreOrreryRewardLocked(reward, out pending))
                    return;

                CaptureOrreryOfferedCardsLocked(pending, reward);
                pending.DeckBeforeSelection = new HashSet<CardModel>(
                    SnapshotDeckCards(pending.Player),
                    ReferenceEqualityComparer.Instance);

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(OrreryRelicId);
                RecordOrreryRewardForTest(
                    agg,
                    BuildOrreryRewardAggregateLocked(pending, "pending"));
                RefreshCurrentRunMetadataLocked();
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"NoteOrreryRewardOpened failed: {e.Message}");
            }
        }
    }

    public static void RecordOrreryRewardObtainedCards(CardReward reward)
    {
        if (reward == null) return;

        lock (_lock)
        {
            try
            {
                if (!_orreryRewards.Remove(reward, out var pending)) return;
                if (!IsTrackedPlayer(pending.Player)) return;

                var obtainedCards = pending.DeckBeforeSelection == null
                    ? new List<CardModel>()
                    : NewDeckCardsSince(pending.Player, pending.DeckBeforeSelection);
                var outcome = obtainedCards.Count > 0 ? "obtained" : "completed_without_card";

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(OrreryRelicId);
                RecordOrreryRewardForTest(
                    agg,
                    BuildOrreryRewardAggregateLocked(pending, outcome, obtainedCards: obtainedCards));
                RefreshCurrentRunMetadataLocked();
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordOrreryRewardObtainedCards failed: {e.Message}");
            }
        }
    }

    public static void RecordOrreryRewardAlternative(CardReward reward, string? alternativeId)
    {
        if (reward == null || string.IsNullOrWhiteSpace(alternativeId)) return;

        lock (_lock)
        {
            try
            {
                if (!_orreryRewards.Remove(reward, out var pending)
                    && !TryRestoreOrreryRewardLocked(reward, out pending))
                    return;

                _orreryRewards.Remove(reward);
                var agg = GetOrCreateCurrentRunRelicAggregateLocked(OrreryRelicId);
                RecordOrreryRewardForTest(
                    agg,
                    BuildOrreryRewardAggregateLocked(
                        pending,
                        "alternative",
                        alternativeId: alternativeId));
                RefreshCurrentRunMetadataLocked();
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordOrreryRewardAlternative failed: {e.Message}");
            }
        }
    }

    public static void RecordOrreryRewardSkipped(CardReward reward)
    {
        if (reward == null) return;

        lock (_lock)
        {
            try
            {
                if (!_orreryRewards.Remove(reward, out var pending)
                    && !TryRestoreOrreryRewardLocked(reward, out pending))
                    return;

                _orreryRewards.Remove(reward);
                var agg = GetOrCreateCurrentRunRelicAggregateLocked(OrreryRelicId);
                RecordOrreryRewardForTest(
                    agg,
                    BuildOrreryRewardAggregateLocked(pending, "skipped"));
                RefreshCurrentRunMetadataLocked();
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordOrreryRewardSkipped failed: {e.Message}");
            }
        }
    }

    internal static void RecordOrreryRewardForTest(
        RelicAggregate agg,
        OrreryRewardAggregate reward)
    {
        if (agg == null || reward == null || reward.RewardNumber is < 1 or > 5) return;

        var copy = new OrreryRewardAggregate
        {
            RewardNumber = reward.RewardNumber,
            Floor = reward.Floor,
            Outcome = string.IsNullOrWhiteSpace(reward.Outcome) ? "pending" : reward.Outcome,
            AlternativeId = reward.AlternativeId ?? "",
            OfferedCardIds = (reward.OfferedCardIds ?? new List<string>())
                .Where(cardId => !string.IsNullOrWhiteSpace(cardId))
                .ToList(),
            CardsObtained = (reward.CardsObtained ?? new List<OrreryObtainedCardAggregate>())
                .Where(card => card != null)
                .Select(card => new OrreryObtainedCardAggregate
                {
                    CardId = card.CardId ?? "",
                    DisplayName = card.DisplayName ?? "",
                    UpgradeLevel = Math.Max(0, card.UpgradeLevel),
                })
                .ToList(),
        };

        agg.OrreryRewards ??= new List<OrreryRewardAggregate>();
        var existingIndex = agg.OrreryRewards.FindIndex(candidate =>
            candidate != null && candidate.RewardNumber == copy.RewardNumber);
        if (existingIndex >= 0)
        {
            var existing = agg.OrreryRewards[existingIndex];
            if (!string.Equals(existing.Outcome, "pending", StringComparison.Ordinal)
                && string.Equals(copy.Outcome, "pending", StringComparison.Ordinal))
                return;

            agg.OrreryRewards[existingIndex] = copy;
        }
        else
        {
            agg.OrreryRewards.Add(copy);
        }

        agg.OrreryRewards.Sort((left, right) =>
            (left?.RewardNumber ?? int.MaxValue).CompareTo(right?.RewardNumber ?? int.MaxValue));
    }

    private static OrreryRewardAggregate BuildOrreryRewardAggregateLocked(
        PendingOrreryReward pending,
        string outcome,
        string? alternativeId = null,
        IReadOnlyList<CardModel>? obtainedCards = null)
    {
        return new OrreryRewardAggregate
        {
            RewardNumber = pending.RewardNumber,
            Floor = pending.Floor,
            Outcome = outcome,
            AlternativeId = alternativeId ?? "",
            OfferedCardIds = pending.OfferedCardIds.ToList(),
            CardsObtained = (obtainedCards ?? Array.Empty<CardModel>())
                .Where(card => card != null)
                .Select(card => new OrreryObtainedCardAggregate
                {
                    CardId = GetCardIdForStats(card),
                    DisplayName = GetCardDisplayNameForStats(card),
                    UpgradeLevel = GetRewardCardUpgradeLevelForStats(card),
                })
                .ToList(),
        };
    }

    private static void CaptureOrreryOfferedCardsLocked(
        PendingOrreryReward pending,
        CardReward reward)
    {
        pending.OfferedCardIds.Clear();
        pending.OfferedCardIds.AddRange(
            reward._cards
                .Where(option => option?.Card != null)
                .Select(option => GetRewardCardIdForStats(option.Card)));
    }

    private static bool TryRestoreOrreryRewardLocked(
        CardReward reward,
        [NotNullWhen(true)] out PendingOrreryReward? pending)
    {
        pending = null;
        if (!IsTrackedPlayer(reward.Player)) return false;

        EnsureLazyCurrentRunLocked();
        if (!_currentRun.RelicAggregates.TryGetValue(OrreryRelicId, out var agg))
            return false;

        var currentFloor = CurrentRunFloorLocked();
        var offeredCardIds = reward._cards
            .Where(option => option?.Card != null)
            .Select(option => GetRewardCardIdForStats(option.Card))
            .ToList();
        var boundNumbers = _orreryRewards.Values
            .Select(candidate => candidate.RewardNumber)
            .ToHashSet();

        var saved = (agg.OrreryRewards ?? new List<OrreryRewardAggregate>())
            .Where(candidate => candidate != null
                && string.Equals(candidate.Outcome, "pending", StringComparison.Ordinal)
                && !boundNumbers.Contains(candidate.RewardNumber)
                && (!candidate.Floor.HasValue
                    || !currentFloor.HasValue
                    || candidate.Floor.Value == currentFloor.Value)
                && (candidate.OfferedCardIds ?? new List<string>())
                    .SequenceEqual(offeredCardIds, StringComparer.Ordinal))
            .OrderBy(candidate => candidate.RewardNumber)
            .FirstOrDefault();
        if (saved == null) return false;

        pending = new PendingOrreryReward(saved.RewardNumber, saved.Floor, reward.Player);
        pending.OfferedCardIds.AddRange(offeredCardIds);
        _orreryRewards[reward] = pending;
        return true;
    }

    /// <summary>
    /// Arm one of Silver Crucible's three charge-consuming card rewards after
    /// Populate has finished. The screen number comes from the relic's saved
    /// TimesUsed value, while the option snapshot preserves the final visible
    /// order and CardCreationResult identity for observed taken attribution.
    /// </summary>
    public static void NoteSilverCrucibleRewardGenerated(CardReward reward, int screenNumber)
    {
        if (reward == null || screenNumber is < 1 or > 3) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(reward.Player)) return;

                var pending = new PendingSilverCrucibleReward(screenNumber, CurrentRunFloorLocked());
                CaptureSilverCrucibleRewardOptionsLocked(pending, reward);
                _silverCrucibleRewards[reward] = pending;
                _silverCrucibleRestoreBatchScreenNumbers.Remove(screenNumber);

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(SilverCrucibleRelicId);
                RecordSilverCrucibleRewardForTest(
                    agg,
                    BuildSilverCrucibleRewardScreenLocked(pending, remaining: null, resolved: false));
                RefreshCurrentRunMetadataLocked();
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"NoteSilverCrucibleRewardGenerated failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Refresh the display snapshot immediately before the card selection is
    /// shown. Relics obtained from another reward on the same page can modify
    /// CardCreationResult.Card after Populate while preserving the result
    /// object's identity. Reopenings refresh only while every original result
    /// is still present, so a multi-pick hook can never erase an earlier take.
    /// </summary>
    public static void NoteSilverCrucibleRewardOpened(CardReward reward)
    {
        if (reward == null) return;

        lock (_lock)
        {
            try
            {
                if (!_silverCrucibleRewards.TryGetValue(reward, out var pending)
                    && !TryRestoreSilverCrucibleRewardLocked(reward, allowBatchFallback: false, out pending))
                    return;

                var currentResults = new HashSet<CardCreationResult>(
                    reward._cards,
                    ReferenceEqualityComparer.Instance);
                var canRefresh = !pending.SelectionOpened
                    || (pending.Cards.Count == currentResults.Count
                        && pending.Cards.All(card => currentResults.Contains(card.Result)));
                if (!canRefresh) return;

                CaptureSilverCrucibleRewardOptionsLocked(pending, reward);
                pending.SelectionOpened = true;

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(SilverCrucibleRelicId);
                RecordSilverCrucibleRewardForTest(
                    agg,
                    BuildSilverCrucibleRewardScreenLocked(pending, remaining: null, resolved: false));
                RefreshCurrentRunMetadataLocked();
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"NoteSilverCrucibleRewardOpened failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Finalize a Silver Crucible reward at an observed terminal boundary:
    /// successful CardReward completion, outer rewards-page skip, or the
    /// pre-clear side of a Driftwood reroll. CardReward removes a
    /// CardCreationResult only after that card successfully enters the deck,
    /// so missing result references are the cards actually taken.
    /// </summary>
    public static void RecordSilverCrucibleRewardResolved(CardReward reward)
    {
        if (reward == null) return;

        lock (_lock)
        {
            try
            {
                if (!_silverCrucibleRewards.TryGetValue(reward, out var pending)
                    && !TryRestoreSilverCrucibleRewardLocked(reward, allowBatchFallback: false, out pending))
                    return;

                _silverCrucibleRewards.Remove(reward);
                if (!IsTrackedPlayer(reward.Player)) return;

                var remaining = new HashSet<CardCreationResult>(reward._cards, ReferenceEqualityComparer.Instance);
                var screen = BuildSilverCrucibleRewardScreenLocked(pending, remaining, resolved: true);

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(SilverCrucibleRelicId);
                RecordSilverCrucibleRewardForTest(agg, screen);
                RefreshCurrentRunMetadataLocked();
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordSilverCrucibleRewardResolved failed: {e.Message}");
            }
        }
    }

    public static void PreserveSilverCrucibleRewardAfterFault(CardReward reward)
    {
        // Keep both the live binding and the persisted unresolved screen. A
        // fault is not an observed skip/take boundary, and unbinding it would
        // make the same screen eligible for an unrelated reward restore.
        if (reward == null) return;

        lock (_lock)
        {
            try
            {
                if (_silverCrucibleRewards.ContainsKey(reward)) return;
                TryRestoreSilverCrucibleRewardLocked(reward, allowBatchFallback: false, out _);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"PreserveSilverCrucibleRewardAfterFault failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Open a tightly bounded ordered-restore window around one
    /// RewardsSet.GenerateWithoutOffering call. Continued combat rooms rebuild
    /// CardReward options rather than serializing them, so signatures may
    /// differ; within this one generation batch, sequential Populate calls are
    /// the stable fallback for the saved Silver use order.
    /// </summary>
    public static bool BeginSilverCrucibleRewardRestoreBatch(Player player)
    {
        if (player == null) return false;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(player)) return false;
                if (player.Relics?.Any(relic => relic is SilverCrucible) != true) return false;

                if (_silverCrucibleRestoreBatchDepth > 0)
                {
                    _silverCrucibleRestoreBatchDepth += 1;
                    return true;
                }

                EnsureLazyCurrentRunLocked();
                if (!_currentRun.RelicAggregates.TryGetValue(SilverCrucibleRelicId, out var agg))
                    return false;

                var candidates = GetUnboundUnresolvedSilverCrucibleScreensLocked(agg);
                if (candidates.Count == 0) return false;

                _silverCrucibleRestoreBatchScreenNumbers.Clear();
                _silverCrucibleRestoreBatchScreenNumbers.AddRange(
                    candidates.Select(screen => screen.ScreenNumber));
                _silverCrucibleRestoreBatchDepth = 1;
                return true;
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"BeginSilverCrucibleRewardRestoreBatch failed: {e.Message}");
                return false;
            }
        }
    }

    public static void EndSilverCrucibleRewardRestoreBatch()
    {
        lock (_lock)
        {
            if (_silverCrucibleRestoreBatchDepth <= 0) return;

            _silverCrucibleRestoreBatchDepth -= 1;
            if (_silverCrucibleRestoreBatchDepth == 0)
                _silverCrucibleRestoreBatchScreenNumbers.Clear();
        }
    }

    /// <summary>
    /// Rebind a generated-but-unresolved screen after a Core hot reload or a
    /// continued rewards page. Exact ordered card signatures are preferred;
    /// when Continue regenerates different cards, ordered fallback is allowed
    /// only inside that reward set's bounded generation batch.
    /// </summary>
    public static void RestoreSilverCrucibleRewardAfterPopulate(CardReward reward)
    {
        if (reward == null) return;

        lock (_lock)
        {
            try
            {
                if (_silverCrucibleRewards.ContainsKey(reward)) return;
                if (!TryRestoreSilverCrucibleRewardLocked(
                        reward,
                        allowBatchFallback: _silverCrucibleRestoreBatchDepth > 0,
                        out var pending))
                    return;

                var agg = GetOrCreateCurrentRunRelicAggregateLocked(SilverCrucibleRelicId);
                RecordSilverCrucibleRewardForTest(
                    agg,
                    BuildSilverCrucibleRewardScreenLocked(pending, remaining: null, resolved: false));
                RefreshCurrentRunMetadataLocked();
                SaveCurrentRun();
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RestoreSilverCrucibleRewardAfterPopulate failed: {e.Message}");
            }
        }
    }

    internal static void RecordSilverCrucibleRewardForTest(
        RelicAggregate agg,
        RelicCardRewardScreenAggregate screen)
    {
        if (agg == null || screen == null || screen.ScreenNumber is < 1 or > 3) return;

        var copy = new RelicCardRewardScreenAggregate
        {
            ScreenNumber = screen.ScreenNumber,
            Floor = screen.Floor,
            Resolved = screen.Resolved,
            Cards = (screen.Cards ?? new List<RelicCardRewardOptionAggregate>())
                .Where(card => card != null)
                .Select(card => new RelicCardRewardOptionAggregate
                {
                    CardId = card.CardId ?? "",
                    DisplayName = card.DisplayName ?? "",
                    UpgradeLevel = Math.Max(0, card.UpgradeLevel),
                    Taken = card.Taken,
                })
                .ToList(),
        };

        agg.CardRewardScreens ??= new List<RelicCardRewardScreenAggregate>();
        var existingIndex = agg.CardRewardScreens.FindIndex(candidate =>
            candidate != null && candidate.ScreenNumber == copy.ScreenNumber);
        if (existingIndex >= 0)
            agg.CardRewardScreens[existingIndex] = copy;
        else
            agg.CardRewardScreens.Add(copy);

        agg.CardRewardScreens.Sort((left, right) =>
            (left?.ScreenNumber ?? int.MaxValue).CompareTo(right?.ScreenNumber ?? int.MaxValue));
    }

    private static void CaptureSilverCrucibleRewardOptionsLocked(
        PendingSilverCrucibleReward pending,
        CardReward reward)
    {
        pending.Cards.Clear();
        foreach (var option in reward._cards)
        {
            var card = option?.Card;
            if (option == null || card == null) continue;

            pending.Cards.Add(new PendingSilverCrucibleCard
            {
                Result = option,
                CardId = GetRewardCardIdForStats(card),
                DisplayName = GetRewardCardDisplayNameForStats(card),
                UpgradeLevel = GetRewardCardUpgradeLevelForStats(card),
            });
        }
    }

    private static RelicCardRewardScreenAggregate BuildSilverCrucibleRewardScreenLocked(
        PendingSilverCrucibleReward pending,
        HashSet<CardCreationResult>? remaining,
        bool resolved)
    {
        return new RelicCardRewardScreenAggregate
        {
            ScreenNumber = pending.ScreenNumber,
            Floor = pending.Floor,
            Resolved = resolved,
            Cards = pending.Cards.Select(card => new RelicCardRewardOptionAggregate
            {
                CardId = card.CardId,
                DisplayName = card.DisplayName,
                UpgradeLevel = card.UpgradeLevel,
                Taken = resolved && remaining != null && !remaining.Contains(card.Result),
            }).ToList(),
        };
    }

    private static bool TryRestoreSilverCrucibleRewardLocked(
        CardReward reward,
        bool allowBatchFallback,
        [NotNullWhen(true)] out PendingSilverCrucibleReward? pending)
    {
        pending = null;
        if (!IsTrackedPlayer(reward.Player)) return false;

        EnsureLazyCurrentRunLocked();
        if (!_currentRun.RelicAggregates.TryGetValue(SilverCrucibleRelicId, out var agg))
            return false;

        var candidates = GetUnboundUnresolvedSilverCrucibleScreensLocked(agg);
        if (candidates.Count == 0) return false;

        var matching = candidates
            .Where(screen => SilverCrucibleScreenMatchesReward(screen, reward))
            .ToList();
        var selected = matching.FirstOrDefault();
        if (selected == null
            && allowBatchFallback
            && _silverCrucibleRestoreBatchDepth > 0)
        {
            selected = _silverCrucibleRestoreBatchScreenNumbers
                .Select(screenNumber => candidates.FirstOrDefault(screen => screen.ScreenNumber == screenNumber))
                .FirstOrDefault(screen => screen != null);
        }
        if (selected == null) return false;

        _silverCrucibleRestoreBatchScreenNumbers.Remove(selected.ScreenNumber);
        pending = new PendingSilverCrucibleReward(selected.ScreenNumber, selected.Floor);
        CaptureSilverCrucibleRewardOptionsLocked(pending, reward);
        _silverCrucibleRewards[reward] = pending;
        return true;
    }

    private static List<RelicCardRewardScreenAggregate> GetUnboundUnresolvedSilverCrucibleScreensLocked(
        RelicAggregate agg)
    {
        var boundScreenNumbers = _silverCrucibleRewards.Values
            .Select(candidate => candidate.ScreenNumber)
            .ToHashSet();
        var currentFloor = CurrentRunFloorLocked();

        return (agg.CardRewardScreens ?? new List<RelicCardRewardScreenAggregate>())
            .Where(screen => screen != null
                && !screen.Resolved
                && screen.ScreenNumber is >= 1 and <= 3
                && !boundScreenNumbers.Contains(screen.ScreenNumber)
                && (!screen.Floor.HasValue
                    || !currentFloor.HasValue
                    || screen.Floor.Value == currentFloor.Value))
            .OrderBy(screen => screen.ScreenNumber)
            .ToList();
    }

    private static bool SilverCrucibleScreenMatchesReward(
        RelicCardRewardScreenAggregate screen,
        CardReward reward)
    {
        var savedCards = screen.Cards ?? new List<RelicCardRewardOptionAggregate>();
        var currentCards = reward._cards
            .Where(option => option?.Card != null)
            .Select(option => option.Card)
            .ToList();
        if (savedCards.Count != currentCards.Count) return false;

        for (var index = 0; index < savedCards.Count; index++)
        {
            var saved = savedCards[index];
            var current = currentCards[index];
            if (saved == null || current == null) return false;
            if (!string.Equals(saved.CardId, GetRewardCardIdForStats(current), StringComparison.Ordinal))
                return false;
        }

        return true;
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
    /// Count a distinct player turn toward Cloak Clasp's held-turn average,
    /// including turns where it ultimately grants no block.
    /// </summary>
    public static void RecordCloakClaspTurnStarted(Player player)
    {
        if (player == null) return;

        lock (_lock)
        {
            try
            {
                if (!IsTrackedPlayer(player)) return;
                _pendingCombat ??= new PendingCombat();
                RecordCloakClaspTurnForPlayerLocked(player);
            }
            catch (Exception e)
            {
                CoreMain.LogDebug($"RecordCloakClaspTurnStarted failed: {e.Message}");
            }
        }
    }

    internal static void RecordCloakClaspCombatForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.CloakClaspCombats += Math.Max(0, count);
    }

    internal static void RecordCloakClaspTurnForTest(RelicAggregate agg, int count = 1)
    {
        if (agg == null) return;
        agg.CloakClaspTurns += Math.Max(0, count);
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
            if (string.Equals(relicId, DowsingRodRelicId, StringComparison.Ordinal)
                && RefreshDowsingRoomsRemainingIfOwnedLocked())
            {
                SaveCurrentRun();
            }
            if (string.Equals(relicId, PaelsClawRelicId, StringComparison.Ordinal)
                && RefreshPaelsClawSnapshotIfOwnedLocked())
            {
                SaveCurrentRun();
            }
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
                if (string.Equals(relicId, RuinedHelmetRelicId, StringComparison.Ordinal))
                    result.RuinedHelmetStrengthAddedThisCombat = pending.StrengthAdded;
                if (string.Equals(relicId, ArtOfWarRelicId, StringComparison.Ordinal))
                {
                    result.ArtOfWarEnergyAddedThisCombat = pending.EnergyGenerated;
                    result.ArtOfWarTurnsThisCombat = pending.ArtOfWarTurns;

                    var player = GetTrackedRunPlayerLocked();
                    if (player != null
                        && _pendingCombat.ArtOfWarEnergyAddedThisTurn.TryGetValue(
                            player,
                            out var energyAddedThisTurn))
                    {
                        result.ArtOfWarEnergyAddedThisTurn = energyAddedThisTurn;
                    }
                }
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

    internal static string FormatRelicIdForDisplay(string relicId)
    {
        var value = relicId;
        const string prefix = "RELIC.";
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

    private static int? RelicFloorAddedToDeck(RelicModel relic)
    {
        try
        {
            var floor = relic.FloorAddedToDeck;
            return floor > 0 ? floor : null;
        }
        catch
        {
            return null;
        }
    }

    private static int? RelicFloorAddedToDeckIncludingRunStart(RelicModel relic)
    {
        try
        {
            var floor = relic.FloorAddedToDeck;
            return floor >= 0 ? floor : null;
        }
        catch
        {
            return null;
        }
    }

    private static void RecordStrikeDummyStrikePlayedIfOwnedLocked(CardModel card)
    {
        if (!IsStrikeDummyStrikeCard(card)) return;

        var owner = card.Owner;
        if (owner == null || !IsTrackedPlayer(owner) || !PlayerHasStrikeDummy(owner)) return;

        var agg = GetOrCreateRelicAggregateForCurrentContextLocked(StrikeDummyRelicId);
        agg.StrikeDummyStrikesPlayed += 1;
    }

    private static void RecordNutritiousSoupEnchantedStrikePlayedIfOwnedLocked(CardModel card)
    {
        if (!IsNutritiousSoupEnchantedStrikeCard(card)) return;

        var owner = card.Owner;
        if (owner == null || !IsTrackedPlayer(owner) || !PlayerHasNutritiousSoup(owner)) return;

        var agg = GetOrCreateRelicAggregateForCurrentContextLocked(NutritiousSoupRelicId);
        RecordNutritiousSoupEnchantedStrikePlayedForTest(agg);
    }

    private static void RecordPaelsClawGoopyCardPlayedIfOwnedLocked(CardModel card)
    {
        if (!IsGoopyCard(card)) return;

        var owner = card.Owner;
        if (owner == null || !IsTrackedPlayer(owner) || !PlayerHasPaelsClaw(owner)) return;

        RecordPaelsClawCombatForPlayerLocked(owner);
        RecordPaelsClawTurnForPlayerLocked(owner);

        var agg = GetOrCreatePendingRelicAggregateLocked(PaelsClawRelicId);
        RecordPaelsClawGoopyCardPlayedForTest(agg);
    }

    private static bool IsGoopyCard(CardModel card)
    {
        try
        {
            return card?.Enchantment is Goopy
                   || (card?.DeckVersion ?? card)?.Enchantment is Goopy;
        }
        catch
        {
            return false;
        }
    }

    private static bool RefreshPaelsClawSnapshotIfOwnedLocked(Player? player = null)
    {
        if (_currentRun == null || !CurrentRunMatchesLiveGameLocked()) return false;

        player ??= GetTrackedRunPlayerLocked();
        if (player?.Deck?.Cards == null
            || !IsTrackedPlayer(player)
            || !PlayerHasPaelsClaw(player))
        {
            return false;
        }

        var goopyCards = player.Deck.Cards
            .Where(card => card?.Enchantment is Goopy)
            .ToList();
        if (goopyCards.Count == 0) return false;

        var liveEnhancements = goopyCards.Sum(
            card => Math.Max(0, ((Goopy)card.Enchantment!).Amount - 1));
        var pendingEnhancements = _pendingCombat?.RelicAggregates.TryGetValue(
            PaelsClawRelicId,
            out var pending) == true
            ? Math.Max(0, pending.PaelsClawGoopyEnhancements)
            : 0;
        var committedLiveEnhancements = Math.Max(0, liveEnhancements - pendingEnhancements);

        var agg = GetOrCreateCurrentRunRelicAggregateLocked(PaelsClawRelicId);
        var goopyCardCount = Math.Max(agg.PaelsClawGoopyCards, goopyCards.Count);
        var enhancementCount = Math.Max(
            agg.PaelsClawGoopyEnhancements,
            committedLiveEnhancements);
        var changed = agg.PaelsClawGoopyCards != goopyCardCount
                      || agg.PaelsClawGoopyEnhancements != enhancementCount;

        agg.PaelsClawGoopyCards = goopyCardCount;
        agg.PaelsClawGoopyEnhancements = enhancementCount;
        return changed;
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

    private static void RecordVajraAttackPlayedIfOwnedLocked(CardModel card)
    {
        if (card.Type != CardType.Attack) return;

        var owner = card.Owner;
        if (owner == null || !IsTrackedPlayer(owner) || !PlayerHasVajra(owner)) return;

        var agg = GetOrCreateRelicAggregateForCurrentContextLocked(VajraRelicId);
        RecordVajraAttackPlayedForTest(agg);
    }

    private static void RecordVajraAttackHitIfOwnedLocked(CardModel card)
    {
        if (card.Type != CardType.Attack) return;

        var owner = card.Owner;
        if (owner == null || !IsTrackedPlayer(owner) || !PlayerHasVajra(owner)) return;

        var agg = GetOrCreateRelicAggregateLocked(VajraRelicId);
        RecordVajraAttackHitForTest(agg);
    }

    private static void RecordEmberTeaAttackPlayedIfActiveLocked(CardModel card)
    {
        if (card.Type != CardType.Attack) return;

        var owner = card.Owner;
        if (owner == null || !IsTrackedPlayer(owner)) return;
        if (_pendingCombat?.EmberTeaActivePlayers.Contains(owner) != true) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(EmberTeaRelicId);
        RecordEmberTeaAttackPlayedForTest(agg);
    }

    private static void RecordEmberTeaAttackHitIfActiveLocked(CardModel card)
    {
        if (card.Type != CardType.Attack) return;

        var owner = card.Owner;
        if (owner == null || !IsTrackedPlayer(owner)) return;
        if (_pendingCombat?.EmberTeaActivePlayers.Contains(owner) != true) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(EmberTeaRelicId);
        RecordEmberTeaAttackHitForTest(agg);
    }

    private static void RecordEmberTeaActiveTurnForTrackedPlayerLocked()
    {
        try
        {
            var player = GetTrackedRunPlayerLocked();
            if (player == null) return;
            RecordEmberTeaActiveTurnForPlayerLocked(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordEmberTeaActiveTurnForTrackedPlayerLocked failed: {e.Message}");
        }
    }

    private static void RecordEmberTeaActiveTurnForPlayerLocked(Player player)
    {
        if (_pendingCombat?.EmberTeaActivePlayers.Contains(player) != true) return;

        var turnNumber = player.PlayerCombatState?.TurnNumber ?? 0;
        if (turnNumber <= 0) return;
        if (_pendingCombat.EmberTeaActiveTurnCountedTurns.TryGetValue(
                player,
                out var recordedTurn)
            && recordedTurn == turnNumber)
        {
            return;
        }

        _pendingCombat.EmberTeaActiveTurnCountedTurns[player] = turnNumber;
        var agg = GetOrCreatePendingRelicAggregateLocked(EmberTeaRelicId);
        RecordEmberTeaActiveTurnForTest(agg);
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
        int nonUpgradedAttacks = 0;
        foreach (var deckCard in player.Deck.Cards)
        {
            if (IsMiniatureCannonUpgradedAttackCard(deckCard))
                upgradedAttacks++;
            else if (IsMiniatureCannonNonUpgradedAttackCard(deckCard))
                nonUpgradedAttacks++;
        }

        bool changed =
            agg.MiniatureCannonUpgradedAttacksInDeck != upgradedAttacks ||
            agg.MiniatureCannonNonUpgradedAttacksInDeck != nonUpgradedAttacks;
        agg.MiniatureCannonUpgradedAttacksInDeck = upgradedAttacks;
        agg.MiniatureCannonNonUpgradedAttacksInDeck = nonUpgradedAttacks;
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

    private static bool PlayerHasNutritiousSoup(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is NutritiousSoup);
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayerHasPaelsClaw(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is PaelsClaw);
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayerHasUnsettlingLamp(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is UnsettlingLamp);
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

    private static bool PlayerHasVajra(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is Vajra);
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayerHasRazorTooth(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is RazorTooth);
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayerHasStoneCracker(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is StoneCracker);
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayerHasWarHammer(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is WarHammer);
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayerHasTriBoomerang(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is TriBoomerang);
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayerHasPaperPhrog(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is PaperPhrog);
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayerHasRegalite(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is Regalite);
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayerHasIntimidatingHelmet(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is IntimidatingHelmet);
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayerHasDaughterOfTheWind(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is DaughterOfTheWind);
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayerHasSturdyClamp(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is SturdyClamp);
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayerHasBeatingRemnant(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is BeatingRemnant);
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayerHasRuinedHelmet(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is RuinedHelmet);
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

    private static bool PlayerHasDowsingRod(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is DowsingRod);
        }
        catch
        {
            return false;
        }
    }

    private static bool RefreshDowsingRoomsRemainingIfOwnedLocked(
        Player? player = null,
        Dowsing? dowsing = null)
    {
        if (_currentRun == null || !CurrentRunMatchesLiveGameLocked()) return false;

        player ??= GetTrackedRunPlayerLocked();
        if (player == null || !IsTrackedPlayer(player) || !PlayerHasDowsingRod(player))
            return false;

        var roomsEntered = GetLiveDowsingRoomsEnteredLocked(player, dowsing);
        if (!roomsEntered.HasValue) return false;

        var roomsRemaining = Math.Clamp(
            Dowsing.maxRooms - roomsEntered.Value,
            0,
            Dowsing.maxRooms);
        if (_currentRun.RelicAggregates.TryGetValue(DowsingRodRelicId, out var existing)
            && existing.DowsingQuestionRoomsRemaining == roomsRemaining)
            return false;

        var agg = GetOrCreateCurrentRunRelicAggregateLocked(DowsingRodRelicId);
        return RecordDowsingRoomsEnteredForTest(agg, roomsEntered.Value);
    }

    private static int? GetLiveDowsingRoomsEnteredLocked(
        Player player,
        Dowsing? dowsing = null)
    {
        dowsing ??= player.Deck?.Cards?.OfType<Dowsing>().FirstOrDefault();
        if (dowsing != null) return dowsing.RoomsEntered;

        // Dowsing transforms into Abundance when its fifth ? room resolves.
        // This reconstructs completion for runs first observed afterward.
        return player.Deck?.Cards?.Any(card => card is Abundance) == true
            ? Dowsing.maxRooms
            : null;
    }

    private static bool CurrentRunMatchesLiveGameLocked()
    {
        if (_currentRun == null) return false;

        try
        {
            var liveGameStartTime = RunManager.Instance._startTime;
            return liveGameStartTime == 0
                   || !_currentRun.GameStartTime.HasValue
                   || _currentRun.GameStartTime.Value == liveGameStartTime;
        }
        catch
        {
            return true;
        }
    }

    private static bool PlayerHasCursedPearl(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is CursedPearl);
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

            RecordPermafrostCombatForPlayerLocked(player);
            RecordLetterOpenerCombatForPlayerLocked(player);
            RecordTuningForkCombatForPlayerLocked(player);
            RecordCloakClaspCombatForPlayerLocked(player);
            RecordRippleBasinCombatForPlayerLocked(player);
            RecordReptileTrinketCombatForPlayerLocked(player);
            RecordArtOfWarCombatForPlayerLocked(player);
            RecordHappyFlowerCombatForPlayerLocked(player);
            RecordPendulumCombatForPlayerLocked(player);
            RecordSealOfGoldCombatForPlayerLocked(player);
            RecordNunchakuCombatForPlayerLocked(player);
            RecordIronClubCombatForPlayerLocked(player);
            RecordBrilliantScarfCombatForPlayerLocked(player);
            RecordNutritiousSoupCombatForPlayerLocked(player);
            RecordUnsettlingLampCombatForPlayerLocked(player);
            RecordMiniatureCannonCombatForPlayerLocked(player);
            RecordBookmarkCombatForPlayerLocked(player);
            RecordStoneCrackerCombatForPlayerLocked(player);
            RecordPaelsClawCombatForPlayerLocked(player);
            RecordPaelsEyeCombatForPlayerLocked(player);
            RecordPaperPhrogCombatForPlayerLocked(player);
            RecordRazorToothCombatForPlayerLocked(player);
            RecordWarHammerCombatForPlayerLocked(player);
            RecordTriBoomerangCombatForPlayerLocked(player);
            RecordRegaliteCombatForPlayerLocked(player);
            RecordIntimidatingHelmetCombatForPlayerLocked(player);
            RecordDaughterOfTheWindCombatForPlayerLocked(player);
            RecordSturdyClampCombatForPlayerLocked(player);
            RecordBeatingRemnantCombatForPlayerLocked(player);
            RecordRuinedHelmetCombatForPlayerLocked(player);
            RecordMummifiedHandCombatForPlayerLocked(player);
            RecordToastyMittensCombatForPlayerLocked(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordHeldCombatRelicBaselinesForTrackedPlayerLocked failed: {e.Message}");
        }
    }

    private static void RecordPermafrostCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasPermafrost(player)) return;
        if (!_pendingCombat.PermafrostCombatCountedPlayers.Add(player)) return;

        // Older run files predate the held-combat denominator. Permafrost can
        // trigger at most once per combat, so its existing trigger count is
        // the minimum number of historical combats we can reconstruct.
        if (_currentRun?.RelicAggregates.TryGetValue(PermafrostRelicId, out var committed) == true)
        {
            committed.PermafrostCombats = Math.Max(
                committed.PermafrostCombats,
                committed.Activations);
        }

        var agg = GetOrCreatePendingRelicAggregateLocked(PermafrostRelicId);
        RecordPermafrostCombatForTest(agg);
    }

    private static void RecordLetterOpenerCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasLetterOpener(player)) return;
        if (!_pendingCombat.LetterOpenerCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(LetterOpenerRelicId);
        RecordLetterOpenerCombatForTest(agg);
    }

    private static void RecordTuningForkCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasTuningFork(player)) return;
        if (!_pendingCombat.TuningForkCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(TuningForkRelicId);
        RecordTuningForkCombatForTest(agg);
    }

    private static void RecordLetterOpenerTurnForTrackedPlayerLocked()
    {
        try
        {
            var player = GetTrackedRunPlayerLocked();
            if (player == null) return;
            RecordLetterOpenerTurnForPlayerLocked(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordLetterOpenerTurnForTrackedPlayerLocked failed: {e.Message}");
        }
    }

    private static void RecordLetterOpenerTurnForPlayerLocked(Player player)
    {
        var turnNumber = player.PlayerCombatState?.TurnNumber ?? 0;
        RecordLetterOpenerTurnForPlayerLocked(player, turnNumber);
    }

    private static void RecordLetterOpenerTurnForPlayerLocked(Player player, int turnNumber)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasLetterOpener(player)) return;
        if (turnNumber <= 0) return;

        if (_pendingCombat.LetterOpenerTurnCountedTurns.TryGetValue(player, out var recordedTurn)
            && recordedTurn == turnNumber)
        {
            return;
        }

        _pendingCombat.LetterOpenerTurnCountedTurns[player] = turnNumber;
        RecordLetterOpenerCombatForPlayerLocked(player);

        var agg = GetOrCreatePendingRelicAggregateLocked(LetterOpenerRelicId);
        RecordLetterOpenerTurnForTest(agg);
    }

    private static void RecordTuningForkTurnForTrackedPlayerLocked()
    {
        try
        {
            var player = GetTrackedRunPlayerLocked();
            if (player == null) return;
            RecordTuningForkTurnForPlayerLocked(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordTuningForkTurnForTrackedPlayerLocked failed: {e.Message}");
        }
    }

    private static void RecordTuningForkTurnForPlayerLocked(Player player)
    {
        var turnNumber = player.PlayerCombatState?.TurnNumber ?? 0;
        RecordTuningForkTurnForPlayerLocked(player, turnNumber);
    }

    private static void RecordTuningForkTurnForPlayerLocked(Player player, int turnNumber)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasTuningFork(player)) return;
        if (turnNumber <= 0) return;

        if (_pendingCombat.TuningForkTurnCountedTurns.TryGetValue(player, out var recordedTurn)
            && recordedTurn == turnNumber)
        {
            return;
        }

        _pendingCombat.TuningForkTurnCountedTurns[player] = turnNumber;
        RecordTuningForkCombatForPlayerLocked(player);

        var agg = GetOrCreatePendingRelicAggregateLocked(TuningForkRelicId);
        RecordTuningForkTurnForTest(agg);
    }

    private static void RecordCloakClaspCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasCloakClasp(player)) return;
        if (!_pendingCombat.CloakClaspCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(CloakClaspRelicId);
        RecordCloakClaspCombatForTest(agg);
    }

    private static void RecordCloakClaspTurnForTrackedPlayerLocked()
    {
        try
        {
            var player = GetTrackedRunPlayerLocked();
            if (player == null) return;
            RecordCloakClaspTurnForPlayerLocked(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordCloakClaspTurnForTrackedPlayerLocked failed: {e.Message}");
        }
    }

    private static void RecordCloakClaspTurnForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasCloakClasp(player)) return;

        var turnNumber = player.PlayerCombatState?.TurnNumber ?? 0;
        if (turnNumber <= 0) return;
        if (_pendingCombat.CloakClaspTurnCountedTurns.TryGetValue(player, out var recordedTurn)
            && recordedTurn == turnNumber)
        {
            return;
        }

        _pendingCombat.CloakClaspTurnCountedTurns[player] = turnNumber;
        RecordCloakClaspCombatForPlayerLocked(player);

        var agg = GetOrCreatePendingRelicAggregateLocked(CloakClaspRelicId);
        RecordCloakClaspTurnForTest(agg);
    }

    private static void RecordRippleBasinCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasRippleBasin(player)) return;
        if (!_pendingCombat.RippleBasinCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(RippleBasinRelicId);
        RecordRippleBasinCombatForTest(agg);
    }

    private static void RecordRippleBasinTurnForTrackedPlayerLocked()
    {
        try
        {
            var player = GetTrackedRunPlayerLocked();
            if (player == null) return;
            RecordRippleBasinTurnForPlayerLocked(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordRippleBasinTurnForTrackedPlayerLocked failed: {e.Message}");
        }
    }

    private static void RecordRippleBasinTurnForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasRippleBasin(player)) return;

        var turnNumber = player.PlayerCombatState?.TurnNumber ?? 0;
        if (turnNumber <= 0) return;
        if (_pendingCombat.RippleBasinTurnCountedTurns.TryGetValue(player, out var recordedTurn)
            && recordedTurn == turnNumber)
        {
            return;
        }

        _pendingCombat.RippleBasinTurnCountedTurns[player] = turnNumber;
        RecordRippleBasinCombatForPlayerLocked(player);

        var agg = GetOrCreatePendingRelicAggregateLocked(RippleBasinRelicId);
        RecordRippleBasinTurnForTest(agg);
    }

    private static void RecordReptileTrinketCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasReptileTrinket(player)) return;
        if (!_pendingCombat.ReptileTrinketCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(ReptileTrinketRelicId);
        RecordReptileTrinketCombatForTest(agg);
    }

    private static void RecordReptileTrinketTurnForTrackedPlayerLocked()
    {
        try
        {
            var player = GetTrackedRunPlayerLocked();
            if (player == null) return;
            RecordReptileTrinketTurnForPlayerLocked(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordReptileTrinketTurnForTrackedPlayerLocked failed: {e.Message}");
        }
    }

    private static void RecordReptileTrinketTurnForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasReptileTrinket(player)) return;

        var turnNumber = player.PlayerCombatState?.TurnNumber ?? 0;
        if (turnNumber <= 0) return;
        if (_pendingCombat.ReptileTrinketTurnCountedTurns.TryGetValue(
                player,
                out var recordedTurn)
            && recordedTurn == turnNumber)
        {
            return;
        }

        _pendingCombat.ReptileTrinketTurnCountedTurns[player] = turnNumber;
        _pendingCombat.ReptileTrinketActivationsThisTurn[player] = 0;
        RecordReptileTrinketCombatForPlayerLocked(player);

        var agg = GetOrCreatePendingRelicAggregateLocked(ReptileTrinketRelicId);
        RecordReptileTrinketTurnForTest(agg);
    }

    private static void RecordBeatingRemnantCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasBeatingRemnant(player)) return;
        if (!_pendingCombat.BeatingRemnantCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(BeatingRemnantRelicId);
        RecordBeatingRemnantCombatForTest(agg);
    }

    private static void RecordBeatingRemnantTurnForTrackedPlayerLocked()
    {
        try
        {
            var player = GetTrackedRunPlayerLocked();
            if (player == null) return;
            RecordBeatingRemnantTurnForPlayerLocked(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"RecordBeatingRemnantTurnForTrackedPlayerLocked failed: {e.Message}");
        }
    }

    private static void RecordBeatingRemnantTurnForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasBeatingRemnant(player)) return;

        var turnNumber = player.PlayerCombatState?.TurnNumber ?? 0;
        if (turnNumber <= 0) return;
        if (_pendingCombat.BeatingRemnantTurnCountedTurns.TryGetValue(
                player,
                out var recordedTurn)
            && recordedTurn == turnNumber)
        {
            return;
        }

        _pendingCombat.BeatingRemnantTurnCountedTurns[player] = turnNumber;
        RecordBeatingRemnantCombatForPlayerLocked(player);

        var agg = GetOrCreatePendingRelicAggregateLocked(BeatingRemnantRelicId);
        RecordBeatingRemnantTurnForTest(agg);
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

    private static void RecordPaperPhrogCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasPaperPhrog(player)) return;
        if (!_pendingCombat.PaperPhrogCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(PaperPhrogRelicId);
        RecordPaperPhrogCombatForTest(agg);
    }

    private static void RecordPaperPhrogTurnForTrackedPlayerLocked()
    {
        try
        {
            var player = GetTrackedRunPlayerLocked();
            if (player == null) return;
            RecordPaperPhrogTurnForPlayerLocked(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordPaperPhrogTurnForTrackedPlayerLocked failed: {e.Message}");
        }
    }

    private static void RecordPaperPhrogTurnForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasPaperPhrog(player)) return;

        var turnNumber = player.PlayerCombatState?.TurnNumber ?? 0;
        if (turnNumber <= 0) return;
        if (_pendingCombat.PaperPhrogTurnCountedTurns.TryGetValue(player, out var recordedTurn)
            && recordedTurn == turnNumber)
        {
            return;
        }

        _pendingCombat.PaperPhrogTurnCountedTurns[player] = turnNumber;
        RecordPaperPhrogCombatForPlayerLocked(player);

        var agg = GetOrCreatePendingRelicAggregateLocked(PaperPhrogRelicId);
        RecordPaperPhrogTurnForTest(agg);
    }

    private static void RecordStoneCrackerCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasStoneCracker(player)) return;
        if (!_pendingCombat.StoneCrackerCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(StoneCrackerRelicId);
        RecordStoneCrackerCombatForTest(agg);
    }

    private static void RecordStoneCrackerTurnForTrackedPlayerLocked()
    {
        try
        {
            var player = GetTrackedRunPlayerLocked();
            if (player == null) return;
            RecordStoneCrackerTurnForPlayerLocked(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordStoneCrackerTurnForTrackedPlayerLocked failed: {e.Message}");
        }
    }

    private static void RecordStoneCrackerTurnForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasStoneCracker(player)) return;

        var turnNumber = player.PlayerCombatState?.TurnNumber ?? 0;
        if (turnNumber <= 0) return;
        if (_pendingCombat.StoneCrackerTurnCountedTurns.TryGetValue(
                player,
                out var recordedTurn)
            && recordedTurn == turnNumber)
        {
            return;
        }

        _pendingCombat.StoneCrackerTurnCountedTurns[player] = turnNumber;
        RecordStoneCrackerCombatForPlayerLocked(player);

        var agg = GetOrCreatePendingRelicAggregateLocked(StoneCrackerRelicId);
        RecordStoneCrackerTurnForTest(agg);
    }

    private static void RecordRazorToothCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasRazorTooth(player)) return;
        if (!_pendingCombat.RazorToothCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(RazorToothRelicId);
        RecordRazorToothCombatForTest(agg);
    }

    private static void RecordRazorToothTurnForTrackedPlayerLocked()
    {
        try
        {
            var player = GetTrackedRunPlayerLocked();
            if (player == null) return;
            RecordRazorToothTurnForPlayerLocked(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordRazorToothTurnForTrackedPlayerLocked failed: {e.Message}");
        }
    }

    private static void RecordRazorToothTurnForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasRazorTooth(player)) return;

        var turnNumber = player.PlayerCombatState?.TurnNumber ?? 0;
        if (turnNumber <= 0) return;
        if (_pendingCombat.RazorToothTurnCountedTurns.TryGetValue(player, out var recordedTurn)
            && recordedTurn == turnNumber)
        {
            return;
        }

        _pendingCombat.RazorToothTurnCountedTurns[player] = turnNumber;
        RecordRazorToothCombatForPlayerLocked(player);

        var agg = GetOrCreatePendingRelicAggregateLocked(RazorToothRelicId);
        RecordRazorToothTurnForTest(agg);
    }

    private static void RecordWarHammerCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasWarHammer(player)) return;
        if (!_pendingCombat.WarHammerCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(WarHammerRelicId);
        RecordWarHammerCombatForTest(agg);
    }

    private static void RecordWarHammerTurnForTrackedPlayerLocked()
    {
        try
        {
            var player = GetTrackedRunPlayerLocked();
            if (player == null) return;
            RecordWarHammerTurnForPlayerLocked(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordWarHammerTurnForTrackedPlayerLocked failed: {e.Message}");
        }
    }

    private static void RecordWarHammerTurnForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasWarHammer(player)) return;

        var turnNumber = player.PlayerCombatState?.TurnNumber ?? 0;
        if (turnNumber <= 0) return;
        if (_pendingCombat.WarHammerTurnCountedTurns.TryGetValue(player, out var recordedTurn)
            && recordedTurn == turnNumber)
        {
            return;
        }

        _pendingCombat.WarHammerTurnCountedTurns[player] = turnNumber;
        RecordWarHammerCombatForPlayerLocked(player);

        var agg = GetOrCreatePendingRelicAggregateLocked(WarHammerRelicId);
        RecordWarHammerTurnForTest(agg);
    }

    private static void RecordTriBoomerangCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasTriBoomerang(player)) return;
        if (!_pendingCombat.TriBoomerangCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(TriBoomerangRelicId);
        RecordTriBoomerangCombatForTest(agg);
    }

    private static void RecordRegaliteCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasRegalite(player)) return;
        if (!_pendingCombat.RegaliteCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(RegaliteRelicId);
        RecordRegaliteCombatForTest(agg);
    }

    private static void RecordRegaliteTurnForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasRegalite(player)) return;

        var turnNumber = player.PlayerCombatState?.TurnNumber ?? 0;
        if (turnNumber <= 0) return;
        if (_pendingCombat.RegaliteTurnCountedTurns.TryGetValue(player, out var recordedTurn)
            && recordedTurn == turnNumber)
        {
            return;
        }

        _pendingCombat.RegaliteTurnCountedTurns[player] = turnNumber;
        RecordRegaliteCombatForPlayerLocked(player);

        var agg = GetOrCreatePendingRelicAggregateLocked(RegaliteRelicId);
        RecordRegaliteTurnForTest(agg);
    }

    private static void RecordIntimidatingHelmetCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasIntimidatingHelmet(player)) return;
        if (!_pendingCombat.IntimidatingHelmetCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(IntimidatingHelmetRelicId);
        RecordIntimidatingHelmetCombatForTest(agg);
    }

    private static void RecordIntimidatingHelmetTurnForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasIntimidatingHelmet(player)) return;

        var turnNumber = player.PlayerCombatState?.TurnNumber ?? 0;
        if (turnNumber <= 0) return;
        if (_pendingCombat.IntimidatingHelmetTurnCountedTurns.TryGetValue(player, out var recordedTurn)
            && recordedTurn == turnNumber)
        {
            return;
        }

        _pendingCombat.IntimidatingHelmetTurnCountedTurns[player] = turnNumber;
        RecordIntimidatingHelmetCombatForPlayerLocked(player);

        var agg = GetOrCreatePendingRelicAggregateLocked(IntimidatingHelmetRelicId);
        RecordIntimidatingHelmetTurnForTest(agg);
    }

    private static void RecordDaughterOfTheWindCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasDaughterOfTheWind(player)) return;
        if (!_pendingCombat.DaughterOfTheWindCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(DaughterOfTheWindRelicId);
        RecordDaughterOfTheWindCombatForTest(agg);
    }

    private static void RecordDaughterOfTheWindTurnForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasDaughterOfTheWind(player)) return;

        var turnNumber = player.PlayerCombatState?.TurnNumber ?? 0;
        if (turnNumber <= 0) return;
        if (_pendingCombat.DaughterOfTheWindTurnCountedTurns.TryGetValue(
                player,
                out var recordedTurn)
            && recordedTurn == turnNumber)
        {
            return;
        }

        _pendingCombat.DaughterOfTheWindTurnCountedTurns[player] = turnNumber;
        RecordDaughterOfTheWindCombatForPlayerLocked(player);

        var agg = GetOrCreatePendingRelicAggregateLocked(DaughterOfTheWindRelicId);
        RecordDaughterOfTheWindTurnForTest(agg);
    }

    private static void RecordSturdyClampCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasSturdyClamp(player)) return;
        if (!_pendingCombat.SturdyClampCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(SturdyClampRelicId);
        RecordSturdyClampCombatForTest(agg);
    }

    private static void RecordRuinedHelmetCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasRuinedHelmet(player)) return;
        if (!_pendingCombat.RuinedHelmetCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(RuinedHelmetRelicId);
        RecordRuinedHelmetCombatForTest(agg);
    }

    private static void RecordMummifiedHandCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasMummifiedHand(player)) return;
        if (!_pendingCombat.MummifiedHandCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(MummifiedHandRelicId);
        RecordMummifiedHandCombatForTest(agg);
    }

    private static void RecordToastyMittensCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasToastyMittens(player)) return;
        if (!_pendingCombat.ToastyMittensCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(ToastyMittensRelicId);
        RecordToastyMittensForTest(
            agg,
            cardsExhausted: 0,
            strengthAdded: 0m,
            combats: 1);
    }

    private static void RecordMummifiedHandTurnForTrackedPlayerLocked()
    {
        try
        {
            var player = GetTrackedRunPlayerLocked();
            if (player == null) return;
            RecordMummifiedHandTurnForPlayerLocked(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordMummifiedHandTurnForTrackedPlayerLocked failed: {e.Message}");
        }
    }

    private static void RecordMummifiedHandTurnForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasMummifiedHand(player)) return;

        var turnNumber = player.PlayerCombatState?.TurnNumber ?? 0;
        if (turnNumber <= 0) return;
        if (_pendingCombat.MummifiedHandTurnCountedTurns.TryGetValue(player, out var recordedTurn)
            && recordedTurn == turnNumber)
        {
            return;
        }

        _pendingCombat.MummifiedHandTurnCountedTurns[player] = turnNumber;
        RecordMummifiedHandCombatForPlayerLocked(player);

        var agg = GetOrCreatePendingRelicAggregateLocked(MummifiedHandRelicId);
        RecordMummifiedHandTurnForTest(agg);
    }

    private static void RecordNutritiousSoupCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasNutritiousSoup(player)) return;
        if (!_pendingCombat.NutritiousSoupCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(NutritiousSoupRelicId);
        agg.Activations += 1;
    }

    private static void RecordUnsettlingLampCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasUnsettlingLamp(player)) return;
        if (!_pendingCombat.UnsettlingLampCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(UnsettlingLampRelicId);
        agg.Activations += 1;
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

    private static void RecordPendulumCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasPendulum(player)) return;
        if (!_pendingCombat.PendulumCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(PendulumRelicId);
        RecordPendulumCombatForTest(agg);
    }

    private static void RecordPendulumCombatEndChargeForTrackedPlayerLocked()
    {
        try
        {
            var player = GetTrackedRunPlayerLocked();
            if (player == null) return;
            RecordPendulumCombatEndChargeForPlayerLocked(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordPendulumCombatEndChargeForTrackedPlayerLocked failed: {e.Message}");
        }
    }

    private static void RecordPendulumCombatEndChargeForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!TryGetPendulum(player, out var pendulum) || pendulum == null) return;
        if (!_pendingCombat.PendulumCombatEndChargeRecordedPlayers.Add(player)) return;

        RecordPendulumCombatForPlayerLocked(player);
        var agg = GetOrCreatePendingRelicAggregateLocked(PendulumRelicId);
        RecordPendulumCombatEndChargeForTest(agg, pendulum.TurnsSeen);
    }

    private static void RecordArtOfWarCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasArtOfWar(player)) return;
        if (!_pendingCombat.ArtOfWarCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(ArtOfWarRelicId);
        RecordArtOfWarCombatForTest(agg);
    }

    private static void RecordArtOfWarTurnForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasArtOfWar(player)) return;

        var turnNumber = player.PlayerCombatState?.TurnNumber ?? 0;
        if (turnNumber <= 0) return;
        if (_pendingCombat.ArtOfWarTurnCountedTurns.TryGetValue(player, out var recordedTurn)
            && recordedTurn == turnNumber)
        {
            return;
        }

        _pendingCombat.ArtOfWarTurnCountedTurns[player] = turnNumber;
        _pendingCombat.ArtOfWarEnergyAddedThisTurn[player] = 0;
        RecordArtOfWarCombatForPlayerLocked(player);

        var agg = GetOrCreatePendingRelicAggregateLocked(ArtOfWarRelicId);
        RecordArtOfWarTurnForTest(agg);
    }

    private static void RecordSealOfGoldCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasSealOfGold(player)) return;
        if (!_pendingCombat.SealOfGoldCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(SealOfGoldRelicId);
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

    private static void RecordIronClubCombatForTrackedPlayerLocked()
    {
        try
        {
            var player = GetTrackedRunPlayerLocked();
            if (player == null) return;
            RecordIronClubCombatForPlayerLocked(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordIronClubCombatForTrackedPlayerLocked failed: {e.Message}");
        }
    }

    private static void RecordIronClubCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasIronClub(player)) return;
        if (!_pendingCombat.IronClubCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(IronClubRelicId);
        agg.IronClubCombats += 1;
    }

    private static void RecordIronClubCombatEndChargeForTrackedPlayerLocked()
    {
        try
        {
            var player = GetTrackedRunPlayerLocked();
            if (player == null) return;
            RecordIronClubCombatEndChargeForPlayerLocked(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordIronClubCombatEndChargeForTrackedPlayerLocked failed: {e.Message}");
        }
    }

    private static void RecordIronClubCombatEndChargeForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!TryGetIronClub(player, out var ironClub) || ironClub == null) return;
        if (!_pendingCombat.IronClubCombatEndChargeRecordedPlayers.Add(player)) return;

        RecordIronClubCombatForPlayerLocked(player);
        var agg = GetOrCreatePendingRelicAggregateLocked(IronClubRelicId);
        RecordIronClubCombatEndChargeForTest(agg, IronClubCharge(ironClub));
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

    private static void RecordPaelsClawCombatForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasPaelsClaw(player)) return;
        if (!_pendingCombat.PaelsClawCombatCountedPlayers.Add(player)) return;

        var agg = GetOrCreatePendingRelicAggregateLocked(PaelsClawRelicId);
        RecordPaelsClawCombatForTest(agg);
    }

    private static void RecordPaelsClawTurnForTrackedPlayerLocked()
    {
        try
        {
            var player = GetTrackedRunPlayerLocked();
            if (player == null) return;
            RecordPaelsClawTurnForPlayerLocked(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordPaelsClawTurnForTrackedPlayerLocked failed: {e.Message}");
        }
    }

    private static void RecordPaelsClawTurnForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasPaelsClaw(player)) return;

        var turnNumber = player.PlayerCombatState?.TurnNumber ?? 0;
        if (turnNumber <= 0) return;
        if (_pendingCombat.PaelsClawTurnCountedTurns.TryGetValue(player, out var recordedTurn)
            && recordedTurn == turnNumber)
        {
            return;
        }

        _pendingCombat.PaelsClawTurnCountedTurns[player] = turnNumber;
        RecordPaelsClawCombatForPlayerLocked(player);

        var agg = GetOrCreatePendingRelicAggregateLocked(PaelsClawRelicId);
        RecordPaelsClawTurnForTest(agg);
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

    private static void RecordBrilliantScarfTurnForTrackedPlayerLocked()
    {
        try
        {
            var player = GetTrackedRunPlayerLocked();
            if (player == null) return;
            RecordBrilliantScarfTurnForPlayerLocked(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordBrilliantScarfTurnForTrackedPlayerLocked failed: {e.Message}");
        }
    }

    private static void RecordBrilliantScarfTurnForPlayerLocked(Player player)
    {
        if (_pendingCombat == null) return;
        if (!PlayerHasBrilliantScarf(player)) return;

        var turnNumber = player.PlayerCombatState?.TurnNumber ?? 0;
        if (turnNumber <= 0) return;
        if (_pendingCombat.BrilliantScarfTurnCountedTurns.TryGetValue(
                player,
                out var recordedTurn)
            && recordedTurn == turnNumber)
        {
            return;
        }

        _pendingCombat.BrilliantScarfTurnCountedTurns[player] = turnNumber;
        RecordBrilliantScarfCombatForPlayerLocked(player);

        var agg = GetOrCreatePendingRelicAggregateLocked(BrilliantScarfRelicId);
        agg.DiscountTurns += 1;
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

    private static bool PlayerHasMummifiedHand(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is MummifiedHand);
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayerHasToastyMittens(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is ToastyMittens);
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

    private static bool PlayerHasPendulum(Player player)
    {
        return TryGetPendulum(player, out _);
    }

    private static bool TryGetPendulum(Player player, out Pendulum? pendulum)
    {
        pendulum = null;

        try
        {
            pendulum = player?.Relics?.OfType<Pendulum>().FirstOrDefault();
            return pendulum != null;
        }
        catch
        {
            pendulum = null;
            return false;
        }
    }

    private static bool PlayerHasArtOfWar(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is ArtOfWar);
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayerHasSealOfGold(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is SealOfGold);
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayerHasRippleBasin(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is RippleBasin);
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayerHasCloakClasp(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is CloakClasp);
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayerHasPermafrost(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is Permafrost);
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayerHasReptileTrinket(Player player)
    {
        try
        {
            return player.Relics.Any(r => r is ReptileTrinket);
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayerHasTuningFork(Player player)
    {
        return TryGetTuningFork(player, out _);
    }

    private static bool TryGetTuningFork(Player player, out TuningFork? tuningFork)
    {
        tuningFork = null;

        try
        {
            tuningFork = player?.Relics?.OfType<TuningFork>().FirstOrDefault();
            return tuningFork != null;
        }
        catch
        {
            tuningFork = null;
            return false;
        }
    }

    private static int TuningForkCharge(TuningFork relic)
    {
        var threshold = TuningForkCardsPerActivation(relic);
        if (threshold <= 0) return 0;
        return Math.Max(0, relic.SkillsPlayed) % threshold;
    }

    private static int TuningForkCardsPerActivation(TuningFork relic)
    {
        try
        {
            return Math.Max(1, relic.DynamicVars.Cards.IntValue);
        }
        catch
        {
            return 10;
        }
    }

    private static bool TryGetKunai(Player player, out Kunai? kunai)
    {
        kunai = null;

        try
        {
            kunai = player?.Relics?.OfType<Kunai>().FirstOrDefault();
            return kunai != null;
        }
        catch
        {
            kunai = null;
            return false;
        }
    }

    private static int KunaiCharge(Kunai relic)
    {
        var threshold = KunaiCardsPerActivation(relic);
        if (threshold <= 0) return 0;
        return Math.Max(0, relic.AttacksPlayedThisTurn) % threshold;
    }

    private static int KunaiCardsPerActivation(Kunai relic)
    {
        try
        {
            return Math.Max(1, relic.DynamicVars.Cards.IntValue);
        }
        catch
        {
            return 3;
        }
    }

    private static int CurrentDexterity(Creature creature)
    {
        try
        {
            return creature.GetPower<DexterityPower>()?.Amount ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private static bool TryGetKusarigama(Player player, out Kusarigama? kusarigama)
    {
        kusarigama = null;

        try
        {
            kusarigama = player?.Relics?.OfType<Kusarigama>().FirstOrDefault();
            return kusarigama != null;
        }
        catch
        {
            kusarigama = null;
            return false;
        }
    }

    private static int KusarigamaCharge(Kusarigama relic)
    {
        var threshold = KusarigamaCardsPerActivation(relic);
        if (threshold <= 0) return 0;
        return Math.Max(0, relic.AttacksPlayedThisTurn) % threshold;
    }

    private static int KusarigamaCardsPerActivation(Kusarigama relic)
    {
        try
        {
            return Math.Max(1, relic.DynamicVars.Cards.IntValue);
        }
        catch
        {
            return 3;
        }
    }

    private static bool TryGetOrnamentalFan(Player player, out OrnamentalFan? ornamentalFan)
    {
        ornamentalFan = null;

        try
        {
            ornamentalFan = player?.Relics?.OfType<OrnamentalFan>().FirstOrDefault();
            return ornamentalFan != null;
        }
        catch
        {
            ornamentalFan = null;
            return false;
        }
    }

    private static int OrnamentalFanCharge(OrnamentalFan relic)
    {
        var threshold = OrnamentalFanCardsPerActivation(relic);
        if (threshold <= 0) return 0;
        return Math.Max(0, relic.AttacksPlayedThisTurn) % threshold;
    }

    private static int OrnamentalFanCardsPerActivation(OrnamentalFan relic)
    {
        try
        {
            return Math.Max(1, relic.DynamicVars.Cards.IntValue);
        }
        catch
        {
            return 3;
        }
    }

    private static bool TryGetShuriken(Player player, out Shuriken? shuriken)
    {
        shuriken = null;

        try
        {
            shuriken = player?.Relics?.OfType<Shuriken>().FirstOrDefault();
            return shuriken != null;
        }
        catch
        {
            shuriken = null;
            return false;
        }
    }

    private static int ShurikenCharge(Shuriken relic)
    {
        var threshold = ShurikenCardsPerActivation(relic);
        if (threshold <= 0) return 0;
        return Math.Max(0, relic.AttacksPlayedThisTurn) % threshold;
    }

    private static int ShurikenCardsPerActivation(Shuriken relic)
    {
        try
        {
            return Math.Max(1, relic.DynamicVars.Cards.IntValue);
        }
        catch
        {
            return 3;
        }
    }

    private static decimal CurrentStrength(Creature creature)
    {
        try
        {
            return creature.GetPower<StrengthPower>()?.Amount ?? 0m;
        }
        catch
        {
            return 0m;
        }
    }

    private static bool PlayerHasNunchaku(Player player)
    {
        return TryGetNunchaku(player, out _);
    }

    private static bool PlayerHasLetterOpener(Player player)
    {
        return TryGetLetterOpener(player, out _);
    }

    private static bool TryGetLetterOpener(Player player, out LetterOpener? letterOpener)
    {
        letterOpener = null;

        try
        {
            letterOpener = player?.Relics?.OfType<LetterOpener>().FirstOrDefault();
            return letterOpener != null;
        }
        catch
        {
            letterOpener = null;
            return false;
        }
    }

    private static int LetterOpenerCharge(LetterOpener relic)
    {
        var threshold = LetterOpenerActivationThreshold(relic);
        if (threshold <= 0) return 0;
        return Math.Max(0, relic.SkillsPlayedThisTurn) % threshold;
    }

    private static int LetterOpenerActivationThreshold(LetterOpener relic)
    {
        try
        {
            return Math.Max(1, relic.DynamicVars.Cards.IntValue);
        }
        catch
        {
            return 3;
        }
    }

    private static bool TryGetPenNib(Player player, out PenNib? penNib)
    {
        penNib = null;

        try
        {
            penNib = player?.Relics?.OfType<PenNib>().FirstOrDefault();
            return penNib != null;
        }
        catch
        {
            penNib = null;
            return false;
        }
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

    private static bool PlayerHasIronClub(Player player)
    {
        return TryGetIronClub(player, out _);
    }

    private static bool TryGetIronClub(Player player, out IronClub? ironClub)
    {
        ironClub = null;

        try
        {
            ironClub = player?.Relics?.OfType<IronClub>().FirstOrDefault();
            return ironClub != null;
        }
        catch
        {
            ironClub = null;
            return false;
        }
    }

    private static int IronClubCharge(IronClub relic)
    {
        var cardsPerTrigger = IronClubCardsPerTrigger(relic);
        if (cardsPerTrigger <= 0) return 0;
        return Math.Max(0, relic.CardsPlayed) % cardsPerTrigger;
    }

    private static int IronClubCardsPerTrigger(IronClub relic)
    {
        try
        {
            return Math.Max(1, relic.DynamicVars["Cards"].IntValue);
        }
        catch
        {
            return 4;
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

    private static bool IsNutritiousSoupEnchantedStrikeCard(CardModel? card)
    {
        try
        {
            if (card == null) return false;
            var canonical = Canonical(card);
            return canonical.Rarity == CardRarity.Basic
                && canonical.Tags.Contains(CardTag.Strike)
                && (canonical.Enchantment is TezcatarasEmber || card.Enchantment is TezcatarasEmber);
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
            return card.IsUpgraded;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsMiniatureCannonNonUpgradedAttackCard(CardModel? card)
    {
        try
        {
            if (card == null || card.Type != CardType.Attack) return false;
            return !card.IsUpgraded;
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

    private static void MergeWingedBootsDestinations(RelicAggregate target, RelicAggregate source)
    {
        target.WingedBootsDestinations ??= new List<WingedBootsDestinationAggregate>();
        if (source.WingedBootsDestinations == null) return;

        foreach (var destination in source.WingedBootsDestinations
                     .Where(entry => entry != null
                                     && entry.UseNumber is >= 1 and <= 3
                                     && !string.IsNullOrWhiteSpace(entry.Destination))
                     .OrderBy(entry => entry.UseNumber))
        {
            if (target.WingedBootsDestinations.Any(entry => entry.UseNumber == destination.UseNumber))
                continue;

            target.WingedBootsDestinations.Add(new WingedBootsDestinationAggregate
            {
                UseNumber = destination.UseNumber,
                Destination = destination.Destination,
            });
        }

        target.WingedBootsDestinations.Sort((left, right) => left.UseNumber.CompareTo(right.UseNumber));
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

    private static void MergeWarHammerUpgradedCardInstanceIdsInto(
        RelicAggregate target,
        RelicAggregate source)
        => AddUniqueWarHammerCardInstanceIds(
            target,
            source.WarHammerUpgradedCardInstanceIds);

    private static void AddUniqueWarHammerCardInstanceIds(
        RelicAggregate target,
        IEnumerable<string>? instanceIds)
    {
        if (instanceIds == null) return;

        target.WarHammerUpgradedCardInstanceIds ??= new List<string>();
        var seen = new HashSet<string>(
            target.WarHammerUpgradedCardInstanceIds,
            StringComparer.Ordinal);
        foreach (var instanceId in instanceIds)
        {
            if (string.IsNullOrWhiteSpace(instanceId) || !seen.Add(instanceId)) continue;
            target.WarHammerUpgradedCardInstanceIds.Add(instanceId);
        }
    }

    private static void MergeSharpEnchantedCardsInto(RelicAggregate target, RelicAggregate source)
    {
        if (source.SharpEnchantedCards == null || source.SharpEnchantedCards.Count == 0)
            return;

        target.SharpEnchantedCards ??= new List<string>();
        target.SharpEnchantedCards.AddRange(
            source.SharpEnchantedCards.Where(card => !string.IsNullOrWhiteSpace(card)));
    }

    private static void MergeTriBoomerangInstinctCardsInto(
        RelicAggregate target,
        RelicAggregate source)
        => AddUniqueTriBoomerangInstinctCards(
            target,
            source.TriBoomerangInstinctCards);

    private static void AddUniqueTriBoomerangInstinctCards(
        RelicAggregate target,
        IEnumerable<RelicEnchantedCardAggregate>? cards)
    {
        if (cards == null) return;

        target.TriBoomerangInstinctCards ??= new List<RelicEnchantedCardAggregate>();
        var byInstanceId = target.TriBoomerangInstinctCards
            .Where(card =>
                card != null
                && !string.IsNullOrWhiteSpace(card.CardInstanceId))
            .GroupBy(card => card.CardInstanceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var card in cards)
        {
            if (card == null || string.IsNullOrWhiteSpace(card.CardInstanceId))
                continue;

            if (byInstanceId.TryGetValue(card.CardInstanceId, out var existing))
            {
                if (string.IsNullOrWhiteSpace(existing.DisplayName)
                    && !string.IsNullOrWhiteSpace(card.DisplayName))
                {
                    existing.DisplayName = card.DisplayName;
                }
                continue;
            }

            var added = new RelicEnchantedCardAggregate
            {
                CardInstanceId = card.CardInstanceId,
                DisplayName = card.DisplayName ?? "",
            };
            target.TriBoomerangInstinctCards.Add(added);
            byInstanceId[added.CardInstanceId] = added;
        }
    }

    private static void MergeCardRewardScreens(RelicAggregate target, RelicAggregate source)
    {
        if (source.CardRewardScreens == null || source.CardRewardScreens.Count == 0) return;

        foreach (var screen in source.CardRewardScreens)
            RecordSilverCrucibleRewardForTest(target, screen);
    }

    private static void MergeOrreryRewards(RelicAggregate target, RelicAggregate source)
    {
        if (source.OrreryRewards == null || source.OrreryRewards.Count == 0) return;

        foreach (var reward in source.OrreryRewards)
            RecordOrreryRewardForTest(target, reward);
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

    private static void MergeRelicCardsReturned(RelicAggregate target, RelicAggregate source)
    {
        if (source.CardsReturned == null || source.CardsReturned.Count == 0) return;

        target.CardsReturned ??= new List<RelicCardReturnAggregate>();
        foreach (var card in source.CardsReturned)
        {
            if (card == null || string.IsNullOrWhiteSpace(card.CardId)) continue;

            target.CardsReturned.Add(new RelicCardReturnAggregate
            {
                CardId = card.CardId,
                DisplayName = card.DisplayName ?? "",
                UpgradeLevel = Math.Max(0, card.UpgradeLevel),
            });
        }
    }

    private static void MergeRelicsGranted(
        Dictionary<string, RelicGrantedAggregate> target,
        Dictionary<string, RelicGrantedAggregate>? source)
    {
        if (source == null || source.Count == 0) return;

        foreach (var kvp in source)
        {
            var relic = kvp.Value;
            if (relic.Count <= 0) continue;
            var relicId = string.IsNullOrWhiteSpace(relic.RelicId) ? kvp.Key : relic.RelicId;
            AddRelicGranted(target, relicId, relic.DisplayName, relic.Count);
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

    private static void MergeRelicMaxHpActivations(RelicAggregate target, RelicAggregate source)
    {
        if (source.MaxHpActivations == null || source.MaxHpActivations.Count == 0) return;

        target.MaxHpActivations ??= new List<RelicMaxHpActivationAggregate>();
        foreach (var activation in source.MaxHpActivations)
        {
            if (activation == null) continue;
            target.MaxHpActivations.Add(new RelicMaxHpActivationAggregate
            {
                StartingHp = Math.Max(0m, activation.StartingHp),
                ResultingHp = Math.Max(0m, activation.ResultingHp),
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

    private static void AddRelicGranted(
        Dictionary<string, RelicGrantedAggregate> relics,
        string relicId,
        string displayName,
        int count)
    {
        if (count <= 0 || string.IsNullOrWhiteSpace(relicId)) return;

        if (!relics.TryGetValue(relicId, out var agg))
        {
            agg = new RelicGrantedAggregate
            {
                RelicId = relicId,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? FormatRelicIdForDisplay(relicId) : displayName,
            };
            relics[relicId] = agg;
        }

        if (string.IsNullOrWhiteSpace(agg.RelicId))
            agg.RelicId = relicId;
        if (string.IsNullOrWhiteSpace(agg.DisplayName))
            agg.DisplayName = string.IsNullOrWhiteSpace(displayName) ? FormatRelicIdForDisplay(relicId) : displayName;

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

    private static string GetRelicDisplayName(RelicModel relic)
    {
        try
        {
            var title = relic.Title.GetFormattedText();
            if (!string.IsNullOrWhiteSpace(title))
                return title;
        }
        catch
        {
        }

        try
        {
            var title = relic.Title.GetRawText();
            if (!string.IsNullOrWhiteSpace(title))
                return title;
        }
        catch
        {
        }

        return FormatRelicIdForDisplay(relic.Id.ToString());
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
                if (!ShouldTrackCardStatsDuringCombatLocked()) return;

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
                if (!ShouldTrackCardStatsDuringCombatLocked()) return false;

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

    private static string GetRewardCardIdForStats(CardModel card)
    {
        try { return card.Id.ToString(); }
        catch { return ""; }
    }

    private static string GetRewardCardDisplayNameForStats(CardModel card)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(card.Title))
                return card.Title;

            return FormatCardIdForDisplay(card.Id.ToString());
        }
        catch
        {
            try { return FormatCardIdForDisplay(card.Id.ToString()); }
            catch { return "Unknown card"; }
        }
    }

    private static int GetRewardCardUpgradeLevelForStats(CardModel card)
    {
        try { return Math.Max(0, card.CurrentUpgradeLevel); }
        catch { return 0; }
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

    /// <summary>
    /// True only when <paramref name="card"/> is the exact object currently
    /// stored in its owner's permanent deck. A combat clone whose DeckVersion
    /// points at that object is intentionally false.
    /// </summary>
    internal static bool IsExactPermanentDeckCard(CardModel card)
    {
        try
        {
            return IsExactPermanentDeckCardForTest(card, card?.Owner?.Deck?.Cards);
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsExactPermanentDeckCardForTest(
        CardModel? card,
        IEnumerable<CardModel>? permanentDeckCards)
    {
        if (card == null || permanentDeckCards == null) return false;
        return permanentDeckCards.Any(deckCard => ReferenceEquals(deckCard, card));
    }

    public static void RecordUpgrade(CardModel card, bool isPermanentDeckUpgrade)
    {
        lock (_lock)
        {
            // These attribution paths intentionally observe their own source
            // mechanics, including temporary combat-copy upgrades.
            RecordSandCastleCardUpgradedLocked(card);
            RecordWhetstoneCardUpgradedLocked(card);
            RecordWarPaintCardUpgradedLocked(card);
            RecordFragrantMushroomCardUpgradedLocked(card);
            RecordFishingRodCardUpgradedLocked(card);
            RecordWarHammerCardUpgradedLocked(card);
            RecordDrainPowerCardUpgradedLocked(card);

            // Card lineage is different: only mutation of the exact object in
            // the permanent deck counts. Do not canonicalize a combat clone
            // through DeckVersion and make its temporary upgrade look lasting.
            if (!isPermanentDeckUpgrade) return;

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

    private static void RecordFragrantMushroomCardUpgradedLocked(CardModel card)
    {
        try
        {
            if (card == null) return;
            var owner = card.Owner;
            if (owner == null) return;
            if (!_pendingFragrantMushroomPickups.TryGetValue(owner, out var pending)) return;

            pending.UpgradedCards.Add(GetCardDisplayNameForStats(card));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordFragrantMushroomCardUpgradedLocked failed: {e.Message}");
        }
    }

    private static void RecordFishingRodCardUpgradedLocked(CardModel card)
    {
        try
        {
            if (card == null) return;
            var owner = card.Owner;
            if (owner == null) return;
            if (!_pendingFishingRodUpgrades.TryGetValue(owner, out var pending)) return;

            pending.UpgradedCards.Add(GetCardDisplayNameForStats(card));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordFishingRodCardUpgradedLocked failed: {e.Message}");
        }
    }

    private static void RecordWarHammerCardUpgradedLocked(CardModel card)
    {
        try
        {
            if (card?.Owner is not Player owner) return;
            if (!_pendingWarHammerActivations.TryGetValue(owner, out var pending)) return;

            pending.UpgradedCards.Add(GetCardDisplayNameForStats(card));
            if (TryGetInstanceId(card, out var instanceId))
                pending.UpgradedCardInstanceIds.Add(instanceId);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RecordWarHammerCardUpgradedLocked failed: {e.Message}");
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

            var initialUpgradeLevel = _currentRun.Aggregates.TryGetValue(
                instanceId,
                out var agg)
                ? agg.InitialUpgradeLevel
                : 0;
            return FilterPermanentUpgradeEvents(
                _currentRun.Events.Where(e =>
                    e.Type == "card_upgraded" && e.CardId == instanceId),
                initialUpgradeLevel);
        }
    }

    /// <summary>
    /// Filters legacy upgrade events that could only have come from the old
    /// combat-clone canonicalization bug. A real permanent upgrade advances
    /// the deck card's level; temporary clone events repeat or lower the deck
    /// card's recorded level.
    /// </summary>
    internal static IReadOnlyList<CardEvent> FilterPermanentUpgradeEvents(
        IEnumerable<CardEvent>? events,
        int initialUpgradeLevel)
    {
        if (events == null) return Array.Empty<CardEvent>();

        var result = new List<CardEvent>();
        var lastPermanentLevel = Math.Max(0, initialUpgradeLevel);
        foreach (var cardEvent in events)
        {
            if (!cardEvent.UpgradeLevel.HasValue) continue;
            if (cardEvent.UpgradeLevel.Value <= lastPermanentLevel) continue;

            result.Add(cardEvent);
            lastPermanentLevel = cardEvent.UpgradeLevel.Value;
        }

        return result;
    }

    private static void RecordCardDrawn(CardDrawnEntry entry)
    {
        lock (_lock)
        {
            if (!ShouldTrackCardStatsDuringCombatLocked()) return;

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
            if (!ShouldTrackCardStatsDuringCombatLocked()) return;

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
            if (ShouldTrackCardStatsDuringCombatLocked())
            {
                var instanceId = GetOrAssignInstanceId(card);
                var agg = GetOrCreateAggregate(_pendingCombat, instanceId);
                agg.TimesDiscarded++;
            }

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
            // Relic tracking is independent from the Card Stats preference.
            // CardCmd.Exhaust emits this entry only after the card reaches the
            // exhaust pile, so this is Toasty Mittens' confirmed outcome.
            ObserveToastyMittensCardExhaustedLocked(exhaustedCard);

            if (!ShouldTrackCardStatsDuringCombatLocked()) return;

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
            if (!ShouldTrackCardStatsDuringCombatLocked()) return;

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
            if (!ShouldTrackCardStatsDuringCombatLocked()) return;

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
            if (!ShouldTrackCardStatsDuringCombatLocked()) return;

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
                if (!ShouldTrackCardStatsDuringCombatLocked()) return;

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
                if (!ShouldTrackCardStatsDuringCombatLocked()) return;

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
            if (!ShouldTrackCardStatsDuringCombatLocked()) return;

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
            if (!ShouldTrackCardStatsDuringCombatLocked()) return;

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
            if (!ShouldTrackCardStatsDuringCombatLocked()) return;

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
            if (!ShouldTrackCardStatsDuringCombatLocked()) return;

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

    private static void RecordUnsettlingLampPowerReceivedLocked(
        PowerModel power,
        Creature target,
        Creature? applier,
        decimal receivedAmount)
    {
        if (_pendingCombat == null || receivedAmount <= 0m) return;

        var pending = TakePendingUnsettlingLampDebuff(power, target, applier);
        if (pending == null) return;

        var amount = Math.Min(pending.ExtraAmount, receivedAmount);
        if (amount <= 0m) return;

        var agg = GetOrCreateRelicAggregateLocked(UnsettlingLampRelicId);
        RecordUnsettlingLampDebuffApplied(agg, power, amount);
    }

    private static PendingUnsettlingLampDebuff? TakePendingUnsettlingLampDebuff(
        PowerModel power,
        Creature target,
        Creature? applier)
    {
        for (var i = _pendingUnsettlingLampDebuffs.Count - 1; i >= 0; i--)
        {
            var pending = _pendingUnsettlingLampDebuffs[i];
            if (!IsSamePower(pending.Power, power)) continue;
            if (!ReferenceEquals(pending.Target, target)) continue;
            if (!ReferenceEquals(pending.Applier, applier)) continue;

            _pendingUnsettlingLampDebuffs.RemoveAt(i);
            return pending;
        }

        for (var i = _pendingUnsettlingLampDebuffs.Count - 1; i >= 0; i--)
        {
            var pending = _pendingUnsettlingLampDebuffs[i];
            if (!IsSamePower(pending.Power, power)) continue;
            if (!ReferenceEquals(pending.Target, target)) continue;

            _pendingUnsettlingLampDebuffs.RemoveAt(i);
            return pending;
        }

        return null;
    }

    private static void RecordUnsettlingLampDebuffApplied(RelicAggregate agg, PowerModel power, decimal amount)
    {
        RecordUnsettlingLampDebuffApplied(
            agg,
            power.Id.ToString(),
            GetPowerDisplayName(power),
            GetPowerIconPath(power),
            amount);
    }

    private static void RecordUnsettlingLampDebuffApplied(
        RelicAggregate agg,
        string effectId,
        string displayName,
        string? iconPath,
        decimal amount)
    {
        if (agg == null || amount <= 0m) return;

        var effect = GetOrCreateAppliedEffect(agg, effectId, displayName, iconPath);
        effect.TimesApplied++;
        effect.TotalAmountApplied += amount;

        var roundedAmount = (int)Math.Round(amount, MidpointRounding.AwayFromZero);
        if (IsVulnerableEffect(effectId, displayName))
            agg.VulnerableApplied += roundedAmount;
        else if (IsWeakEffect(effectId, displayName))
            agg.WeakApplied += roundedAmount;
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
            RecordRazorToothUpgradedCardDrawnLocked(card);
            if (!ShouldTrackCardStatsDuringCombatLocked()) return;

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
            if (!ShouldTrackCardStatsDuringCombatLocked()) return;

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
                if (!ShouldTrackCardStatsDuringCombatLocked()) return;

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
                if (target != null && entry.Amount > 0m)
                    RecordUnsettlingLampPowerReceivedLocked(entry.Power, target, entry.Applier, entry.Amount);
                if (target != null)
                    RecordToastyMittensStrengthReceivedLocked(
                        entry.Power,
                        target,
                        entry.Applier,
                        entry.Amount);

                if (!ShouldTrackCardStatsDuringCombatLocked()) return;

                if (entry.Amount > 0m
                    && entry.Power is DanseMacabrePower danseMacabre
                    && danseMacabre.Owner?.Player != null
                    && IsTrackedPlayer(danseMacabre.Owner.Player))
                {
                    RecordDanseMacabrePowerActiveForPlayerLocked(
                        danseMacabre,
                        danseMacabre.Owner.Player);
                }

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

                if (target?.IsPlayer == true
                    && entry.Amount > 0m
                    && entry.Power is FreeAttackPower
                    && causingPlay.Card is Unrelenting)
                {
                    var powerAgg = GetOrCreatePowerAggregate(
                        _pendingCombat.MetaStats,
                        entry.Power.Id.ToString(),
                        GetPowerDisplayName(entry.Power));
                    AccumulateFreeAttackGrant(
                        powerAgg,
                        Math.Max(0, (int)Math.Floor(entry.Amount)));
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
            if (!ShouldTrackCardStatsDuringCombatLocked()) return;
            if (!creature.IsPlayer) return;
            _pendingPlayerBlockClearAmount = Math.Max(0, creature.Block);
            _pendingPlayerBlockClearArmed = _pendingPlayerBlockClearAmount > 0;
        }
    }

    public static void NotePlayerBlockClearPrevented(Creature creature)
    {
        lock (_lock)
        {
            if (!ShouldTrackCardStatsDuringCombatLocked()) return;
            if (!creature.IsPlayer) return;
            ClearPendingPlayerBlockClearLocked();
        }
    }

    public static void NotePlayerBlockCleared(Creature creature)
    {
        lock (_lock)
        {
            if (!ShouldTrackCardStatsDuringCombatLocked()) return;
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
            if (!entry.Receiver.IsPlayer)
            {
                RecordMiniatureCannonUpgradedAttackHitIfOwnedLocked(entry.CardSource!);
                RecordVajraAttackHitIfOwnedLocked(entry.CardSource!);
                RecordEmberTeaAttackHitIfActiveLocked(entry.CardSource!);
            }

            if (!ShouldTrackCardStatsDuringCombatLocked()) return;

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
        target.PotionsGained += source.PotionsGained;
        target.CommonPotionsGained += source.CommonPotionsGained;
        target.UncommonPotionsGained += source.UncommonPotionsGained;
        target.RarePotionsGained += source.RarePotionsGained;
        target.PotionsSkipped += source.PotionsSkipped;
        target.JackColorlessCardsAdded += source.JackColorlessCardsAdded;
        target.JackUncommonCardsAdded += source.JackUncommonCardsAdded;
        target.JackRareCardsAdded += source.JackRareCardsAdded;
        target.JackAttacksAdded += source.JackAttacksAdded;
        target.JackSkillsAdded += source.JackSkillsAdded;
        target.JackPowersAdded += source.JackPowersAdded;
        target.JackAddedCardCostTotal += source.JackAddedCardCostTotal;
        target.DiscoveryCardsPicked += source.DiscoveryCardsPicked;
        target.DiscoveryCommonCardsPicked += source.DiscoveryCommonCardsPicked;
        target.DiscoveryUncommonCardsPicked += source.DiscoveryUncommonCardsPicked;
        target.DiscoveryRareCardsPicked += source.DiscoveryRareCardsPicked;
        target.DiscoveryAttacksPicked += source.DiscoveryAttacksPicked;
        target.DiscoverySkillsPicked += source.DiscoverySkillsPicked;
        target.DiscoveryPowersPicked += source.DiscoveryPowersPicked;
        target.DiscoveryEnergyDiscountTotal += source.DiscoveryEnergyDiscountTotal;
        target.DrainPowerCardsUpgraded += source.DrainPowerCardsUpgraded;
        target.DrainPowerTurnsInDeck += source.DrainPowerTurnsInDeck;
        target.DrainPowerUpgradedCardPlays += source.DrainPowerUpgradedCardPlays;
        target.DebtTriggers += source.DebtTriggers;
        target.DebtGoldLost += source.DebtGoldLost;
        target.DebtGoldLossBlocked += source.DebtGoldLossBlocked;
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
        target.TotalMaxHpLost += source.TotalMaxHpLost;
        target.TimesCardsDrawn += source.TimesCardsDrawn;
        target.TimesCardsDrawAttempted += source.TimesCardsDrawAttempted;
        target.TimesCardsDrawBlocked += source.TimesCardsDrawBlocked;
        target.TimesSummonedToHand += source.TimesSummonedToHand;
        target.TotalOstyHpAttackBonus += source.TotalOstyHpAttackBonus;
        target.TimesOstyHpAttackBonusApplied += source.TimesOstyHpAttackBonusApplied;
        target.TimesOstySummoned += source.TimesOstySummoned;
        target.TotalOstyHpSummoned += source.TotalOstyHpSummoned;
        target.SoulsAddedToDrawPile += source.SoulsAddedToDrawPile;
        target.SoulsAddedToHand += source.SoulsAddedToHand;
        target.SoulsAddedToDiscardPile += source.SoulsAddedToDiscardPile;
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
        target.ExtraBlockGainedFromUnmovablePower += source.ExtraBlockGainedFromUnmovablePower;
        MergePowerAggregatesInto(target.PowerAggregates, source.PowerAggregates);
    }

    private static void MergePowerAggregatesInto(
        Dictionary<string, PowerAggregate> target,
        Dictionary<string, PowerAggregate>? source)
    {
        if (source == null) return;

        foreach (var (powerId, sourceAgg) in source)
        {
            if (!target.TryGetValue(powerId, out var targetAgg))
            {
                targetAgg = new PowerAggregate
                {
                    PowerId = sourceAgg.PowerId,
                    DisplayName = sourceAgg.DisplayName,
                };
                target[powerId] = targetAgg;
            }

            if (string.IsNullOrWhiteSpace(targetAgg.PowerId))
                targetAgg.PowerId = sourceAgg.PowerId;
            if (string.IsNullOrWhiteSpace(targetAgg.DisplayName)
                && !string.IsNullOrWhiteSpace(sourceAgg.DisplayName))
                targetAgg.DisplayName = sourceAgg.DisplayName;

            targetAgg.AttacksCopied += sourceAgg.AttacksCopied;
            targetAgg.CommonAttacksCopied += sourceAgg.CommonAttacksCopied;
            targetAgg.UncommonAttacksCopied += sourceAgg.UncommonAttacksCopied;
            targetAgg.RareAttacksCopied += sourceAgg.RareAttacksCopied;
            targetAgg.TurnsActive += sourceAgg.TurnsActive;
            targetAgg.CombatsActive += sourceAgg.CombatsActive;
            targetAgg.TimesTriggered += sourceAgg.TimesTriggered;
            targetAgg.BlockGained += sourceAgg.BlockGained;
            targetAgg.FreeAttackChargesGranted += sourceAgg.FreeAttackChargesGranted;
            targetAgg.FreeAttackChargesUsed += sourceAgg.FreeAttackChargesUsed;
            targetAgg.FreeAttackZeroEnergySavingsUses += sourceAgg.FreeAttackZeroEnergySavingsUses;
            targetAgg.FreeAttackEnergySaved += sourceAgg.FreeAttackEnergySaved;
            targetAgg.FreeAttackBasicAttacksDiscounted += sourceAgg.FreeAttackBasicAttacksDiscounted;
            targetAgg.FreeAttackCommonAttacksDiscounted += sourceAgg.FreeAttackCommonAttacksDiscounted;
            targetAgg.FreeAttackUncommonAttacksDiscounted += sourceAgg.FreeAttackUncommonAttacksDiscounted;
            targetAgg.FreeAttackRareAttacksDiscounted += sourceAgg.FreeAttackRareAttacksDiscounted;
        }
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

    private static AppliedEffectAggregate GetOrCreateAppliedEffect(RelicAggregate agg, PowerModel power)
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

    private static AppliedEffectAggregate GetOrCreateAppliedEffect(
        RelicAggregate agg,
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

    private static bool IsSamePower(PowerModel left, PowerModel right)
    {
        return ReferenceEquals(left, right)
            || string.Equals(left.Id.ToString(), right.Id.ToString(), StringComparison.Ordinal);
    }

    internal static bool IsVulnerableEffect(string effectId, string? displayName)
    {
        return effectId.Contains("VULNERABLE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(displayName, "Vulnerable", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsWeakEffect(string effectId, string? displayName)
    {
        return effectId.Contains("WEAK", StringComparison.OrdinalIgnoreCase)
            || string.Equals(displayName, "Weak", StringComparison.OrdinalIgnoreCase);
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

internal sealed class PendingFresnelLensReward
{
    public int NimbleCards { get; private set; }

    public static PendingFresnelLensReward FromReward(CardReward? reward)
    {
        var result = new PendingFresnelLensReward();
        if (reward == null) return result;

        foreach (var option in reward._cards)
        {
            try
            {
                if (option?.Card?.Enchantment is Nimble
                    && option.ModifyingRelics.Any(relic => relic is FresnelLens))
                    result.NimbleCards += 1;
            }
            catch
            {
            }
        }

        return result;
    }
}

internal sealed class PendingSilverCrucibleReward
{
    public PendingSilverCrucibleReward(int screenNumber, int? floor)
    {
        ScreenNumber = screenNumber;
        Floor = floor;
    }

    public int ScreenNumber { get; }
    public int? Floor { get; }
    public bool SelectionOpened { get; set; }
    public List<PendingSilverCrucibleCard> Cards { get; } = new();
}

internal sealed class PendingOrreryReward
{
    public PendingOrreryReward(int rewardNumber, int? floor, Player player)
    {
        RewardNumber = rewardNumber;
        Floor = floor;
        Player = player;
    }

    public int RewardNumber { get; }
    public int? Floor { get; }
    public Player Player { get; }
    public List<string> OfferedCardIds { get; } = new();
    public HashSet<CardModel>? DeckBeforeSelection { get; set; }
}

internal sealed class PendingSilverCrucibleCard
{
    public required CardCreationResult Result { get; init; }
    public string CardId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public int UpgradeLevel { get; init; }
}

internal sealed class PendingJugglingCopyWindow
{
    public required Player Player { get; init; }
    public string PowerId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string TriggerCardId { get; init; } = "";
    public int RemainingAttempts { get; set; }
}

internal sealed class PendingFreeAttackUse
{
    public required FreeAttackPower Power { get; init; }
    public required Player Player { get; init; }
    public required CardModel Card { get; init; }
    public int StartingPowerAmount { get; init; }
    public decimal OfferedEnergySavings { get; init; }
    public bool IsAutoPlay { get; init; }
}

internal sealed class PendingDanseMacabreBlockAttribution
{
    public required PendingCombat PendingCombat { get; init; }
    public required Creature Owner { get; init; }
    public required string PowerId { get; init; }
    public required string DisplayName { get; init; }
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
    public HashSet<Player> PermafrostCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> LetterOpenerCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Creature> PantographActivationCountedCreatures { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> LetterOpenerTurnCountedTurns { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> LetterOpenerTurnEndChargeRecordedTurns { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> TuningForkCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> TuningForkTurnCountedTurns { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> TuningForkTurnEndChargeRecordedTurns { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> CloakClaspCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> CloakClaspTurnCountedTurns { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> EmberTeaActivePlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> EmberTeaActiveTurnCountedTurns { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> RippleBasinCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> RippleBasinTurnCountedTurns { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> ReptileTrinketCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> ReptileTrinketTurnCountedTurns { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> ReptileTrinketActivationsThisTurn { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> BeatingRemnantCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> BeatingRemnantTurnCountedTurns { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> ArtOfWarCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> ArtOfWarTurnCountedTurns { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> ArtOfWarEnergyAddedThisTurn { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<OrbModel> CrackedCoreStartingOrbs { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> HappyFlowerCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> PendulumCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> PendulumCombatEndChargeRecordedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> SealOfGoldCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> NunchakuCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> NunchakuCombatEndChargeRecordedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> IronClubCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> IronClubCombatEndChargeRecordedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> BrilliantScarfCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> BrilliantScarfTurnCountedTurns { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> SturdyClampCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> SturdyClampTurnCountedTurns { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> RuinedHelmetCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<RuinedHelmet, decimal> PendingRuinedHelmetStrengthGains { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> MummifiedHandCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> MummifiedHandTurnCountedTurns { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> ToastyMittensCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> JugglingPowerCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> JugglingPowerTurnCountedTurns { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, PendingJugglingCopyWindow> PendingJugglingCopyWindows { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> DanseMacabrePowerCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> DanseMacabrePowerTurnCountedTurns { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<CardModel, decimal> FreeAttackEnergySavingsByCard { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<string, int> DrainPowerTurnCountedTurns { get; }
        = new(StringComparer.Ordinal);
    public Dictionary<CardModel, HashSet<string>> DrainPowerSourcesByUpgradedCard { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> NutritiousSoupCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> UnsettlingLampCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> MiniatureCannonCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> BookmarkCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> PaelsClawCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> PaelsClawTurnCountedTurns { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> PaperPhrogCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> PaperPhrogTurnCountedTurns { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> StoneCrackerCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> StoneCrackerTurnCountedTurns { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<CardModel> StoneCrackerUpgradedCards { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> RazorToothCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> RazorToothTurnCountedTurns { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<CardModel> RazorToothUpgradedCards { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> WarHammerCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> WarHammerTurnCountedTurns { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> TriBoomerangCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> RegaliteCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> RegaliteTurnCountedTurns { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> IntimidatingHelmetCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> IntimidatingHelmetTurnCountedTurns { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> DaughterOfTheWindCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> DaughterOfTheWindTurnCountedTurns { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> PaelsEyeCombatCountedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> PaelsEyeActivationStartedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> LanternFirstTurnExcessRecordedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> VeryHotCocoaFirstTurnExcessRecordedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> CandelabraSecondTurnExcessRecordedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> ChandelierThirdTurnExcessRecordedPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> PenNibTurnEndChargeRecordedTurns { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> KunaiTurnEndChargeRecordedTurns { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> KusarigamaTurnEndChargeRecordedTurns { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> OrnamentalFanTurnEndChargeRecordedTurns { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> ShurikenTurnEndChargeRecordedTurns { get; }
        = new(ReferenceEqualityComparer.Instance);
    public HashSet<Player> GamblingChipDiscardAttributionPlayers { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> CentennialPuzzleDrawsRemaining { get; }
        = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Player, int> IronClubDrawsRemaining { get; }
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

internal sealed class PendingToastyMittensActivation
{
    public PendingToastyMittensActivation(
        ToastyMittens relic,
        Player player,
        PendingToastyMittensActivation? previous)
    {
        Relic = relic;
        Player = player;
        Previous = previous;
    }

    public ToastyMittens Relic { get; }
    public Player Player { get; }
    public PendingToastyMittensActivation? Previous { get; }
    public bool CardExhausted { get; set; }
    public decimal? LastStrengthReceived { get; set; }
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

internal sealed class PendingUnsettlingLampDebuff
{
    public required PowerModel Power { get; init; }
    public required Creature Target { get; init; }
    public required Creature Applier { get; init; }
    public CardModel? CardSource { get; init; }
    public required decimal ExtraAmount { get; init; }
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

internal sealed class PendingFragrantMushroomPickup
{
    public List<string> UpgradedCards { get; } = new();
}

internal sealed class PendingFishingRodUpgrade
{
    public List<string> UpgradedCards { get; } = new();
}

internal sealed class PendingWarHammerActivation
{
    public List<string> UpgradedCards { get; } = new();
    public HashSet<string> UpgradedCardInstanceIds { get; } = new(StringComparer.Ordinal);
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
