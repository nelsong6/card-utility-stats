# 0003. Surface Meta-Stats On Related Cards Without Claiming Per-Card Ownership

Date: 2026-06-28

Status: Accepted

## Context

Some stats are useful when shown on a card, but are not facts caused by that
specific physical card instance. SpireLens already has examples of this pattern:
supplemental deck-view cards like Shiv and Sovereign Blade summarize all usage
of that generated or synthetic card family rather than one stable deck copy.

Necrobinder's Osty stats need the same distinction. A card that summons Osty can
own the HP it actually added through `OstyCmd.Summon`. Later damage absorbed by
Osty is different: it describes the overall Osty body across the run, not the
last card that happened to summon or revive it.

## Decision

Use meta-stats for run-level mechanic facts that have a natural card UI home but
are not per-card attribution facts.

Rules:

1. Store the value outside the per-card aggregate when the stat covers all
   instances or all occurrences of a mechanic.
2. Surface the value on related cards or supplemental cards when that is where a
   player naturally looks for the mechanic.
3. Keep card-owned contribution stats separate from meta-stats.
4. Tooltip wording should avoid implying that the hovered card caused the meta
   value.

For Osty:

- `TimesOstySummoned` and `TotalOstyHpSummoned` belong to the source card that
  successfully called `OstyCmd.Summon`.
- `TotalOstyDamageAbsorbed` belongs to run-level meta stats and may be surfaced
  on Osty summon cards as a related mechanic stat.
- Unleash's Osty HP damage bonus remains on Unleash because Unleash itself uses
  Osty's current HP at play time.

## Consequences

This avoids inventing false ownership ledgers. A card can still show a rich
story about its mechanic family, but the persisted shape and tooltip labels keep
the difference between "this card caused this" and "this related mechanic did
this across the run."

Future stats should explicitly choose among per-card aggregate, effect/relic
aggregate, supplemental pooled card aggregate, and run-level meta stat before
adding fields.
