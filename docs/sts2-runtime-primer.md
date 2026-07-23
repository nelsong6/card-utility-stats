# Slay the Spire 2 Runtime Primer

This is the stable mental model future agents should read before changing card or relic attribution. It is not a replacement for source inspection. It is the map of the game/runtime facts that have been rediscovered often enough that they should be treated as durable context.

Primary local entry points after reading this:

- `Core/RunTracker.cs`: attribution state machine, pending combat buffer, persistence boundary, card identity, block/effect/poison ledgers.
- `Core/Patches/*.cs`: trusted hook surfaces and the timing assumptions behind them.
- `Core/RunData.cs`: persisted shape and what each aggregate field means.
- `docs/architecture.md`: SpireLens topology and product-level data flow.

## The Two Worlds

SpireLens always has to keep two worlds distinct:

1. The game's live runtime: Godot nodes, CombatManager, RunManager, CardModel objects, piles, hooks, combat history, powers, relics, and async model callbacks.
2. SpireLens' run record: committed `RunData`, pending combat aggregates/events, tooltip projections, and JSON persistence.

The live runtime is mutable and object-reference heavy. The persisted run record must be stable across hot reloads, combat transitions, and future shape additions. Most bugs come from accidentally treating one world as if it had the guarantees of the other.

A good rule: observe the game as late as needed to know what really happened, but record it in SpireLens as early as needed to preserve the source context before the game discards it.

## Threading And Async Shape

The game events SpireLens relies on fire on the main thread. `RunTracker` still uses a lock because save I/O is asynchronous and because the hot-reload lifecycle can cross visible state transitions.

Several STS2 hook methods are async or participate in async card/relic/power flows. SpireLens usually does not await them. Instead, patches capture a prefix or postfix observation at a point where the relevant fact is already true or the relevant source context has not yet been lost.

Important examples:

- `Hook.AfterCardDrawn` is async in the game, but a SpireLens prefix is enough because by the time this hook is invoked, the card is already in hand.
- `Hook.ShouldDraw` runs before the draw succeeds or fails, which makes it useful for source attribution and blocked-draw attempts.
- `Hook.AfterCardChangedPiles` observes the final pile result after a move/redirection, which is better than trusting the attempted move.
- Energy and star gain are captured with before/after snapshots on the actual player resource mutation methods, so the recorded amount is the applied delta.

Do not assume that a card's source context is still available when a visible outcome appears. Some effects finish the `CardPlay` history entry before their downstream action fully resolves.

## Hot Reload Lifecycle

The runtime is split into a stable loader and a hot-reloaded core.

- `Loader/LoaderMain.cs` is the long-lived bootstrap loaded by the mod manager.
- `Core/CoreMain.cs` is reloaded on F5. It installs Harmony patches, wires tracker hooks, resumes active run state, and cleans up on shutdown.
- `CoreMain.Initialize()` must allocate only things that `CoreMain.Shutdown()` can release.
- Shutdown order is deliberately UI teardown, tooltip teardown, event unsubscription, then Harmony unpatching.

Old core assemblies are orphaned rather than truly unloaded. That is acceptable only if the old assembly stops receiving callbacks. Any new event subscription, Godot node signal, Harmony patch, static callback, or UI node must have an explicit cleanup path.

When adding a hook:

- Make sure it is installed by Harmony patch discovery.
- Make sure it is removed by `UnpatchAll(_harmonyId)` or otherwise cleaned up.
- If it subscribes to game events or Godot signals directly, add teardown.
- If it creates UI nodes, make hot-reload reinjection and `QueueFree()` behavior explicit.

## Run And Combat Boundaries

SpireLens persistence is combat-boundary based.

- `RunManager.Instance.RunStarted` starts a new run record — or resumes one; see below.
- `CombatManager.Instance.CombatSetUp` creates `_pendingCombat`.
- During combat, live observations accumulate in `_pendingCombat`.
- `CombatManager.Instance.CombatEnded` promotes pending aggregates/events into committed `RunData`, updates run metadata, saves, and clears `_pendingCombat`.
- `RunTracker.OnRunEnded` also promotes `_pendingCombat` before stamping the outcome — for the `loss` outcome only. Loss ordering (decompiled `CreatureCmd.Kill` → `LoseCombat()` → `RunManager.OnEnded`): `OnRunEnded` runs synchronously from the killing action, and the fatal combat's `CombatEnded` only fires LATER via `ProcessPendingLoss` — after the buffer has been consumed. Without this second promotion site the fatal combat's stats would be discarded. Abandoning mid-combat still discards the buffer (a half-played fight is not a resolved combat — a save-and-quit may even have rolled it back), and wins always get a normal `CombatEnded` first. Promotion is idempotent per buffer (the buffer is nulled after), so the two sites cannot double-promote. `RecordCombatEndingSuppressedDamage` stops capturing once `_currentRun` is null so the post-`OnRunEnded` damage tail can't resurrect the buffer and mint a junk run file at that deferred `CombatEnded`.
- Between-combat and between-floor reloads are supported.
- Mid-combat restore is intentionally out of scope.

This distinction is easy to blur because tooltips merge committed and pending data for immediate display. That merged view is for UI only. The permanent run file is not promoted until combat ends — or, for the final combat of a lost run, until the run ends.

`RunStarted` does NOT always mean a new run. The game re-fires `RunManager.RunStarted` with the SAME `RunManager._startTime` every time a saved run is continued from the main menu (log line: "Continuing run with character"). `RunTracker.OnRunStarted` therefore treats a matching in-progress `game_start_time` as a continuation: it keeps the in-memory `RunData` (or, after a full game restart, adopts the newest resumable on-disk record via `RunStorage.FindByGameStartTime`) and rebinds card identity through `AdoptRunLocked` instead of minting a fresh run file. Historic builds minted a fresh `RunData` here, which stranded all previously committed stats in orphaned files and reset tooltips to zero mid-run.

When implementing new combat attribution, write it into `_pendingCombat` first unless the event is truly outside combat, such as card arrival/removal/upgrade lineage. Promotion should stay centralized in `PromotePendingCombatIntoRun` and its two callers.

## RunStarted Is Not Deck-Ready

Do not use `RunStarted` as the source of truth for starter deck population.

Earlier code tried to walk `player.Deck.Cards` at `RunStarted`, but fresh runs had a timing race: the deck was not always populated yet. The durable hook is `CardPile.AddInternal` filtered to `PileType.Deck`, implemented by `CardEnterDeckPatch`.

Continue-loads make the ordering worse: when a saved run is continued from the main menu, the game repopulates the deck with brand-new `CardModel` refs, and `CardPile.AddInternal` can fire BEFORE or AFTER the `RunStarted` re-fire — neither order is guaranteed. Observed consequence of the before-ordering: `CardEnterDeckPatch` stamps the fresh refs with brand-new instance numbers continuing the old counters (ghost aggregates like `CARD.DEATH_MARCH#3/#4` for a two-copy deck) before run adoption can rebind them. `AdoptRunLocked` handles both orderings with one mechanism: it seeds the saved `InstanceNumbersByDef` snapshot into `_pendingRankRestores` (per-def queues in deck-rank order), walks whatever portion of the deck already exists through `GetOrAssignNumber` (which claims queued numbers in arrival order before minting), leaves the rest queued for later `CardEnterDeckPatch` arrivals, and prunes the ghost stamps afterward via `PruneGhostAggregates`. Known assumption (shared with the hot-reload deck walk): repopulation arrival order matches `player.Deck.Cards` enumeration order at save time — if the game ever repopulates same-def copies out of deck order, two copies of the same def would silently swap stats. `CaptureInstanceNumbersByDeckRank` includes still-queued numbers in every snapshot, so a save during adoption cannot strand them; queues left unclaimed at combat setup are discarded with an always-on log line.

`CardPile.AddInternal` catches:

- starter deck population,
- Ascender's Bane / ascension curse insertion,
- reward cards,
- shop cards,
- event grants,
- other permanent deck entries routed through deck pile mutation.

For card arrival metadata, prefer the game's `CardModel.FloorAddedToDeck` when present. The game sets it to floor 1 for starters and current floor for mid-run additions. If it is null, SpireLens falls back to the current run floor when possible.

## Card Identity

Card identity is per physical card when a card has stable deck identity.

Key facts:

- Combat cards are often clones of permanent deck cards.
- Combat clones point back to the deck original through `CardModel.DeckVersion`.
- Deck-view cards are already the original and usually have `DeckVersion == null`.
- `RunTracker.Canonical(card)` uses `card.DeckVersion ?? card` so combat-time and hover-time references converge.
- Aggregates are keyed as `{card_definition_id}#{monotonic_number}`.
- The monotonic number is per card definition and is never reused within a run.
- Removed cards keep their aggregate and removal snapshot rather than being deleted.

Do not key long-lived attribution by raw `CardModel` reference unless you are inside a live-only ledger that will never cross reload or persistence boundaries. References are useful inside a single combat; persisted data needs stable string keys.

Non-assigning lookups matter. Hovering a preview/template card should not burn a new instance number. Paths that merely display a card should use non-assigning lookup behavior; paths that observe a real deck entry or real play may assign.

## Piles And Card Movement

Permanent deck membership is not the same thing as a card's current combat pile.

During combat, a deck card may be in draw, hand, discard, exhaust, play, or another transient pile. The permanent deck view still wants the same physical instance identity. Conversely, combat-generated cards may exist only for a combat and should not always pretend to be permanent deck members.

Useful pile facts in this repo:

- `CardPile.AddInternal` filtered to `PileType.Deck` means permanent deck entry.
- `CardPileCmd.RemoveFromDeck` prefix captures removal before the game detaches the card and mutates its state.
- `CardPileCmd.Add(card, PileType.Draw, CardPilePosition.Top, ...)` prefix can classify top-of-draw placements by reading the source pile before mutation.
- `Hook.AfterCardChangedPiles` is the generic post-mutation observation point for final pile results.

For redirections, prefer final pile observation. A card can attempt to move to hand but land elsewhere because the hand is full or because another game rule intervenes.

## Combat History

`CombatHistory.Add(entry)` is the broadest combat observation point. `CombatHistoryAddPatch` postfixes it and forwards each typed entry to `RunTracker.Observe(entry)` after the entry has been appended, which means the entry survived the game's own logic and is real.

This is a good master hook for entries that reliably reach `Add`, such as many card plays, damage entries, block entries, and power entries.

But do not assume every small `CombatHistory.*` wrapper is safe to patch. The repo has a documented draw trap:

- `CombatHistory.CardDrawn` is tiny and can be JIT-inlined.
- Harmony patches on that wrapper did not fire in diagnostic runs.
- `CardDrawnEntry` also did not appear through the generic `Observe` distribution during confirmed draws.
- The reliable draw hook is `Hook.AfterCardDrawn`, not `CombatHistory.CardDrawn`.

When a future stat seems obvious from combat history but never appears, suspect inlining, alternate code paths, or late async flow. Add a focused diagnostic hook only long enough to prove the path, then promote the reliable hook or document the trap.

## Card Play Timing

A card play has at least three useful phases for stats:

1. The card play is recognized and source context is available.
2. The card's immediate costs/effects mutate resources, piles, powers, block, damage, etc.
3. Downstream effects may continue after the play is already considered finished by some history APIs.

`CardPlay.Resources.EnergySpent` and star spend are the source of truth for actual cost paid, not printed card cost. This captures cost reduction, free plays, X-cost behavior, and modifiers.

`CardPlay.PlayIndex` and `CardPlay.PlayCount` describe a play series. Total
card plays should still count every `CardPlayFinishedEntry`; replay extra plays
are the subset where `PlayIndex > 0`. This preserves the normal "played N"
stat while revealing how often Replay/Glam-like mechanics caused additional
plays.

Replay source attribution is a two-phase pattern. Capture play-count modifiers
when the game calculates the series, then spend those pending sources only when
an actual extra `CardPlayFinishedEntry` arrives. `Hook.ModifyCardPlayCount`
exposes power/model contributors such as Burst, Echo Form, One Two Punch,
Duplication, Signal Boost, and Tag Team. `Glam.EnchantPlayCount` covers the
card-enchantment Replay path. If an extra play has no captured source, count it
as plain `Replay`; this is the fallback for card-native/base replay counts and
other effects that mutate the card's replay count before the hook can expose a
source.

Replay shortfalls and no-outcome replays are not the same thing. `PlayCardAction`
can cancel before `OnPlayWrapper` starts if `CanPlay` or `IsValidTarget` fails;
that produces no `CardPlayStartedEntry`, no `CardPlayFinishedEntry`, and no
resource spend. Once `OnPlayWrapper` starts, the game loops through the generated
`playCount`, emits started/finished history for each iteration, and lets the
card's own async `OnPlay` body run its commands. A later replay can therefore be
a real finished card play while a command such as `AttackCommand` finds no valid
target and produces no damage entries. Track planned replay extras, finished
replay extras, and command-specific no-outcome buckets separately.

For source context, `RunTracker` keeps notions like current player card play, recently completed player card play, pending draw source, pending effect source, and history counts. These are deliberately temporal and should be handled carefully. When adding a stat, ask:

- Is the source card still current at the outcome hook?
- If not, can the source be captured before the outcome and resolved after it?
- Is there a recent-completed play fallback, and how many history entries should it remain valid for?
- Could an enemy, relic, or power produce the same outcome without a card source?

Do not casually widen temporal attribution windows. A wide window can make unrelated follow-up effects look card-caused.

Alchemize creates one random in-combat potion and awaits the exact
`PotionCmd.TryToProcure(PotionModel, Player, int)` overload, but discards its
`PotionProcureResult`. Capture the currently resolving physical Alchemize card
when that command begins, then wrap the returned task and record its observed
result before returning it to Alchemize. A successful result supplies the
actual gained potion and rarity; a failed result means no potion entered the
belt (currently a full belt or a `ShouldProcurePotion` blocker such as Sozu).
Do not resolve the source after awaiting: `CardPlayFinished` can clear the
current-card context as soon as Alchemize resumes.

Debt applies its curse effect from the owner-specific
`Debt.OnTurnEndInHand(PlayerChoiceContext)` callback. Its intended loss is the
card's `Gold` dynamic var, but Debt clamps the amount passed to
`PlayerCmd.LoseGold` to the owner's current balance. Observe the owner's gold
before and after the completed callback: that delta is actual gold lost, and
`intended - actual` is the amount blocked by insufficient gold. Patching the
generic gold-loss command would lose Debt's unclamped intent and risk
attributing unrelated gold changes.

Seal of Gold uses its owner-specific
`AfterSideTurnStart(CombatSide, IReadOnlyList<Creature>, ICombatState)`
callback. It activates only when the owner is in the callback's participant
list and has at least its five-gold cost, then awaits energy gain followed by
gold loss. Apply the same affordability gate in the prefix, wrap the returned
task, and observe both resource deltas on completion. Count held combats
separately from activations so its boss-relic energy-per-combat average includes
combats where the owner ran out of gold and the relic produced no energy.

## Damage Attribution

For direct card damage, `DamageReceivedEntry` is the important observed outcome.

Enemy damage totals are computed from the game-reported pieces:

- `BlockedDamage`: damage absorbed by target block.
- `UnblockedDamage`: HP actually lost.
- `OverkillDamage`: attempted damage beyond lethal.

SpireLens definitions:

- intended damage = blocked + unblocked + overkill,
- effective damage = unblocked,
- blocked = blocked,
- overkill = overkill,
- kill = `WasTargetKilled`.

Effective damage is the user-facing total damage because it is the HP actually removed. Intended damage is useful internally and for waste percentages.

Relic damage should follow the same observed-result path when the relic emits a damage command. For example, Festive Popper arms attribution from `FestivePopper.AfterPlayerTurnStart` only when its owner is about to fire on turn one, then records the actual `CreatureCmd.Damage` results so blocked damage, overkill, and dead targets are not inferred from the relic text.

Known trap: an attack can play and produce no damage event, for example if the target is already dead/not fully removed or if no damage is actually received. Tooltip code treats a played attack with zero intended damage as a real but zero-damage case rather than inventing damage.

Known trap — combat-ending killing blows: `DamageReceivedEntry` is NOT complete. In decompiled `CreatureCmd.Damage`, HP loss (`LoseHpInternal`) is applied BEFORE the emission gate `if (CombatManager.Instance.IsInProgress && !CombatManager.Instance.IsEnding) History.DamageReceived(...)`, and `CombatManager.IsEnding` is a live computed property that flips true the moment no living primary enemy remains — so the hit that kills the last enemy suppresses its own history entry and never reaches `CombatHistory.Add`. Mid-combat kills (another enemy still alive) emit normally. Scaling finishers systematically lose their biggest hits to this (diagnosed 2026-07-02 with Death March: 5/5 combat-ending kills unrecorded, all mid-combat hits recorded). SpireLens closes the gap with `HookAfterDamageGivenPatch`: `Hook.AfterDamageGiven` is dispatched directly by the game (its own doc comment: it "fires from the same damage event that ends combat (the killing hit), so it must still resolve for that hit"), fires after the possible emission for the same `DamageResult`, and runs before `Kill()` triggers `CombatEnded`. `RunTracker.RecordCombatEndingSuppressedDamage` synthesizes the missing `DamageReceivedEntry` (never touching the game's own history) only when `IsEnding` is true AND the `DamageResult` reference wasn't already observed via `CombatHistory.Add` — dedup is by result-object reference through `TryMarkDamageResultObserved`. Deliberate scope note: this captures ALL history-suppressed damage in the ending window, not only the killing blow itself — e.g. thorns or residual hits that really applied HP loss while combat wound down. That is the observed-outcomes principle: the damage genuinely happened (`LoseHpInternal` ran); only the game's history omitted it. Damage that never applied produces no `Hook.AfterDamageGiven` dispatch and is never invented.

Player self-damage is tracked as HP lost from playing a card and uses observed unblocked damage after reductions. That is the real cost, not the text value.

Maximum-HP costs are a separate signal from current-HP damage. Brightest
Flame's owner-specific `OnPlay` gains energy, draws, and then awaits
`CreatureCmd.LoseMaxHp`. That command records the requested amount in the
game's map history but clamps the resulting max HP to at least one; it can also
deal current-HP damage with no card source when current HP is above the new
maximum. Wrap Brightest Flame's returned `OnPlay` task and compare the owner's
max HP before and after the callback so SpireLens records only the actual max
HP removed, including a zero delta at the one-max-HP floor. Keep this in the
card aggregate, separate from `TotalHpLost`.

Storybook grants a permanent-deck Brightest Flame but does not retain a
reference to that exact granted card. Its tooltip therefore uses the explicit
pooled-by-definition model: combine every `CARD.BRIGHTEST_FLAME` aggregate,
including pending combat data in live views, and show the card's play, draw,
energy, card-draw, and max-HP-loss stats. Brightest Flame can also come from
the event card pool, so this is deliberately a family-wide projection rather
than exact Storybook-grant lineage.

## Osty Body Attribution

Necrobinder has both investment cards that create or maintain Osty and payoff
cards that spend the resulting board state. Track those as different signals.

- Patch `OstyCmd.Summon`, not individual card text, for successful card-sourced
  Osty summons. The command carries the source model and returns the
  game-observed summon amount.
- Attribute the summon amount to the source card. That is the card's own
  contribution. Tooltip label: `Summon gained`.
- Also add successful summon amount to run-level Osty meta stats. Tooltip
  label: `All Osty total summon`.
- Attribute Osty HP lost through `Hook.AfterCurrentHpChanged` negative deltas
  on any Osty creature into run-level meta stats. Do not assign all later Osty
  damage absorbed to the card that happened to summon most recently. Tooltip
  label: `All Osty damage absorbed`.
- Keep payoff tracking separate. For example, Unleash's Osty-current-HP attack
  bonus belongs on Unleash, while HP summoned belongs on the summon card, and
  all-Osty totals are meta stats surfaced on related summon cards.

## Block Attribution

Block has two different stats:

- block gained: what a card added to the player's block pool,
- block effective/wasted: what that block later absorbed or failed to absorb.

The game has one block pool, not per-card block. SpireLens uses a provenance ledger inside `_pendingCombat.PlayerBlockLedger`.

The current mental model:

- When a card grants block, add a `BlockChunk` with a source card instance and sequence.
- When incoming damage consumes block, absorbed block is charged through the ledger in FIFO order.
- When block clears/expires unused, wasted block is charged through surviving ledger chunks in LIFO order, matching the idea that later overfill was more likely redundant.
- Retain/prevent-clear effects must cancel pending clear attribution.

Relevant hooks:

- block gained comes from observed combat outcomes in `RunTracker.Observe` / block entries,
- `Hook.ShouldClearBlock` arms a possible clear with the current player block amount,
- `Hook.AfterBlockCleared` confirms clear and attributes waste,
- `Hook.AfterPreventingBlockClear` cancels the armed clear.

When changing block logic, be explicit that effective/wasted block is heuristic ledger attribution, not a game-native per-card truth.

## Draw Attribution

Draw stats have three related but different signals:

- this card was drawn (`TimesDrawn`),
- this card caused other cards to be drawn (`TimesCardsDrawn`),
- this card attempted draws that were blocked or redirected (`TimesCardsDrawAttempted`, `TimesCardsDrawBlocked`, `BlockedDrawReasons`).

Reliable hooks:

- `Hook.AfterCardDrawn` records that a card arrived in hand/draw flow. `fromHandDraw` distinguishes turn-start automatic draw from effect-side draw.
- `Hook.ShouldDraw` prefix notes draw attempts while source context is still recoverable.
- `Hook.ShouldDraw` postfix records blocked attempts when result is false and exposes the blocking modifier.
- `Hook.AfterCardChangedPiles` can identify final pile result for redirected movement.

Do not derive draw solely from play counts. A card can be drawn and not played; a draw effect can attempt to draw and be blocked by No Draw, hand size, or other prevention.

Blocked draw reason rows should stay explanatory rather than pretending full certainty. Known buckets include No Draw, hand full, and other/uncategorized.

## Energy, Stars, And Forge

For resource generation, record the actual mutation, not card text.

Energy:

- Hook `PlayerCombatState.GainEnergy`.
- Prefix captures before value.
- Postfix computes positive delta and attributes to the currently resolving card owned by that player.

Stars:

- Hook `PlayerCombatState.GainStars` the same way.
- Star spend is from actual play resources, not listed star cost.

Forge:

- Hook `Hook.AfterForge`.
- Record actual forge amount, forger, and source.
- This also preserves effect source context for immediate follow-up effects and marks Sovereign Blade overlay availability.

For UI, energy/star spent rows intentionally appear only when empirical variance exists or when the resource is otherwise interesting. Absence of a row usually means actual spend matched expected listed cost across plays.

## Effects And Powers

Power/effect attribution needs two layers:

1. Application attribution: which card caused a power/effect to be applied and for how much.
2. Downstream attribution: what that applied effect later caused, if observable and meaningful.

`PowerReceivedEntry` and related hooks can record applications when card source is available. But receiver-side modifiers can change or eliminate the application.

Artifact-blocked debuffs require a before/after pair:

- `Hook.BeforePowerAmountChanged` captures attempted power, amount, target, applier, and card source before receiver-side modifiers.
- `Hook.ModifyPowerAmountReceived` postfix sees the final result and modifiers. If requested amount was reduced to zero by Artifact, SpireLens can still credit the blocked attempt to the source card.

Do not record only successful applications. A debuff eaten by Artifact is still an important thing the card caused, and it should surface as blocked/stripped rather than disappearing.

## Poison And Other Downstream Damage

Poison demonstrates the core challenge of downstream attribution: the damage tick often arrives with `CardSource == null`, but users care which card originally applied the poison.

Current poison model:

- Poison applications are recorded as applied effects on the source card.
- `_pendingCombat.PoisonOwnershipByTarget` tracks ownership shares by target and source effect.
- `PoisonPower.AfterSideTurnStart` arms a one-shot attribution window for the target.
- The next null-source damage on that target can be recognized as poison tick damage and charged through the poison ownership ledger.
- Damage and overkill from poison ticks are added back into the source effect summary.

Noxious Fumes has an extra wrinkle:

- It is a power that later applies poison without direct card source on each application.
- `NoxiousFumesPower.AfterSideTurnStart` arms a short attribution window.
- Contribution ledgers preserve which source card owns the Fumes effect before the poison fanout is added to poison ownership.

When adding another downstream effect stat, follow the poison pattern only if there is a reliable arming event and a narrow enough outcome window. Anonymous damage or anonymous pile movement without a narrow source window should not be guessed broadly.

## Relic Attribution

Relics are not cards, but many relic stats use the same runtime lessons.

Combat-start relics often hook relic model callbacks directly by type name with `AccessTools.TypeByName`, then filter on side and round:

- Bag of Marbles: `BeforeSideTurnStart`, `CombatSide.Player`, `RoundNumber == 1`, count alive enemies, record Vulnerable applied.
- Red Mask: same shape, record Weak applied.

This is intentionally outcome-shaped but still simple. It assumes the relic applies one stack to each alive enemy at combat start. If a future relic has prevention/modifier interactions, use the same observed-result discipline as cards rather than only counting targets.

Relic aggregates live in `RunData.RelicAggregates`, keyed by relic id. Fields are shared across relics; each relic uses only relevant fields.

Centennial Puzzle already exposes the exact combat-local state needed for a
live tooltip through `UsedThisCombat`. The relic sets it before drawing from
its first qualifying unblocked HP-loss callback and resets it in
`AfterCombatEnd`. Read that property directly for `Triggered this combat`;
do not infer the boolean from cumulative activations or persist it in the run
aggregate.

Juzu Bracelet is map-point based, not resolved-room based. Count
`RunManager.EnterMapPointInternal` when the original `MapPointType` is
`Unknown` and the player currently holds the relic. Do not infer this stat from
`RoomType.Event` or `EventRoom`: a `?` can resolve into multiple room types, and
later room transitions can happen after the map site was already entered.

Dowsing Rod itself only grants the Dowsing quest card. The card owns the saved
`RoomsEntered` counter, updates it only for qualifying `?` room entries, and
transforms into Abundance at five. Observe the `Dowsing.RoomsEntered` setter and
derive remaining rooms as `5 - RoomsEntered`; do not independently decrement at
the broader map-point hook. Reconcile from the live Dowsing/Abundance deck card
after Continue or hot reload so the relic tooltip inherits the game's persisted
quest state.

Fishing Rod owns a saved `CombatsSeen` counter and increments it only after
normal monster combats. Every third increment, `FishingRod.AfterCombatEnd`
chooses one random upgradable permanent-deck card and calls `CardCmd.Upgrade`
synchronously before returning its completed task. Attribute the result by
arming a window around that exact callback and consuming the existing
`CardModel.UpgradeInternal` observation; do not infer the chosen card from deck
state or independently reproduce the relic's RNG selection.

Molten Egg, Toxic Egg, and Frozen Egg share `EggRelicHelper.UpgradeValidCards`
for both late card-reward modification and merchant inventory modification.
For each matching upgradable option, that helper calls the two-argument
`CardCreationResult.ModifyCard(CardModel, RelicModel)` overload with the egg as
the modifying relic. Observe that exact call to count upgraded cards offered:
it covers rewards, shops, and other choosable-card surfaces that use the shared
helper, while excluding the eggs' separate `TryModifyCardBeingAddedToDeck`
path for direct non-choosable deck additions.

Pael's sacrifice reward option is owned by `PaelsWing`, not `PaelsFlesh`.
`PaelsWing.TryModifyCardRewardAlternatives` adds the `SACRIFICE` card reward
alternative and `PaelsWing.OnSacrifice` increments the saved sacrifice count.
`PaelsFlesh` is a separate combat max-energy relic that activates after turn 3.
Track consumed card reward rarities and skipped sacrifice opportunities from the
card reward alternative flow, not from PaelsFlesh's energy hooks. Every second
sacrifice pulls and obtains a normal `RelicModel`. Capture that direct artifact
from the owner's first `RelicObtained` event during `OnSacrifice`; the event fires
synchronously when the relic enters inventory, before its async `AfterObtained`
callback. Store its id, display name, and count in Pael's Wing's `RelicsGranted`
ledger.

Pael's Tooth is a separate pickup-and-return mechanic. Its native `CardTitles`
text is rebuilt from only the `SerializableCards` still held by the relic, so a
card intentionally disappears from that text when it is returned. At combat
end, `Hook.AfterCombatEnd` awaits relic listeners sequentially before the
game's `CombatEnded` event: Tooth reconstructs one stored card, upgrades it,
awaits its deck insertion, then removes the stored entry. Wrap Tooth's returned
task so attribution completes before combat promotion, and compare raw deck
references before and after to capture the final physical card after upgrade
and deck-add replacement modifiers. Do not infer a successful return merely
from the stored entry disappearing; Tooth removes it even when the deck-add
result reports failure.

For relics that grant block after a specific owner-owned condition, arm a narrow block-gain window at the relic callback and let `Hook.AfterBlockGained` record the modified amount. Permafrost follows this pattern from `Permafrost.AfterCardPlayed`: mirror the first-owned-Power condition, count that combat trigger, then derive block per combat from observed block gained divided by triggers.

Intimidating Helmet's `BeforeCardPlayed` condition uses the frozen play-time
`cardPlay.Resources.EnergyValue`, not printed cost or `EnergySpent`. Normal
plays therefore use the energy actually paid, resolved X-cost plays use their
captured X value, and autoplay can qualify at its resolved cost despite spending
zero energy. The callback immediately awaits the `BlockVar` overload of
`CreatureCmd.GainBlock`; consume an owner-creature marker at that exact command
and record its returned post-modifier amount. Use all distinct combats and
player turns while the relic is held, including zero-trigger ones, as the
per-combat and per-turn block denominators.

`JugglingPower` already owns the exact turn-local progress needed for its live
counter. `AfterApplied` seeds `attacksPlayedThisTurn` from owner Attack plays in
combat history, `AfterCardPlayed` increments it for subsequent owner Attacks,
and `AfterSideTurnEnd` resets it. Surface that field through `DisplayAmount` and
raise `DisplayAmountChanged` after each mutation; do not repurpose `Amount`,
which remains Juggling's stack count and controls how many Attack copies it
creates. The progress counter continues above the third-Attack trigger and does
not belong in persisted run data.

For relics that emit damage commands, prefer the relic-owned callback plus the resolved `CreatureCmd.Damage` result. Mercury Hourglass arms from `MercuryHourglass.AfterPlayerTurnStart`, records the actual multi-target damage split from the command result on each turn, and counts the combat once so damage per combat is not confused with damage per turn-start trigger.

Mr. Struggles follows the same owner-specific turn-start pattern, but its
damage amount is the current turn number, so it uses the multi-target
`CreatureCmd.Damage` overload with a decimal amount plus `ValueProp` rather
than the `DamageVar` overload used by Mercury Hourglass and Festive Popper.

Pen Nib is detected at its `ModifyDamageMultiplicative` hook when the relic
returns `2` for the actual `CardPlay`, but the amount recorded comes from the
raw per-hit value passed into `CreatureCmd.Damage` before hook modifiers run.
SpireLens labels this "base damage added" rather than deriving from final
damage, so effects such as Lethality or Vulnerable do not inflate the stat.

Pickup relics with multi-step health effects should record the observed result
across the full pickup callback when that is what the player experiences. Lee's
Waffle records current-HP gained across `AfterObtained`, covering both its
max-HP grant and the follow-up heal-to-full.

For simple max-HP pickup relics such as Strawberry, Pear, and Nutritious Oyster,
snapshot the owner's max HP in an `AfterObtained` prefix and observe it only
after the returned task completes successfully. This captures the actual gain,
including caps or other runtime changes, without counting relic restoration.

Darkstone Periapt is owned by `DarkstonePeriapt.AfterCardChangedPiles`. Mirror
the relic's own final-pile condition (`card.Pile.Type == Deck`, same owner,
`CardType.Curse`), then record the actual max-HP delta after the async
`GainMaxHp` command resolves. Count the curse acquisition from that same
owner-specific match rather than from every generic curse card entry.

Chosen Cheese gains max HP from `AfterCombatEnd`, then the game heals the same
amount as part of `CreatureCmd.GainMaxHp`. Snapshot the owner's max HP before
the async relic callback and record only the actual max-HP delta after
successful completion. Its baseline max HP comes from the `RelicCmd.Obtain`
pickup boundary. Do not display or store a Chosen Cheese "resulting max HP":
other max-HP effects can interleave between its later gains, so the only durable
run-level facts are pickup-time starting max HP and total max HP gained. Because
the combat-end callback can complete around combat promotion, route the gained
amount to pending combat when it still exists and directly to the run otherwise.

Strike Dummy identifies eligible cards through the game's `CardTag.Strike`.
Its damage modifier can run per damage evaluation, so count Strike cards played
from finished card-play events while the relic is owned; use the modifier only
to confirm the eligibility rule. Base Strikes are `IsBasicStrikeOrDefend` cards
that also carry the Strike tag, while non-base Strike cards are every other
permanent deck card with that tag.

Kunai, Kusarigama, Ornamental Fan, and Shuriken share the same repeatable
three-Attack counter shape: their owner-specific `AfterCardPlayed` callback
increments a turn-local counter and activates at every threshold multiple.
Count owner Attack plays at that callback, snapshot unused modulo charge from
`Hook.BeforeSideTurnEnd` before the relic resets, and observe each payoff at its
narrow outcome: power delta for Kunai/Shuriken, the resolved block-command
result for Ornamental Fan, and the resolved single-target damage result for
Kusarigama. Kusarigama only activates when its threshold play can choose a
hittable enemy; do not infer an activation from the counter alone.

Razor Tooth upgrades eligible Attack and Skill cards synchronously inside its
owner-specific `AfterCardPlayed` callback, after the finished card-play history
entry has already been emitted. Combat history has no upgrade entry for this
effect, and `CardCmd.Upgrade` only adds the game's run-history upgrade record for
cards in the permanent Deck pile. Snapshot the played card's
`CurrentUpgradeLevel` before and after Razor Tooth's callback and count only a
positive observed delta; `CardCmd.Upgrade` can still no-op while combat is
ending. Keep successfully upgraded cards in a combat-local, raw-reference set:
the same combat `CardModel` survives pile moves and later replay/draw events,
while canonicalizing would risk giving a distinct generated or copied card the
same credit. `CardPlayFinished` occurs before Razor Tooth's callback, so the
triggering play is not an upgraded-card play; later finished replay iterations
are. Count later successful draws only from `Hook.AfterCardDrawn`, and use held
player turns/combats (including zero-result ones) as rate denominators.

Brilliant Scarf increments its per-turn card counter from `AfterCardPlayed`,
after `CardPlayFinished` has already entered combat history. Its actual cost
discounts happen through `TryModifyEnergyCostInCombatLate` and `TryModifyStarCost`
when that counter is one short of the configured threshold. Cost modifiers are
queried repeatedly for UI/playability, so count the offer from the counter
transition and use the modifier only to measure energy saved by the card that
later consumes the offer.

Mummified Hand resolves entirely inside its `AfterCardPlayed` callback despite
returning `Task.CompletedTask`: after an owner Power play, it selects one card
already in hand and calls that card's `SetToFreeThisTurn`. Observe the selected
card and its effective energy cost immediately around that exact call. The
triggering `CardPlay.Resources.EnergyValue` is the Power's play-time cost, while
`EnergySpent` is the distinct numerator for spend-to-discounted-cost ratios.

Pael's Claw applies Goopy with amount 1 to every eligible permanent deck card
in its synchronous `AfterObtained` callback. That initial amount is the
enchantment baseline: Goopy's block bonus is `Amount - 1`. A finished Goopy
card play is observable from `CardPlayFinishedEntry`, but Goopy earns its
permanent increment later in `Goopy.AfterCardPlayed`, where both the combat
copy and `DeckVersion` amounts are incremented. The game skips the entire
`Hook.AfterCardPlayed` dispatch once combat has ended, so count finished Goopy
plays and observed earned enhancements separately rather than assuming they
are identical.

Stone Humidifier applies its repeatable max-HP gain from the owner-specific
async `AfterRestSiteHeal(Player, bool)` callback. Snapshot the owner's max HP
before the callback and only record the resulting max HP after its returned
task completes successfully; the callback awaits `CreatureCmd.GainMaxHp`, so
the post-task value is the observed result after game modifiers or prevention.

Sturdy Clamp prevents the normal player block clear in `ShouldClearBlock`, and
its owner-specific `AfterPreventingBlockClear(AbstractModel, Creature)` callback
runs from `Creature.AfterTurnStart` on every player turn after turn 1, including
when block is zero. Capture the pre-callback block, then wait for its task before
reading retained block because the relic asynchronously removes the amount over
10. The pre-callback amount above 10 is the excess; the post-task block is the
observed retained result.

Shovel adds `DigRestSiteOption` from `TryModifyRestSiteOptions`; the relic
itself does not receive the obtained relic payload. Patch
`DigRestSiteOption.OnSelect`, snapshot the owner's relic inventory before the
async selection, and after a successful result record the newly present relic
instances and their actual `RelicRarity`. To count missed Dig opportunities,
inspect `RestSiteSynchronizer.BeforeLocalRestSiteExited`: at that point the
local option list and chosen-option index still reveal whether a Dig option was
available and whether the selected option was anything other than Dig.

Fresnel Lens applies Nimble from its owner-specific
`TryModifyCardRewardOptionsLate` callback. Count the final option only when its
`CardCreationResult` is still Nimble and names Fresnel Lens in
`ModifyingRelics`; this distinguishes relic-caused Nimble from an option that
was already enchanted. `CardReward.OnSelect` opens the actual selection and
removes a card from its option list only after `CardPileCmd.Add(..., Deck)`
succeeds, so an initial-versus-remaining option snapshot measures cards taken.
The card-screen Skip returns to the outer rewards page without consuming the
reward, so keep the same reference-keyed pending snapshot across reopenings and
finalize it only on a completed selection or `CardReward.OnSkipped`. A reroll
reuses that same reward/screen and must refresh, not increment, its snapshot.
Drowning Beacon applies its max-HP loss before obtaining Fresnel Lens; wrap the
full async `DrowningBeacon.ClimbOption` to preserve the observed before/after
max HP because a relic pickup hook begins too late to recover the baseline.

Silver Crucible's first, second, and third reward numbers are generation order,
not click order. `CardReward.Populate` runs before the outer rewards page is
shown, and `SilverCrucible.AfterModifyingCardRewardOptions` synchronously
increments its saved `TimesUsed`; snapshot that before/after transition to bind
the final ordered `_cards` options to the exact one-based use. Multiple rewards
such as Prayer Wheel can consume consecutive uses before either is opened. An
ordinary Driftwood reroll clears the old `CardCreationResult` objects and calls
`Populate` again with `IsCardReward`, so finalize the old set as all not taken
and register the rerolled set as the next Silver use. Compare result-object
references, not card ids or `CardModel` references: `ModifyCard` can replace the
displayed card while preserving its result object, and a successful deck add
removes that result object from `_cards`. Inner card-screen Skip remains
non-terminal; completed selection, outer `OnSkipped`, and pre-clear reroll are
the terminal outcome boundaries.
Persist the generated offer immediately as unresolved, then upsert its final
taken flags at a terminal boundary. On Core reload or Continue, rebind current
`CardReward` objects to same-floor unresolved screens by ordered card
id signature. Continued reward sets may regenerate different cards
because they serialize creation options rather than `_cards`; allow generation-
order fallback only inside that one `RewardsSet.GenerateWithoutOffering` batch,
never on an arbitrary later reward. Otherwise the already-advanced `TimesUsed`
counter makes those ordinals impossible to recover after the in-memory
reference map is cleared.
`CardReward.OnRelicObtained` can apply a newly obtained Silver Crucible to an
already-populated reward, but the game then calls `AfterModifyingRewards`
rather than `AfterModifyingCardRewardOptions`, so that free modification does
not consume `TimesUsed`. Keep it outside the numbered three-use ledger unless
the game changes its own counter behavior.

## Generated And Supplemental Cards

Not every visible card should become a permanent per-instance deck card.

Patterns already in use:

- Stable deck cards get normal instance ids.
- Removed deck cards keep stats and render via removed-card overlay.
- Combat-generated cards can get per-observed identities if they are actually played/tracked.
- Some generated cards are better represented as pooled deck-view summaries.

Examples:

- Shiv data is pooled under a synthetic deck-view Shiv overlay once a Shiv has been generated.
- Sovereign Blade gets a supplemental pooled deck-view overlay once forged/generated behavior makes it relevant.

Use pooled summaries when a card does not meaningfully exist as a stable deck resident and per-copy identities would mislead the user.

Enemy status-card pollution uses a source-window plus observed-result pattern:

- Patch the exact enemy-owned move or power trigger that creates the status
  card. Examples include `HauntedShip.HauntMove` and Entomancer's
  `PersonalHivePower.AfterDamageReceived` callback.
- While that owner-specific window is active, observe
  `CardPileCmd.AddGeneratedCardsToCombat` results.
- Count only successful `CardPileAddResult` entries whose `cardAdded.Type` is
  `CardType.Status`.
- Attribute the result to the enemy definition, not to a card or relic. Split
  by destination pile and by status card id so enemy hovers can answer what the
  enemy actually added.

Enemy damage dealt to the player can be observed from `DamageReceivedEntry`
when `Receiver.IsPlayer` and `Dealer.Monster` is present. Attribute this to the
enemy definition, not a card. Use `BlockedDamage + UnblockedDamage` as attempted
damage, `BlockedDamage` as blocked, and `UnblockedDamage` as dealt/effective.

## UI Timing And Tooltip Surfaces

Card stats are exposed through Godot UI patches, not through game combat state alone.

Important surfaces:

- `ViewStatsInjectorPatch` hooks `NCardsViewScreen.ConnectSignals`, gates to `NDeckViewScreen`, clones the existing View Upgrades tickbox, rewires duplicated node internals, persists preferences, and reinjects on hot reload if the deck view is already open. The master on/off control gates every SpireLens stats surface, while separate default-off controls gate card stats and monster hover stats. Gate both `NCardHolder.CreateHoverTips` and run-history deck-entry focus before aggregate lookup or tooltip construction when Card Stats is off. The Card Stats control is presentation-only and must not be wired to `DisableCardStatsDuringCombat`, which suppresses attribution itself.
- `StatsVisibilityHotkeyPatch` postfixes the stable Loader input node so hot-reloaded Core code can handle both keyboard and controller events. A standalone Left Shift tap and raw Right Stick (R3) press share the persisted master toggle and the same focus/overlay/transition/rebind guards. Left Stick press is the game's Peek action; R3 is absent from the shipped and saved controller action maps. Native Steam Input layouts must expose R3 as a virtual joypad button for the raw event to reach the mod.
- `NCardsViewScreen.ConnectSignals` calls its controller-state update before the SpireLens postfix. Controller mode hides the built-in `%Upgrades` tickbox, so clones of that subtree inherit `Visible=false` unless SpireLens explicitly restores visibility. Any injected deck controls cloned from View Upgrades must set their own visibility rather than inherit the source's controller-specific state.
- `CardHoverTooltipPatch` hooks `NCardHolder.CreateHoverTips` and `ClearHoverTips` to show/hide the SpireLens tooltip.
- Hand hovers are compact unless verbose hand stats are enabled.
- Deck view and other card-view hovers can show full lineage and stat breakdown.
- Tooltip aggregate display merges committed run data plus current pending combat so combat stats appear immediately.

Do not treat UI merged aggregate as proof that a combat has been saved. It is a presentation merge.

When modifying tooltip rows:

- Preserve compact-vs-full distinction.
- Keep rows self-describing without loud section headers unless they genuinely reduce confusion.
- Prefer game icon assets for recognizable concepts like block/draw/energy/stars/effects.
- Avoid creating instance numbers from hover-only preview/template cards.

## Persistence And Shape

`RunData` is the serialized shape. Changes must be additive so that older run files keep loading.

Current persistence facts:

- One run file per run under Godot `user://SpireLens/runs/`.
- File name is SpireLens run id, not the game's run-history file name.
- `GameStartTime` stores the game's run identifier (`RunManager._startTime`) so SpireLens can correlate with game run history and resume active runs after hot reload.
- `RunStorage.SaveAsync` serializes on the caller thread while `RunTracker` holds its lock, then writes on a background task.
- The on-disk shape is detected structurally, not by an explicit version number. The historic pooled shape (aggregates keyed by card definition id) is history-only because it cannot rebuild per-instance live state. Everything else is the current per-instance shape and is resumable across hot reload.
- Per-instance shape is identified by presence of `instance_numbers_by_def` or `def_counters` at the top level of the JSON file. Files predating per-instance identity lack both fields entirely.

For new persisted fields:

- keep them additive so old files deserialize with safe defaults,
- add a fixture under `Fixtures/RunSchema` capturing the new shape,
- update `SchemaLoadingTests` to assert the new shape loads and any new fields land where expected,
- update tooltip/tests if user-facing.

## Choosing A Hook

Use this decision order when adding a stat:

1. Prefer the narrowest reliable owner-specific hook for the mechanic being measured. See [ADR 0001](adr/0001-owner-specific-attribution-hooks.md).
2. Prefer an observed outcome hook over card text or intent.
3. If the observed outcome lacks source context, capture the source earlier and resolve it later through a narrow pending window.
4. If a high-level combat-history wrapper is tiny, beware JIT inlining; prefer a substantive `Hook.*` method or actual mutation point.
5. If the game method mutates a value, use prefix/postfix before/after snapshots to record actual delta.
6. If an action can be redirected, use a final post-mutation hook for the result.
7. If the outcome is heuristic, label the implementation and tooltip behavior as heuristic.
8. If no narrow source window exists, do not guess. Prefer no attribution or a pooled/unknown bucket.

Owner-specific means the game is invoking the card, relic, power, or mechanic
that owns the behavior, not merely reporting a nearby downstream fact. For
example, Book Repair Knife should start from
`BookRepairKnife.AfterDiedToDoom`, then treat the killed-creature payload as the
per-enemy trigger/value unit because the relic heals once per killed creature.
A global Doom-death observer is too broad unless there is no reliable
relic-owned callback.

For per-enemy-killed effects, do not equate "callback invoked once" with
"triggered once" unless the game mechanic really works once per callback. If an
owner callback receives three killed creatures and applies its effect per
creature, the user-facing trigger count is three. If the effect also changes a
resource, measure the actual resource delta around the owner-specific callback
when the attempted amount can diverge from the observed result.

Healing has its own attribution rule: track attempted, actually restored, and
lost healing separately, with lost-healing reason buckets such as `full_hp` and
specific blocker ids as they are discovered. See [ADR 0002](adr/0002-healing-attribution.md).

Good hook surfaces already proven useful:

- `CombatHistory.Add`: broad real-entry observation point. Caveat: does NOT see damage from combat-ending killing blows (see the Damage Attribution known trap).
- `Hook.AfterDamageGiven`: fires for every `DamageResult` including the killing hit that ends combat (game dispatches it directly, bypassing the combat-hook guard); the surface used to capture history-suppressed combat-ending damage.
- `Hook.AfterCardDrawn`: reliable card draw arrival.
- `Hook.ShouldDraw`: draw attempts and blocked draw modifier.
- `Hook.AfterCardChangedPiles`: final pile result.
- `PlayerCombatState.GainEnergy`: actual energy delta.
- `PlayerCombatState.GainStars`: actual star delta.
- `Hook.AfterForge`: actual forge gain/source.
- `Hook.BeforePowerAmountChanged`: attempted power application context.
- `Hook.ModifyPowerAmountReceived`: final modified power amount and blockers.
- `Hook.ShouldClearBlock`, `Hook.AfterBlockCleared`, `Hook.AfterPreventingBlockClear`: block expiry/waste window.
- `CardPile.AddInternal` filtered to Deck: permanent card entry.
- `CardPileCmd.RemoveFromDeck` prefix: permanent card removal.
- `CardModel.UpgradeInternal` postfix: upgrades from all sources.
- `RunManager.EnterMapPointInternal`: original map point entry, before `?`
  points resolve into concrete room types.
- Specific power/relic methods via `AccessTools.TypeByName`: useful when no public compile-time type is safe or when patching optional/specific models.

## Diagnostic Habits

When a new stat does not work, first determine which of these failed:

- The patch did not install.
- The target method never fires for this mechanic.
- The target fires but before/after timing is wrong.
- The outcome has no card source at that point.
- The source card is a combat clone and was not canonicalized.
- The event occurred outside `_pendingCombat`.
- The data is pending but tooltip reads only committed data.
- The data was recorded but shape/default/merge omitted it.
- The stat is correct but compact tooltip intentionally hides it.

`CoreMain.Initialize()` logs Harmony-patched methods for diagnostics. Use that list to confirm a hook exists before chasing tracker logic.

For source/context debugging, log compact identifiers that line up with JSON events: card id, instance id, card hash, `DeckVersion` status, creature id, history count, current/pending play, and current floor.

## Common Future-Agent Questions

Where do I start for a card stat?

- Find the observed runtime outcome first. Search patches for the closest hook. Then add a `RunTracker` record method and a persisted aggregate if needed.

Where do I start for a relic stat?

- Identify whether the relic has its own callback for the behavior first. If it does, prefer that owner-specific callback and decide whether callback count or payload-item count matches the mechanic's semantic trigger. For per-enemy effects, payload count is often the trigger/value unit. If the relic mutates a resource, record the actual observed delta when it can diverge. If no owner callback exists, identify whether the behavior fires at combat start, turn start, damage, block, draw, or resource mutation. Direct relic model callbacks are preferred when specific and stable; common observed hooks are fallbacks.

How do I know whether a stat belongs to a card instance, pooled generated summary, effect summary, or relic aggregate?

- Stable physical deck card: per-instance card aggregate.
- Combat-generated card with no meaningful deck identity: pooled generated summary or ephemeral aggregate, depending on UI semantics.
- Power/debuff whose later ticks matter: applied effect summary plus downstream ledger if reliably attributable.
- Relic-owned behavior: relic aggregate.

Can I save mid-combat state for reload?

- No, not under the current contract. Pending combat is intentionally not persisted. Keep mid-combat display as pending-only and commit at combat end.

Can I read card text to infer behavior?

- Avoid it. The project goal is observed outcomes. Text can guide which hook to investigate, but not be the source of truth when the game can diverge.

Can I assume `CardSource` is present?

- No. Direct card damage often has it; downstream power damage often does not. Poison is the canonical example of needing an ownership ledger.

Can I assume the current card play is still current?

- No. Some effects resolve after card play history has advanced. Use pending source context only with narrow windows.

Can I patch a private method?

- Yes. Harmony string-name patching and the Publicizer setup make private members accessible where needed. Still document why that method is stable enough to depend on.

Can I patch a tiny wrapper?

- Be suspicious. `CombatHistory.CardDrawn` was unreliable because it could be inlined. Prefer non-trivial hooks or mutation points.

## Maintenance Contract For This Document

Update this primer whenever you learn a durable runtime fact that a future agent would otherwise rediscover. Good candidates:

- a hook that proved reliable or unreliable,
- a timing window around async card/relic/power behavior,
- a source-context trap,
- a canonicalization/pile identity trap,
- a combat-history entry semantic,
- a UI lifecycle quirk,
- a persistence/resume invariant.

Do not turn this into a changelog. Keep it focused on stable game/runtime mechanics and the attribution implications of those mechanics.
