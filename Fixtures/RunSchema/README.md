These fixture files pin on-disk run-file shapes that the mod has written. The
`v*-` prefix in each filename is historical — it dates the fixture to the
schema version that existed when it was added. Versions are no longer used at
runtime; the loader detects the per-instance vs. pooled shape structurally.
New fixtures added going forward do not need a `v*-` prefix.

- `v1-pooled-run.json`
  Pooled shape. Aggregates are keyed by card definition id and do not carry
  the per-instance resume metadata introduced later. Loads as historical-only;
  cannot rebuild live state.
- `v2-per-instance-run.json`
  Earliest per-instance shape. Aggregates are keyed by per-instance card id
  and include the resume-only snapshots (`instance_numbers_by_def`,
  `def_counters`) needed to rebuild numbering after hot reload.
- `v3-per-instance-effects-run.json`
  Per-instance shape extended with applied-effect summaries nested under each
  card aggregate.
- `v4-per-instance-effects-exhaust-run.json`
  Adds the per-card "times exhausted" count.
- `v5-per-instance-block-ledger-run.json`
  Adds absorbed/wasted block aggregates.
- `v6-per-instance-artifact-block-run.json`
  Adds per-effect Artifact-blocked debuff counters.
- `v7-per-instance-poison-damage-run.json`
  Adds per-effect downstream damage and overkill counters so dedicated poison
  rows can report observed poison outcomes, not just poison applied.
- `v8-per-instance-regent-stars-run.json`
  Adds Regent star-resource spend/gain fields alongside the existing energy
  and per-instance attribution data.
- `v9-per-instance-blocked-draw-run.json`
  Adds per-card blocked-draw attribution counts.
- `v9-per-instance-forge-run.json`
  Per-card forge granted tracking from the forge-tracking branch (added in
  parallel with the v9 blocked-draw work).
- `v10-per-instance-forge-run.json`
  Per-card forge granted tracking on top of the v9 blocked-draw fields.
- `v11-per-instance-no-draw-blocked-run.json`
  Adds per-effect downstream blocked-draw counts so powers like No Draw can
  report how many cards they actually prevented from being drawn.
- `v12-per-instance-draw-attempt-gap-run.json`
  Adds per-card attempted draw counts so draw cards can show what they tried
  to draw versus what actually landed.
- `v13-per-instance-blocked-draw-reasons-run.json`
  Adds categorized blocked-draw reasons so draw cards can say why missing
  draws were prevented.
- `v14-per-instance-make-it-so-run.json`
  Adds per-card summon-to-hand tracking for cards like `Make It So`.
- `v15-bag-of-marbles-run.json`
  Adds relic aggregate storage for Bag of Marbles combat-start Vulnerable
  tracking.
- `v16-red-mask-run.json`
  Adds Red Mask Weak tracking to relic aggregates.
- `v17-orichalcum-run.json`
  Adds Orichalcum additional block gained tracking to relic aggregates.
- `v18-pocketwatch-run.json`
  Adds Pocketwatch additional cards drawn tracking to relic aggregates.
- `v19-book-repair-knife-run.json`
  Adds Book Repair Knife confirmed per-enemy Doom trigger, kill-payload, and
  healing attribution tracking to relic aggregates.
- `happy-flower-energy-average-run.json`
  Adds Happy Flower held-combat tracking as the denominator for average energy
  generated per combat.
- `v20-bone-flute-run.json`
  Adds Bone Flute owned-Osty trigger tracking alongside actual block gained in
  relic aggregates.
- `v21-unleash-osty-hp-run.json`
  Adds Unleash-specific Osty current-HP attack bonus tracking to card
  aggregates.
- `v22-osty-summon-body-run.json`
  Adds card-sourced Osty summon HP to card aggregates and run-level Osty
  absorbed damage to meta stats.
- `v23-replay-extra-plays-run.json`
  Adds per-card replay extra-play tracking, where total plays still includes
  all plays and this field counts the subset with nonzero play-series index.
- `v24-replay-source-breakdown-run.json`
  Adds per-card replay extra-play source breakdowns, preserving both the total
  extra-play count and the observed source counts when the game exposes them.
- `meal-ticket-relic-run.json`
  Adds relic activation tracking plus Meal Ticket shop-entry healing
  attribution, including restored healing and full-HP lost healing.
- `burning-blood-relic-run.json`
  Adds Burning Blood combat-victory activation tracking and relic healing
  attribution, including restored healing and full-HP lost healing.
- `chosen-cheese-relic-run.json`
  Adds Chosen Cheese max-HP gain tracking: starting max HP at pickup plus
  observed max HP gained. It intentionally omits a resulting max-HP snapshot
  because unrelated max-HP effects can interleave between Chosen Cheese gains.
- `white-beast-statue-relic-run.json`
  Adds White Beast Statue potion-gained tracking with common, uncommon, and
  rare potion rarity splits, plus skipped White Beast potion reward tracking.
- `alchemize-card-run.json`
  Adds Alchemize potion procurement tracking with successful common, uncommon,
  and rare potion splits plus failed procure results.
- `debt-card-run.json`
  Adds Debt end-of-turn trigger tracking with observed gold lost and the
  unaffordable portion blocked by the player being out of gold.
- `seal-of-gold-relic-run.json`
  Adds Seal of Gold activation, observed gold-loss outcome, energy generated,
  and held-combat denominator tracking for its energy-per-combat average.
- `phylactery-relic-run.json`
  Adds Bound Phylactery and Phylactery Unbound activation tracking plus actual
  Osty summon HP gained from the shared summon command result.
- `v32-enemy-damage-run.json`
  Adds per-enemy observed damage output aggregates: attempted damage, HP damage
  dealt, and damage blocked by player block.
- `enemy-status-pollution-run.json`
  Adds run-level enemy aggregates for enemy damage dealt to the player and
  status cards that enemies actually add, split by destination pile and status
  card id.
- `open-branch-relic-stats-run.json`
  Adds consolidated relic aggregates rescued from stale open branches: Anchor,
  Letter Opener, Blood Vial, Akabeko, Booming Conch, Pendulum, Parrying
  Shield, Horn Cleat, and Toolbox.
- `letter-opener-relic-run.json`
  Adds Letter Opener combat, turn, and skill-play denominators for average
  attempted damage, plus turn-end 1/2 charge buckets.
- `bronze-scales-relic-run.json`
  Adds Bronze Scales Thorns damage tracking with observed damage, blocked
  damage, overkill, kills, and target count.
- `candelabra-relic-run.json`
  Adds Candelabra activation tracking, observed energy generated, and count of
  second player turns that ended with excess energy while it was held.
- `turn-energy-relics-run.json`
  Adds shared Lantern, Very Hot Cocoa, Candelabra, and Chandelier turn-energy
  relic tracking: activations, observed energy generated, matching turn-end
  excess-energy counts, and missed-energy combat counts for the turn-2/turn-3
  relics.
- `nunchaku-relic-run.json`
  Adds Nunchaku tracking: attacks played, observed energy gained, completed
  combats held for averages, combat-end 8/9 charge counts, and combat-end
  charge total for average charge.
- `pen-nib-relic-run.json`
  Adds Pen Nib tracking: total base damage added, attack-play count for the
  average base damage per attack, turn-end 8/9 charge counts, and turn-end
  charge samples for average charge.
- `iron-club-relic-run.json`
  Adds Iron Club tracking: actual cards drawn, completed combats held for the
  average cards-drawn rate, combat-end 0/1/2/3 charge counts, and explicit
  combat-end charge samples for average charge.
- `paels-wing-sacrifice-relic-run.json`
  Adds Pael's Wing sacrifice tracking: consumed card reward options split by
  common, uncommon, and rare, sacrifices made and skipped, and the total and
  specific artifacts gained from its completed sacrifice pairs.
- `paels-tooth-relic-run.json`
  Adds Pael's Tooth returned-card history in observed return order, preserving
  duplicate definitions, final display names, and post-return upgrade levels.
- `paels-eye-relic-run.json`
  Adds Pael's Eye activation tracking plus counts of status and curse cards
  actually exhausted by its extra-turn callback, and combats where it was held
  without activating.
- `strike-dummy-relic-run.json`
  Adds Strike Dummy tracked Strike-card plays since pickup plus current
  permanent-deck counts for base Strikes and non-base Strike-tagged cards.
- `unsettling-lamp-relic-run.json`
  Adds Unsettling Lamp debuff tracking: combats held, fixed Vulnerable and
  Weak totals, and dynamic per-effect buckets for other debuffs it doubles.
- `nutritious-soup-relic-run.json`
  Adds Nutritious Soup tracking for combats held plus finished plays of basic
  Strike-tagged cards carrying the Tezcataras Ember enchantment while the relic
  was held.
- `vajra-relic-run.json`
  Adds Vajra tracking for attack cards played while held plus actual enemy
  damage hits from those attacks, with multi-hit attacks counted per hit.
- `paper-phrog-relic-run.json`
  Adds Paper Phrog tracking for bonus damage added by its Vulnerable multiplier,
  vulnerable-enhanced attack counts, and held combat/turn denominators for
  average rows.
- `razor-tooth-relic-run.json`
  Adds Razor Tooth held combat/turn denominators plus later finished plays and
  successful draws of the exact combat cards it was observed upgrading.
- `storybook-relic-run.json`
  Adds Brightest Flame's observed max-HP-loss card aggregate, which Storybook
  surfaces through a definition-pooled view with play, draw, energy, and
  card-draw stats.
- `brilliant-scarf-relic-run.json`
  Adds Brilliant Scarf discount tracking: energy-discount offers, taken
  discounts, energy saved by those taken discounts, held combats, and
  discounted card cost buckets including dynamic star costs.
- `darkstone-periapt-relic-run.json`
  Adds Darkstone Periapt curse acquisition tracking plus observed max HP
  gained from the relic's owner-specific curse-to-deck callback, and
  original/new max-HP snapshots.
- `shovel-relic-run.json`
  Adds Shovel Dig tracking: total relics acquired plus common, uncommon, and
  rare rarity splits from the actual obtained relic instances, plus campfires
  where Dig was available but not used.
- `juzu-bracelet-relic-run.json`
  Adds Juzu Bracelet tracking for map `?` sites entered while the relic was
  held.
- `dowsing-rod-relic-run.json`
  Adds Dowsing Rod's current `?`-room countdown, derived from the saved Dowsing
  quest card state.
- `cursed-pearl-relic-run.json`
  Adds Cursed Pearl tracking for floors ascended before the first shop while
  held, plus the stats for the Greed curse granted by the relic.
- `gambling-chip-relic-run.json`
  Adds Gambling Chip combat-start discard tracking: combats triggered and
  cards actually discarded so the tooltip can show total and average discarded
  per combat.
- `centennial-puzzle-relic-run.json`
  Adds Centennial Puzzle activation tracking plus actual cards drawn so the
  tooltip can show total and average cards drawn per combat.
- `regal-pillow-relic-run.json`
  Adds Regal Pillow rest-site bonus healing tracking: activations, effective
  bonus HP healed, and bonus healing lost to full HP.
- `lizard-tail-relic-run.json`
  Adds Lizard Tail pickup and activation floor tracking plus observed HP healed
  by the one-shot revive.
- `pantograph-relic-run.json`
  Adds Pantograph boss-combat healing tracking: activations, effective HP
  healed, and healing wasted to full HP.
- `precarious-shears-relic-run.json`
  Adds Precarious Shears pickup tracking: removed card names plus max HP before
  and after the relic's max-HP cost, stored in the common original/new max-HP
  format as well as the legacy field names.
- `leafy-poultice-relic-run.json`
  Adds Leafy Poultice pickup tracking: original/new max HP after the relic's
  max-HP loss resolves, plus the two source/result card transform pairs.
- `fresnel-lens-relic-run.json`
  Adds Fresnel Lens's Drowning Beacon max-HP loss snapshots, successful Nimble
  card picks, any-Nimble / exact-two / three-or-more reward-screen counts, the
  no-Nimble count, and rewards where Nimble was offered but none was taken.
- `silver-crucible-relic-run.json`
  Adds Silver Crucible's ordered first, second, and third card-reward screens,
  including every offered card's displayed upgrade state and explicit
  taken/not-taken outcome.
- `strawberry-relic-run.json`
  Adds Strawberry pickup tracking: activations, observed max HP gained, and
  original/new max-HP snapshots.
- `pear-relic-run.json`
  Adds Pear pickup tracking: activations, observed max HP gained, and
  original/new max-HP snapshots.
- `mango-relic-run.json`
  Adds Mango pickup tracking: activations, observed max HP gained, and
  original/new max-HP snapshots.
- `nutritious-oyster-relic-run.json`
  Adds Nutritious Oyster pickup tracking: activations, observed max HP gained,
  and original/new max-HP snapshots.
- `stone-humidifier-relic-run.json`
  Adds Stone Humidifier rest-site trigger tracking: observed max HP gained and
  an ordered starting/resulting max-HP snapshot for every activation.
- `sturdy-clamp-relic-run.json`
  Adds Sturdy Clamp's observed retained block, pre-cap excess block, and the
  turn/combat denominators used by its four average rows.
- `paels-claw-relic-run.json`
  Adds Pael's Claw's finished Goopy-card plays, observed earned Goopy
  enhancements, enchanted-card count, and held turn/combat denominators.
- `sand-castle-relic-run.json`
  Adds Sand Castle pickup tracking: the actual cards upgraded by the relic.
- `fragrant-mushroom-relic-run.json`
  Adds Fragrant Mushroom pickup tracking: the actual cards upgraded by the relic.
- `fishing-rod-relic-run.json`
  Adds Fishing Rod tracking: every card actually upgraded at its three-combat
  interval, retained in upgrade order.
- `egg-relic-offers-run.json`
  Adds Molten, Toxic, and Frozen Egg tracking: every matching choosable card
  option the egg actually upgraded across rewards, shops, and other offers.
- `hefty-tablet-relic-run.json`
  Adds Hefty Tablet rare-card choice tracking: cards granted by id/name and
  skipped pickup choices.
- `arcane-scroll-relic-run.json`
  Adds Arcane Scroll rare-card grant tracking: the rare card added by id/name.
- `large-capsule-relic-run.json`
  Adds Large Capsule relic grant tracking: relics added by id/name.
- `neows-bones-relic-run.json`
  Adds Neow's Bones tracking: the two relics obtained by id/name plus the
  random curse added by id/name and its card stats.
- `vambrace-relic-run.json`
  Adds Vambrace activation tracking plus extra block gained from the block
  packets its 2x multiplier actually modified.
- `regalite-relic-run.json`
  Adds Regalite tracking for owner-created combat cards, observed block gained,
  and held turn/combat denominators for average block rows.
- `intimidating-helmet-relic-run.json`
  Adds Intimidating Helmet qualifying 2+-Energy card plays, observed block
  gained, and held turn/combat denominators for average block rows.
- `kunai-relic-run.json`
  Adds Kunai attack-counter tracking: owner attack plays, activations, observed
  Dexterity gained, and 1/2 charge turn-end buckets with average charge samples.
- `unlimited-attack-charge-relics-run.json`
  Adds the remaining unlimited three-Attack turn counters: Kusarigama,
  Ornamental Fan, and Shuriken, including observed damage/block/Strength
  outcomes and 1/2 charge turn-end buckets with average charge samples.
- `tuning-fork-relic-run.json`
  Adds Tuning Fork owner Skill-play count, trigger count, observed block
  gained, held combat/turn denominators, and turn-end charge buckets.
- `mummified-hand-relic-run.json`
  Adds Mummified Hand trigger costs, observed card discounts, energy-spend to
  discounted-cost ratios, held combat/turn denominators, and recipient types.
- `ripple-basin-relic-run.json`
  Adds Ripple Basin no-attack turn-end activation tracking plus observed block
  gained.
- `war-paint-relic-run.json`
  Adds War Paint pickup tracking: the actual skill cards upgraded by the relic.
- `unmovable-power-meta-run.json`
  Adds run-level Unmovable power tracking for the extra block produced by the
  power's block multiplier, surfaced on Unmovable card tooltips rather than
  attributed to one physical card instance.

Why these exist:

- new shape work should be validated against real checked-in examples, not memory
- pooled vs. per-instance is not a lossless migration, so the old pooled shape
  needs to stay visible when changing loader behavior
- additive follow-on shapes still need fixture coverage so "old but resumable"
  behavior stays intentional
- future tests can read these files directly without having to reconstruct old
  JSON by hand
