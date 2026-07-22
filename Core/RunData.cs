using System;
using System.Collections.Generic;

namespace SpireLens.Core;

/// <summary>
/// Serialized shape of one run's stats. Written to disk as JSON.
///
/// The on-disk shape evolves additively: new persisted fields default safely
/// on missing-field deserialization, so older files continue to load without
/// an explicit version number. The historic pooled shape (aggregates keyed by
/// card definition id rather than per-instance id, written before
/// <see cref="InstanceNumbersByDef"/> and <see cref="DefCounters"/> existed)
/// is detected structurally by <see cref="RunStorage"/>; everything else is
/// the current per-instance shape.
/// </summary>
public class RunData
{
    public string RunId { get; set; } = "";
    public string StartedAt { get; set; } = "";  // ISO-8601 UTC
    public string UpdatedAt { get; set; } = "";
    public string? EndedAt { get; set; }
    public string? Character { get; set; }
    public int? Ascension { get; set; }
    public int? FloorReached { get; set; }
    public string Outcome { get; set; } = "in_progress";  // in_progress | win | loss | abandoned

    /// <summary>
    /// The game's own run identifier — Unix seconds of run start, sourced from
    /// <c>RunManager._startTime</c>. The game saves its run history to
    /// <c>{StartTime}.run</c>, so this field is the correlation key for M5
    /// (Run History integration): user clicks a past run in the game, the
    /// game knows its start_time, we find our file where <see cref="GameStartTime"/>
    /// matches. Our file name stays a GUID for identity independence.
    ///
    /// Null for runs created before this field was added or runs that observed
    /// combat before RunStarted fired (edge case — mod loaded mid-run).
    /// </summary>
    public long? GameStartTime { get; set; }

    /// <summary>Per-card aggregates. Keyed by card definition ID (e.g. "STRIKE_KIN"). Upgraded and base versions share a key for now; upgrade breakout is a future issue.</summary>
    public Dictionary<string, CardAggregate> Aggregates { get; set; } = new();

    /// <summary>Full per-event log for later deep analysis. One entry per card play + one entry per damage-received-from-card.</summary>
    public List<CardEvent> Events { get; set; } = new();

    /// <summary>Per-relic stat aggregates. Keyed by relic id (e.g. "RELIC.BAG_OF_MARBLES").</summary>
    public Dictionary<string, RelicAggregate> RelicAggregates { get; set; } = new();

    /// <summary>Per-enemy stat aggregates. Keyed by monster id (e.g. "MONSTER.HAUNTED_SHIP").</summary>
    public Dictionary<string, EnemyAggregate> EnemyAggregates { get; set; } = new();

    /// <summary>
    /// Run-level facts surfaced on related cards when that card is the natural
    /// place to inspect the mechanic, but the value is not caused by that
    /// specific card instance.
    /// </summary>
    public RunMetaStats MetaStats { get; set; } = new();

    /// <summary>
    /// Snapshot of per-instance number assignments, serialized so that hot
    /// reload mid-run can resume with the same numbers instead of losing
    /// the CardModel-ref → number mapping (which only lives in memory on
    /// the soon-to-be-orphaned Core assembly).
    ///
    /// Format: <c>{def_id → [number, number, ...]}</c> where the list is
    /// ordered by each card's current deck-rank among cards of the same
    /// def_id. Example: if the deck has 4 Strikes with instance numbers
    /// #1, #2, #4, #5 (because #3 was Smith'd), this stores
    /// <c>{"STRIKE": [1, 2, 4, 5]}</c>.
    ///
    /// On resume: walk the live deck, compute each card's
    /// (def_id, rank-among-same-def), look up the number, repopulate
    /// <c>RunTracker._instanceNumbers</c>. Removal-safe because rank is
    /// relative to the CURRENT deck composition.
    ///
    /// Presence of this field (or <see cref="DefCounters"/>) at the top
    /// level of an on-disk JSON file is also the structural marker that
    /// the file uses the per-instance shape. Files predating per-instance
    /// identity lack both fields entirely.
    /// </summary>
    public Dictionary<string, List<int>> InstanceNumbersByDef { get; set; } = new();

    /// <summary>
    /// Snapshot of the monotonic per-def counters. Preserves the invariant
    /// that numbers never get reused across hot reload — if the saved state
    /// had Strike #1..#5 and #3 was removed, <c>DefCounters["STRIKE"] == 5</c>,
    /// so the next added Strike becomes #6 (not a recycled #3).
    /// </summary>
    public Dictionary<string, int> DefCounters { get; set; } = new();

    // (Intentionally no PendingCombat field — pending-combat persistence
    // was explored and rejected. Rule-of-use: F5 between combats, not
    // during. Rest/shop/event/reward rooms are all safe because
    // _pendingCombat is null outside of active combat. See git history
    // on 2026-04-20 for the full PendingCombatSnapshot approach if we
    // ever decide to re-enable mid-combat persistence.)
}

/// <summary>Aggregated per-card attribution stats for this run.</summary>
public class CardAggregate
{
    // Number of combats that started while this physical card was in the
    // player's permanent deck. Useful denominator for dud cards that may
    // rarely be drawn or played.
    public int CombatsInDeck { get; set; }

    public int Plays { get; set; }

    // M1: Attack attribution. Null/zero for non-attack cards.
    public int TotalIntended { get; set; }   // damage the card tried to deal (pre-block, including overkill)
    public int TotalBlocked { get; set; }    // damage absorbed by target block
    public int TotalOverkill { get; set; }   // damage past target HP (wasted)
    public int TotalEffective { get; set; }  // damage that actually moved HP (observed unblocked damage)
    public int Kills { get; set; }           // times the card landed a killing blow

    // M3a: Energy spent. Sum of CardPlay.Resources.EnergySpent across every
    // play of this card instance. Uses EnergySpent (actual energy paid) not
    // EnergyValue (listed cost) so cost modifiers like Mummified Hand show
    // up correctly — a Strike played from Hand at 0 cost counts 0 here.
    // Average is derived on the display side via TotalEnergySpent / Plays.
    public int TotalEnergySpent { get; set; }

    // M3j: Energy generated directly by this card while it is resolving.
    // Sourced from PlayerCombatState.GainEnergy, attributed to the currently-
    // resolving card play. Tracks the ACTUAL amount added to the pool after
    // clamping / prevention, not the raw text on the card, so "gain 1" under
    // a no-energy-gain effect correctly records 0.
    public int TotalEnergyGenerated { get; set; }

    // M3k: Regent star spend / generation mirrors the energy fields above,
    // but for the character's separate star resource.
    public int TotalStarsSpent { get; set; }
    public int TotalStarsGenerated { get; set; }

    // M3l: Forge granted directly by this card while it is resolving.
    // Stored as decimal because forge values are sourced from the game's
    // dynamic vars / command path, which are decimal-backed even when most
    // current cards use whole numbers.
    public decimal TotalForgeGenerated { get; set; }

    // Potion procurement outcomes caused by this card. Alchemize is the
    // first card using these fields: gained counts only successful observed
    // procure results, rarity buckets use the potion actually returned by
    // the command, and skipped counts failed procure results.
    public int PotionsGained { get; set; }
    public int CommonPotionsGained { get; set; }
    public int UncommonPotionsGained { get; set; }
    public int RarePotionsGained { get; set; }
    public int PotionsSkipped { get; set; }

    // Debt's end-of-turn curse effect. The intended amount comes from the
    // card's Gold dynamic var; actual loss is observed from the owner's gold
    // balance before/after the callback. Any unaffordable remainder is kept
    // separately so a trigger at zero gold is still visible and explainable.
    public int DebtTriggers { get; set; }
    public int DebtGoldLost { get; set; }
    public int DebtGoldLossBlocked { get; set; }

    // M2a: Block gained (how much block this card contributed over the run,
    // summed across plays). M2b extends this with absorbed/wasted splits
    // using an ordered provenance ledger for the player's block pool.
    public int TotalBlockGained { get; set; }
    public int TotalBlockEffective { get; set; }
    public int TotalBlockWasted { get; set; }

    // M3c: Draw count. Every time this card instance gets drawn — at
    // turn start or via card-effect draw ("draw 2 cards"). Shows up-
    // stream of plays: you can't play a card without drawing it first,
    // so TimesDrawn >= Plays always. Useful for efficiency signals like
    // "drew 10 times, played 4" (you've been stuck with dead draws).
    public int TimesDrawn { get; set; }

    // M3e: Discarded count. Every time this card goes to the discard
    // pile — end-of-turn (still in hand), mid-combat discard effects,
    // etc. Meaningful signal when high relative to plays ("I keep
    // discarding this without playing it").
    public int TimesDiscarded { get; set; }

    // M3f: Pile-top placement counts. Tracks when THIS card gets placed
    // on top of the draw pile from specific sources. Useful for cards
    // that manipulate draw order (Shining Strike's self-retain after
    // play, Finisher effects putting attacks back on top, etc.).
    //   FromHand: card was in hand, got moved to top of draw (retain-style)
    //   FromDiscard: card was in discard pile, got moved to top of draw
    //     ("from graveyard" in player parlance)
    public int TimesPlacedOnTopFromHand { get; set; }
    public int TimesPlacedOnTopFromDiscard { get; set; }

    // M3g: Exhaust attribution. When THIS card's play caused OTHER cards
    // to be exhausted. Covers Havoc (exhausts the auto-played card), Fiend
    // Fire (exhausts the hand), Second Wind (exhausts non-attacks), etc.
    // Self-exhaust (card exhausts itself after play) is NOT counted here
    // — different signal, different meaning.
    public int TimesExhaustedOtherCards { get; set; }

    // M3g2: How often THIS card itself got exhausted, regardless of cause.
    // Useful for exhaust-tag cards, ephemeral generated cards, and effects
    // that consume a card from hand/discard. Shown only when > 0 on the
    // full tooltip.
    public int TimesExhausted { get; set; }

    // M3h: Player HP loss from playing this card. Tracks Ironclad-style
    // self-damage (Hemokinesis, Offering, Combust tick, etc.). Uses the
    // damage's UnblockedDamage, which is POST-reduction — so Tungsten Rod
    // / buffer effects naturally show up as less HP loss. That's the
    // truth of "what did this card actually cost me", not what its
    // listed damage says.
    public int TotalHpLost { get; set; }

    // Maximum HP actually lost from playing this card. Kept separate from
    // TotalHpLost because reducing max HP is a permanent run cost even when
    // the player's current HP does not move. Brightest Flame is the first
    // card using this field.
    public int TotalMaxHpLost { get; set; }

    // M3i: Draw attribution. When THIS card's play causes OTHER cards to
    // be drawn. Signal for draw-enabler cards (Prepared, Coolheaded,
    // Acrobatics etc. depending on the character). Excludes turn-start
    // auto-draw via the game's FromHandDraw flag.
    public int TimesCardsDrawn { get; set; }

    // M3i1: Total card draw attempts caused by THIS card's play, regardless
    // of whether each draw actually succeeded. Lets the tooltip show the gap
    // between "tried to draw X" and "actually drew Y" without caring whether
    // the miss came from No Draw, full hand fallback, or another prevention
    // path.
    public int TimesCardsDrawAttempted { get; set; }

    // M3i2: Blocked draw attribution. When THIS card's play ATTEMPTS to draw
    // cards but a draw-prevention hook vetoes the attempt (Battle Trance,
    // future "can't draw" effects, etc.). Counts blocked cards separately
    // from successful draws so draw cards don't silently look like they drew
    // zero when the game explicitly prevented them.
    public int TimesCardsDrawBlocked { get; set; }

    // M3i3: Categorized blocked-draw reasons for THIS card's draw attempts.
    // Keeps the card-side gap explainable without caring about the exact
    // blocker implementation: No Draw, hand full, or an "other" bucket when
    // the game prevented the draw for some reason we didn't categorize yet.
    public Dictionary<string, BlockedDrawReasonAggregate> BlockedDrawReasons { get; set; } = new();

    // M3m: Successful self-summons into Hand. Counts actual arrivals in
    // Hand, not mere attempts, so hand-full redirects to Discard stay out
    // of this number.
    public int TimesSummonedToHand { get; set; }

    // M3n: Unleash-specific Osty HP scaling. Unleash adds Osty's current HP
    // to its attack payload; these fields track that contribution separately
    // from the normal observed damage totals.
    public int TotalOstyHpAttackBonus { get; set; }
    public int TimesOstyHpAttackBonusApplied { get; set; }

    // M3o: Card-sourced successful Osty summons. These are direct card
    // contributions: how often this card summoned/revived Osty and how much
    // Osty HP the command actually added.
    public int TimesOstySummoned { get; set; }
    public decimal TotalOstyHpSummoned { get; set; }

    // M3p: Extra plays caused by the game's replay/multi-play series. Total
    // Plays already includes these; this field tracks the subset where the
    // finished CardPlay was not the first play in its series.
    public int TimesReplayExtraPlanned { get; set; }
    public Dictionary<string, ReplayExtraPlayReasonAggregate> ReplayExtraPlayPlannedReasons { get; set; } = new();
    public int TimesReplayExtraPlayed { get; set; }
    public Dictionary<string, ReplayExtraPlayReasonAggregate> ReplayExtraPlayReasons { get; set; } = new();
    public int TimesReplayAttackNoDamage { get; set; }
    public Dictionary<string, ReplayExtraPlayReasonAggregate> ReplayAttackNoDamageReasons { get; set; } = new();

    // M4a: Effect / power application summary for this specific card
    // instance. First pass tracks ONLY that the card caused a power/effect
    // to be applied, not what the downstream effect later did. Keyed by the
    // game's power id (e.g. "POWER.NECROBINDER_TRIGGER"), with localized
    // display text cached for tooltip rendering.
    public Dictionary<string, AppliedEffectAggregate> AppliedEffects { get; set; } = new();

    // M3d: Per-instance lineage (when the card entered the deck and at
    // what upgrade level). Lets us distinguish between "card arrived
    // upgraded" (bought from a shop pre-upgraded, event reward, etc.) and
    // "card upgraded during the run" (rest site / Armaments etc.).
    //
    //   FloorAdded:         CardModel.FloorAddedToDeck snapshot at first
    //                       observation. Null = starting deck (the game
    //                       leaves this null for the initial 5).
    //   InitialUpgradeLevel: CurrentUpgradeLevel at first observation.
    //                       If > 0, the card arrived already upgraded.
    //
    // Subsequent upgrades are recorded in the Events log as "card_upgraded"
    // entries with Floor + UpgradeLevel, so the tooltip can render a full
    // lineage like "Arrived: floor 3, +1" followed by "Upgraded: floor 6 → +2".
    public int? FloorAdded { get; set; }
    public int InitialUpgradeLevel { get; set; }

    // M5a: Removal tracking. When a card is removed from the deck (Smith,
    // event, curse-dispose, etc.), we mark the aggregate rather than
    // delete it — so the user can browse "what did I remove this run and
    // how was it performing?" via the deck-view injection.
    //   Removed: true once the card left the permanent deck
    //   RemovedAtFloor: floor the removal happened on
    //   RemovedSnapshot: the card's full serializable state at removal —
    //     upgrade level, enchantment, props, etc. Used on resume to
    //     reconstruct a CardModel ref matching the removed card's state
    //     (via CardModel.FromSerializable) so the deck-view injection
    //     renders it correctly post-reload.
    public bool Removed { get; set; }
    public int? RemovedAtFloor { get; set; }
    public MegaCrit.Sts2.Core.Saves.Runs.SerializableCard? RemovedSnapshot { get; set; }

    // M3c: Draw count attribution. Null until M3c.
}

public class RunMetaStats
{
    public decimal TotalOstyHpSummoned { get; set; }
    public decimal TotalOstyDamageAbsorbed { get; set; }
    public decimal ExtraBlockGainedFromUnmovablePower { get; set; }
}

public class EnemyAggregate
{
    public string EnemyId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int DamageInstances { get; set; }
    public int DamageAttempted { get; set; }
    public int DamageDealt { get; set; }
    public int DamageBlocked { get; set; }
    public int StatusCardsAdded { get; set; }
    public int StatusCardsAddedToHand { get; set; }
    public int StatusCardsAddedToDraw { get; set; }
    public int StatusCardsAddedToDiscard { get; set; }
    public int StatusCardsAddedToDeck { get; set; }
    public Dictionary<string, EnemyStatusCardAggregate> StatusCardsById { get; set; } = new();
}

public class EnemyStatusCardAggregate
{
    public string CardId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>
/// First-pass effect/power tracking credited back to a card instance.
/// Keeps enough display metadata for tooltip rendering without forcing the
/// UI to re-query live game state for historical runs.
/// </summary>
public class AppliedEffectAggregate
{
    public string EffectId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? IconPath { get; set; }
    public int TimesApplied { get; set; }
    public decimal TotalAmountApplied { get; set; }
    public int TimesBlockedByArtifact { get; set; }
    public decimal TotalAmountBlockedByArtifact { get; set; }
    public decimal TotalTriggeredEffectiveDamage { get; set; }
    public decimal TotalTriggeredOverkill { get; set; }
    public int TotalTriggeredCardsDrawBlocked { get; set; }
}

public class BlockedDrawReasonAggregate
{
    public string ReasonId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int Count { get; set; }
}

public class HealingLostReasonAggregate
{
    public string ReasonId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public decimal Amount { get; set; }
}

public class ReplayExtraPlayReasonAggregate
{
    public string ReasonId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>
/// Aggregated stats for a single relic across this run.
/// Fields are shared across relics; each relic uses only the fields relevant to it.
/// </summary>
public class RelicAggregate
{
    // Total times this relic's tracked effect activated.
    // Used by room/trigger-based relics such as Meal Ticket and Tuning Fork.
    public int Activations { get; set; }

    // Total enemies across all combats this run that had a debuff applied
    // by this relic at combat start.
    public int EnemiesAffected { get; set; }

    // Total Vulnerable stacks applied by this relic across all combats.
    // Used by Bag of Marbles (1 Vulnerable per enemy).
    public int VulnerableApplied { get; set; }

    // Total Weak stacks applied by this relic across all combats.
    // Used by Red Mask (1 Weak per enemy at combat start).
    public int WeakApplied { get; set; }

    // Dynamic effect/power tracking credited to this relic. Used by Unsettling
    // Lamp for debuffs beyond the fixed Vulnerable/Weak rows.
    public Dictionary<string, AppliedEffectAggregate> AppliedEffects { get; set; } = new();

    // Total additional cards drawn by this relic across the run.
    // Used by Pocketwatch (draws 3 extra cards when 3 or fewer cards were
    // played last turn), Gremlin Horn (draws after enemy death), Pendulum
    // (draws every N turns), and Booming Conch (draws extra cards at Elite
    // combat start).
    public int AdditionalCardsDrawn { get; set; }

    // Total block gained from this relic across all combats.
    // Used by Orichalcum, Permafrost, The Abacus, Bone Flute, Cloak Clasp,
    // Anchor, Horn Cleat, Tuning Fork, Ornamental Fan, Regalite, and Vambrace's
    // extra block from its multiplier.
    public int AdditionalBlockGained { get; set; }

    // Total times this relic got its own trigger check but was blocked by
    // its condition not being met. Used by Orichalcum when the player already
    // has block at end of turn.
    public int BlockedTriggers { get; set; }

    // Total Strength this relic added. Used by Reptile Trinket and Shuriken.
    public decimal StrengthAdded { get; set; }

    // Total Plating this relic added. Used by Gorget.
    public decimal PlatingAdded { get; set; }

    // Total cards this relic upgraded. Used by Stone Cracker, Razor Tooth,
    // Sand Castle, Whetstone, War Paint, Fishing Rod, and other
    // upgrade-granting relics.
    public int CardsUpgraded { get; set; }
    public List<string> UpgradedCards { get; set; } = new();

    // Razor Tooth tracking. Combats/turns are held denominators for averages.
    // Plays and draws count only events after the exact combat card was
    // successfully upgraded by Razor Tooth; the triggering play is excluded.
    public int RazorToothCombats { get; set; }
    public int RazorToothTurns { get; set; }
    public int RazorToothUpgradedCardPlays { get; set; }
    public int RazorToothUpgradedCardDraws { get; set; }

    // Total times Bone Flute triggered from an owned Osty attack.
    public int BoneFluteTriggers { get; set; }

    // Total Osty HP successfully summoned by this relic across the run.
    // Used by Bound Phylactery and Phylactery Unbound.
    public decimal TotalOstyHpSummoned { get; set; }

    // Curse-triggered max HP tracking. Used by Darkstone Periapt when a
    // curse successfully enters the owner's permanent deck.
    public int CursesAcquired { get; set; }
    public int TotalMaxHpGained { get; set; }

    // Healing attribution. Attempted is what the relic requested, restored is
    // observed HP actually gained, lost is the gap. Lost reasons keep the gap
    // explainable (full HP, prevention/modification, etc.). Used by Book
    // Repair Knife, Eternal Feather, Planisphere, Lee's Waffle, and other
    // healing relics.
    public decimal TotalHealingAttempted { get; set; }
    public decimal TotalHealingRestored { get; set; }
    public decimal TotalHealingLost { get; set; }
    public Dictionary<string, HealingLostReasonAggregate> HealingLostReasons { get; set; } = new();

    // Relic lifecycle floor snapshots. Used by Lizard Tail to show where it
    // was acquired and where its one-shot revive fired.
    public int? FloorAcquired { get; set; }
    public int? FloorActivated { get; set; }

    // Total observed maximum HP gained by pickup max-HP relics and Chosen Cheese.
    public decimal MaxHpGained { get; set; }

    // Shared max-HP before/after snapshot for relics that add or remove max HP.
    // Original is the first observed value before the relic changed max HP; New
    // is the latest observed value after its max-HP change resolved.
    // For Chosen Cheese, Original stores pickup-time starting max HP and New is
    // intentionally unused because other max-HP effects can interleave between
    // later Chosen Cheese gains.
    public decimal? OriginalMaxHp { get; set; }
    public decimal? NewMaxHp { get; set; }

    // Total times a relic triggered from one or more confirmed Doom deaths.
    // Used by Book Repair Knife.
    public int DoomDeathTriggers { get; set; }

    // Total enemies included in confirmed Doom-death trigger payloads.
    // Used by Book Repair Knife.
    public int DoomKills { get; set; }

    // Total energy generated by this relic across all combats.
    // Used by Happy Flower (gains 1 energy every 3 turns), Gremlin Horn
    // (gains after enemy death), Booming Conch (gains at Elite combat start),
    // Lantern/Very Hot Cocoa/Candelabra/Chandelier (gain energy at the start
    // of turns 1/1/2/3),
    // Prismatic Gem, and Blood-Soaked Rose (max-energy relics counted once per
    // player energy reset), and Seal of Gold (turn-start energy purchased with
    // gold). Also used by Nunchaku, whose gained energy is attributed from the
    // observed PlayerCombatState.GainEnergy delta.
    public int EnergyGenerated { get; set; }

    // Gold actually lost to a relic effect and the portion of its attempted
    // loss that did not leave the player's balance. Seal of Gold is the first
    // relic using these observed outcome fields; attempted loss is derived
    // from its activation count and fixed five-gold cost in the tooltip.
    public int GoldLost { get; set; }
    public int GoldLossBlocked { get; set; }

    // Combats held for relics whose energy-generation tooltip reports a
    // per-combat average. Used by Happy Flower, Nunchaku, and Seal of Gold.
    public int EnergyGeneratedCombats { get; set; }

    // Total relevant player turns that ended with unspent energy while the
    // matching turn-energy relic was held.
    public int FirstTurnsEndedWithExcessEnergy { get; set; }
    public int SecondTurnsEndedWithExcessEnergy { get; set; }
    public int ThirdTurnsEndedWithExcessEnergy { get; set; }

    // Total Vigor applied by Akabeko's combat-start effect.
    public int VigorGained { get; set; }

    // Pen Nib tracking. TotalDamageAttempted stores base damage added; these
    // fields store the attack-play denominator and turn-end charge snapshots.
    public int PenNibAttacksPlayed { get; set; }
    public int PenNibTurnsEndedOn8Charges { get; set; }
    public int PenNibTurnsEndedOn9Charges { get; set; }
    public int PenNibTurnEndChargeTotal { get; set; }
    public int PenNibTurnEndChargeCount { get; set; }

    // Total attempted/base damage from relic effects whose actual damage is not
    // source-attributed by the game's damage entries. Used by Letter Opener
    // and observed-damage relics such as Parrying Shield, Festive Popper, and
    // Mercury Hourglass. For Pen Nib, this stores the raw per-hit amount
    // handed to the damage command: the extra base damage added, before
    // downstream hook effects such as Lethality or Vulnerable.
    public int TotalDamageAttempted { get; set; }

    // Relic damage outcome split. Used by relics such as Parrying Shield,
    // Festive Popper, and Mercury Hourglass when their strikes actually
    // resolve through the game's damage command.
    public int TotalDamageDealt { get; set; }
    public int TotalDamageBlocked { get; set; }
    public int TotalDamageOverkill { get; set; }
    public int Kills { get; set; }

    // Total targets included in those attempted relic-damage payloads. Used by
    // Letter Opener, Parrying Shield, Festive Popper, and Mercury Hourglass.
    public int TotalTargets { get; set; }

    // Letter Opener tracking. TotalDamageAttempted stores the attempted AoE
    // damage, TotalTargets stores live enemy targets at activation time, and
    // these fields provide average denominators plus turn-end charge buckets.
    public int LetterOpenerSkillsPlayed { get; set; }
    public int LetterOpenerCombats { get; set; }
    public int LetterOpenerTurns { get; set; }
    public int LetterOpenerTurnsEndedAt1Charge { get; set; }
    public int LetterOpenerTurnsEndedAt2Charges { get; set; }

    // Tuning Fork tracking. Counts owner Skill plays, held combats/turns for
    // averages, and player-turn-end charge snapshots for near-activation
    // pressure on its persistent 10-Skill counter.
    public int TuningForkSkillsPlayed { get; set; }
    public int TuningForkCombats { get; set; }
    public int TuningForkTurns { get; set; }
    public int TuningForkTurnsEndedOn8Charges { get; set; }
    public int TuningForkTurnsEndedOn9Charges { get; set; }
    public int TuningForkTurnEndChargeTotal { get; set; }
    public int TuningForkTurnEndChargeCount { get; set; }

    // Total potions gained from this relic, split by the potion rarity that
    // was actually claimed. Used by White Beast Statue.
    public int PotionsGained { get; set; }
    public int CommonPotionsGained { get; set; }
    public int UncommonPotionsGained { get; set; }
    public int RarePotionsGained { get; set; }
    public int PotionsSkipped { get; set; }

    // Total relics acquired from this relic, split by the obtained relic's
    // actual rarity. Used by Shovel's Dig rest-site option.
    public int RelicsAcquired { get; set; }
    public int CommonRelicsAcquired { get; set; }
    public int UncommonRelicsAcquired { get; set; }
    public int RareRelicsAcquired { get; set; }
    public int CampfiresNotDug { get; set; }

    // Specific relics granted by relic-owned effects. Used by Large Capsule,
    // Neow's Bones, and Pael's Wing to show which relics/artifacts were obtained.
    public Dictionary<string, RelicGrantedAggregate> RelicsGranted { get; set; } = new();

    // Total offered cards by rarity for relics that generate card-choice
    // screens. Used by Toolbox.
    public int UncommonCardsOffered { get; set; }
    public int RareCardsOffered { get; set; }
    public int UncommonCardsTaken { get; set; }
    public int RareCardsTaken { get; set; }

    // Total choosable card options actually upgraded by Molten Egg, Toxic Egg,
    // or Frozen Egg. Direct card grants are intentionally excluded.
    public int UpgradedCardsOffered { get; set; }

    // Total card reward options consumed when Pael's Wing's Sacrifice option
    // is selected. The game model that owns the sacrifice option is
    // PaelsWing; PaelsFlesh is the separate max-energy-after-turn-3 relic.
    public int CommonCardsConsumed { get; set; }
    public int UncommonCardsConsumed { get; set; }
    public int RareCardsConsumed { get; set; }
    public int SacrificesMade { get; set; }
    public int SacrificesSkipped { get; set; }

    // Status/curse cards exhausted by Pael's Eye when it takes an extra turn.
    public int StatusCardsExhausted { get; set; }
    public int CurseCardsExhausted { get; set; }
    public int CombatsWithoutActivation { get; set; }

    // Strike Dummy tracking. StrikesPlayed is cumulative since the relic was
    // picked up; deck counts are current permanent-deck snapshots.
    public int StrikeDummyStrikesPlayed { get; set; }
    public int StrikeDummyBaseStrikesInDeck { get; set; }
    public int StrikeDummyNonBaseStrikeCardsInDeck { get; set; }

    // Nutritious Soup tracking. Counts finished plays of basic Strike-tagged
    // cards carrying the Tezcataras Ember enchantment while the relic is held.
    public int NutritiousSoupEnchantedStrikesPlayed { get; set; }

    // Miniature Cannon tracking. Plays and hits are cumulative while the relic
    // is held; deck count is the current permanent-deck snapshot.
    public int MiniatureCannonUpgradedAttacksInDeck { get; set; }
    public int MiniatureCannonUpgradedAttackPlays { get; set; }
    public int MiniatureCannonUpgradedAttackHits { get; set; }

    // Vajra tracking. Counts attack cards played while held, and actual enemy
    // damage hits from those attacks. Multi-hit attacks increment hits per
    // resolved damage entry.
    public int VajraAttacksPlayed { get; set; }
    public int VajraAttackHits { get; set; }

    // Kunai tracking. Counts owner attack plays, actual Dexterity gained from
    // activation resolution, and player-turn-end charge snapshots.
    public int KunaiAttacksPlayed { get; set; }
    public int KunaiDexterityGained { get; set; }
    public int KunaiTurnsEndedAt1Charge { get; set; }
    public int KunaiTurnsEndedAt2Charges { get; set; }
    public int KunaiTurnEndChargeTotal { get; set; }
    public int KunaiTurnEndChargeCount { get; set; }

    // Kusarigama, Ornamental Fan, and Shuriken share Kunai's repeatable
    // three-Attack, turn-reset counter. Their payoff uses the shared relic
    // damage, block, and Strength fields above; these fields preserve each
    // relic's input count and player-turn-end unused charge.
    public int KusarigamaAttacksPlayed { get; set; }
    public int KusarigamaTurnsEndedAt1Charge { get; set; }
    public int KusarigamaTurnsEndedAt2Charges { get; set; }
    public int KusarigamaTurnEndChargeTotal { get; set; }
    public int KusarigamaTurnEndChargeCount { get; set; }
    public int OrnamentalFanAttacksPlayed { get; set; }
    public int OrnamentalFanTurnsEndedAt1Charge { get; set; }
    public int OrnamentalFanTurnsEndedAt2Charges { get; set; }
    public int OrnamentalFanTurnEndChargeTotal { get; set; }
    public int OrnamentalFanTurnEndChargeCount { get; set; }
    public int ShurikenAttacksPlayed { get; set; }
    public int ShurikenTurnsEndedAt1Charge { get; set; }
    public int ShurikenTurnsEndedAt2Charges { get; set; }
    public int ShurikenTurnEndChargeTotal { get; set; }
    public int ShurikenTurnEndChargeCount { get; set; }

    // Paper Phrog tracking. Damage added is the actual current damage amount
    // multiplied by Paper Phrog's extra Vulnerable multiplier. Enhanced attacks
    // count each real damage activation where the relic increased Vulnerable.
    public decimal PaperPhrogDamageAdded { get; set; }
    public int PaperPhrogEnhancedAttacks { get; set; }
    public int PaperPhrogCombats { get; set; }
    public int PaperPhrogTurns { get; set; }

    // Regalite tracking. Cards created counts owner-created combat cards while
    // the relic is held; combats and turns are held denominators for block
    // averages.
    public int RegaliteCardsCreated { get; set; }
    public int RegaliteCombats { get; set; }
    public int RegaliteTurns { get; set; }

    // Intimidating Helmet tracking. Activations is the number of owner card
    // plays whose play-time EnergyValue met the relic's 2+ threshold;
    // AdditionalBlockGained is the observed block. These are held combat/turn
    // denominators for the requested averages, including zero-trigger periods.
    public int IntimidatingHelmetCombats { get; set; }
    public int IntimidatingHelmetTurns { get; set; }

    // Bookmark tracking. Activations is total cost-reduction activations;
    // BookmarkCombats is the denominator for average activations per combat.
    public int BookmarkCombats { get; set; }
    public int BookmarkCommonActivations { get; set; }
    public int BookmarkUncommonActivations { get; set; }
    public int BookmarkRareActivations { get; set; }

    // Nunchaku tracking. Attacks and energy are cumulative while held; combat
    // end charge stats snapshot the relic's in-game counter at promotion time.
    public int NunchakuAttacksPlayed { get; set; }
    public int NunchakuCombatsEndedOn8Charges { get; set; }
    public int NunchakuCombatsEndedOn9Charges { get; set; }
    public int NunchakuCombatEndChargeTotal { get; set; }

    // Iron Club tracking. Cards drawn is stored in AdditionalCardsDrawn; this
    // stores the held-combat denominator, combat-end charge buckets, and
    // explicit charge samples for the average charge at combat end.
    public int IronClubCombats { get; set; }
    public int IronClubCombatsEndedOn0Charges { get; set; }
    public int IronClubCombatsEndedOn1Charges { get; set; }
    public int IronClubCombatsEndedOn2Charges { get; set; }
    public int IronClubCombatsEndedOn3Charges { get; set; }
    public int IronClubCombatEndChargeTotal { get; set; }
    public int IronClubCombatEndChargeCount { get; set; }

    // Cost-discount tracking. Used by Brilliant Scarf.
    public int DiscountCombats { get; set; }
    public int DiscountsOffered { get; set; }
    public int DiscountsTaken { get; set; }
    public int EnergySavedByDiscount { get; set; }
    public Dictionary<string, DiscountedCardCostAggregate> DiscountedCardCosts { get; set; } = new();

    // Total cards actually discarded while Gambling Chip's combat-start
    // selection resolves.
    public int CardsDiscarded { get; set; }

    // Total ? map points entered while the relic was held. Used by Juzu Bracelet.
    public int QuestionMarkSitesEntered { get; set; }

    // Current Dowsing quest progress granted by Dowsing Rod. Nullable keeps
    // historic run files distinguishable from an observed zero remaining.
    public int? DowsingQuestionRoomsRemaining { get; set; }

    // Floors already ascended when the first shop map point is entered while
    // the relic is held. Used by Cursed Pearl.
    public int? FloorsAscendedBeforeFirstShop { get; set; }

    // Cards actually removed while Precarious Shears' pickup effect resolves.
    public List<string> CardsRemoved { get; set; } = new();

    // Legacy max-HP snapshot around Precarious Shears' pickup cost. New max-HP
    // changing relics should write OriginalMaxHp/NewMaxHp instead.
    public decimal? StartingMaxHp { get; set; }
    public decimal? ResultingMaxHp { get; set; }

    // Total card rewards whose creation options were modified by this relic.
    // Used by Prismatic Gem.
    public int CardRewardsAffected { get; set; }

    // Fresnel Lens card-reward tracking. The any-Nimble counter overlaps the
    // exact-two and three-or-more breakdowns. Taken counts only successful
    // picks from those rewards.
    public int NimbleCardsTaken { get; set; }
    public int RewardScreensWithNimbleCards { get; set; }
    public int RewardScreensWithTwoNimbleCards { get; set; }
    public int RewardScreensWithThreeOrMoreNimbleCards { get; set; }
    public int RewardScreensWithoutNimbleCards { get; set; }
    public int RewardScreensWithNimbleCardsButNoneTaken { get; set; }

    // Ordered card-reward offers modified by Silver Crucible. Each screen is
    // keyed by the relic's own one-based use number (1-3), while Cards keeps
    // the visible left-to-right option order and, once resolved, explicit
    // taken/not-taken outcomes. A list is required because duplicate card
    // definitions can be offered and must remain distinct observations.
    public List<RelicCardRewardScreenAggregate> CardRewardScreens { get; set; } = new();

    // Observed card reward options by card pool while Prismatic Gem is owned.
    // This is intentionally meta: other reward modifiers may also affect the
    // final options. Used by Prismatic Gem.
    public Dictionary<string, CardRewardCategoryAggregate> CardRewardCategories { get; set; } = new();

    // Specific cards granted by relic-owned effects. Used by Hefty Tablet to
    // show which rare card was picked from its pickup screen, Arcane Scroll to
    // show the rare card it added, and Neow's Bones to show the curse it added.
    public Dictionary<string, RelicCardAggregate> CardsGranted { get; set; } = new();

    // Physical cards Pael's Tooth has actually returned to the deck. The list
    // preserves observed return order, duplicate definitions, the final title,
    // and the post-return upgrade level of each new deck instance.
    public List<RelicCardReturnAggregate> CardsReturned { get; set; } = new();

    // Times a relic-owned card choice was skipped. Used by Hefty Tablet.
    public int CardChoicesSkipped { get; set; }

    // Actual card transformations caused by relic-owned effects. Used by Leafy
    // Poultice to show the two basic cards it transformed and their results.
    public List<RelicCardTransformationAggregate> CardTransformations { get; set; } = new();
}

public class CardRewardCategoryAggregate
{
    public string DisplayName { get; set; } = "";
    public int Count { get; set; }
}

public class RelicCardAggregate
{
    public string CardId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int Count { get; set; }
}

public class RelicCardReturnAggregate
{
    public string CardId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int UpgradeLevel { get; set; }
}

public class RelicCardRewardScreenAggregate
{
    public int ScreenNumber { get; set; }
    // Floor where this Silver use generated its options. This bounds hot-
    // reload/Continue re-association so an abandoned unresolved screen cannot
    // attach itself to an unrelated reward on a later floor.
    public int? Floor { get; set; }
    // False while the reward is generated but no terminal selection/skip/
    // reroll outcome has been observed yet. Persisting this state keeps the
    // screen recoverable across a Core hot reload between floors.
    public bool Resolved { get; set; }
    public List<RelicCardRewardOptionAggregate> Cards { get; set; } = new();
}

public class RelicCardRewardOptionAggregate
{
    public string CardId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int UpgradeLevel { get; set; }
    public bool Taken { get; set; }
}

public class RelicGrantedAggregate
{
    public string RelicId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int Count { get; set; }
}

public class RelicCardTransformationAggregate
{
    public string SourceCardId { get; set; } = "";
    public string SourceDisplayName { get; set; } = "";
    public string ResultCardId { get; set; } = "";
    public string ResultDisplayName { get; set; } = "";
}

public class DiscountedCardCostAggregate
{
    public int EnergyCost { get; set; }
    public int StarCost { get; set; }
    public int Count { get; set; }
}

public readonly record struct CardRewardCategoryObservation(string Key, string DisplayName);

/// <summary>
/// One entry in the full event log. Captures what the mod observed, not what the
/// external analysis will compute on top (that's the aggregates' job).
/// </summary>
public class CardEvent
{
    public string T { get; set; } = "";          // ISO-8601 UTC timestamp
    public string Type { get; set; } = "";       // "card_played" | "damage_received" | "energy_gained" | "stars_gained" | "forge_gained"
    public string CardId { get; set; } = "";

    // card_played fields
    public string? Target { get; set; }          // if the card targeted an enemy, their entity id (e.g. "KIN_PRIEST_0")
    public int? EnergySpent { get; set; }        // actual energy paid for this play (accounts for cost modifiers)
    public int? EnergyGained { get; set; }       // actual energy added to the pool while this card was resolving
    public int? StarsSpent { get; set; }         // actual stars paid for this play
    public int? StarsGained { get; set; }        // actual stars added while this card was resolving
    public decimal? ForgeGained { get; set; }    // actual forge added while this card was resolving

    // card_upgraded fields (and general-purpose: Floor also stamped on
    // other event types when useful). UpgradeLevel is the NEW level AFTER
    // the upgrade (post-increment); Floor is RunManager.State.TotalFloor
    // at the moment the upgrade fired.
    public int? Floor { get; set; }
    public int? UpgradeLevel { get; set; }

    // damage_received fields (only populated when Type == "damage_received" with a CardSource)
    public string? Receiver { get; set; }
    public int? Blocked { get; set; }
    public int? Unblocked { get; set; }
    public int? Overkill { get; set; }
    public bool? Killed { get; set; }
}
