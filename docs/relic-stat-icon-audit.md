# Relic Stat Icon Audit

This is the migration map for replacing repeated relic-stat vocabulary with
tooltip-backed symbols. It is intentionally an audit of presentation, not a
change to tracking or persisted data.

This audit is limited to relic-stat popups. “Card” rows below are relic stats
about cards, such as cards upgraded or cards offered. Card-stat popups are
explicitly tabled until they have a mouse-accessible pinning interaction.

## Current surface

A static scan of `Core/Patches/RelicHoverTooltipPatch.cs` found:

- 371 `Row3` call sites, plus a small number of flow and described-row call sites
- 290 rows whose labels are string literals and can be classified mechanically
- 214 unique literal labels
- additional dynamic labels built from effect names, card names, relic-specific
  values, and helper functions

The literal rows contain these reusable concepts:

| Concept | Candidate rows |
|---|---:|
| Card / cards | 63 |
| Activation or trigger | 59 |
| Average | 56 |
| Combat context | 43 |
| Upgrade | 30 |
| Charge | 27 |
| Attack | 23 |
| Damage or hit | 21 |
| Turn context | 19 |
| Healing or HP | 14 |
| Draw | 12 |
| Strength | 9 |
| Gold | 4 |
| Exhaust | 4 |
| Floor context | 3 |

These counts overlap: for example, “Avg activations per combat” belongs to
average, activation, and combat.

The file also has existing native-icon helpers at 33 block call sites, 17
energy call sites, and 2 draw call sites. Those are strong candidates for
moving into the shared glossary rather than maintaining a second icon path.

## Row grammar

Each migrated row should have four distinct parts:

1. a left-side information symbol whose hint explains the entire row
2. zero or more concept symbols, each with its own glossary hint
3. only the wording that is specific to this metric
4. the value

For example, Sturdy Clamp’s “avg block retained per turn” becomes:

`ⓘ  [average] [block] [turn] retained  1.8`

The information hint says “Average block retained by Sturdy Clamp per turn.”
The three concept hints independently define average, block, and turn.

Rows should normally use no more than three concept symbols. Proper names,
card names, reason names, ordered reward histories, and other one-off content
should remain text.

## Migration order

### Available now

- activation / trigger
- average
- block
- card
- charge
- combat
- floor
- healing gained
- healing blocked
- information
- turn
- upgraded
- relic-specific Osty summon gained

### Next native concepts to add

- energy
- draw
- damage / hit
- HP and max HP
- strength
- gold
- attack, skill, and power
- exhaust
- card reward

### Rollout

1. Prove the multi-concept row grammar on Sturdy Clamp.
2. Convert exact repeated rows such as Activations, Times triggered, and
   average-per-turn/combat rows.
3. Move the existing block, energy, and draw helper icons into the glossary.
4. Convert context-sensitive and dynamically named rows explicitly, with a
   full sentence for every information hint.
5. Leave bespoke history and proper-name rows as text, but still give each one
   a left-side information hint.

Progress:

- activation and trigger totals now use the activation symbol and a full-row
  information hint throughout the relic-stat renderer
- Sturdy Clamp is the initial multi-concept example

The information description should be authored at the call site. It should
not be generated from the visible label: the full sentence often needs to say
what was counted, what the denominator is, and whether the value is attempted,
successful, blocked, current-combat, or lifetime.
