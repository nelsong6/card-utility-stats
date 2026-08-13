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

- every ordinary relic-stat row now receives a left-side information hint
- every ordinary relic-stat row is matched against all currently available
  glossary concepts; recognized repeated wording is replaced with the
  corresponding hinted symbols in its original order
- existing non-glossary native icons such as Energy, Draw, Vulnerable, Weak,
  and effect icons remain visible alongside the glossary symbols
- explicitly authored concept rows remain available for descriptions that need
  more specificity than the shared row vocabulary

The shared vocabulary supplies a readable fallback information description for
every row. Rows whose meaning depends on attempted versus successful outcomes,
blocked values, current-combat state, or another special denominator should
override that fallback with an authored sentence at the call site.

## Faithfulness audit

Every stat-row call site with a literal label was run through
`RelicStatRowVocabulary.Create` and compared against the concepts its own
wording mentions: 887 row call sites, 620 literal labels, 459 distinct labels.

### Rule: a multi-word rule needs a matching multi-word symbol

Concept rules are applied in list order and each match removes its span, so an
earlier rule that spans two concepts suppresses the later rule for the word it
swallowed. That is correct only when the surviving symbol carries both meanings
— `strength gained` → `strength_gained`, `block wasted` → `block_wasted`,
`common potions` → `potion_common`, `max HP` → `max_hp`.

The `taken` rule broke that rule: it spanned `floors? acquired`, so "Floor
acquired" rendered a lone Taken symbol and silently dropped its Floor icon,
while the adjacent "Floor activated" row in the same Lizard Tail tooltip kept
one. The rule now matches only the verb. When adding a multi-word rule, confirm
a combined symbol exists for the whole phrase.

### Open: the row wording, the symbol, and the hint use different words

These rows are correct in what they *count*; the drift is vocabulary. In each
case the row says one word, the symbol's glossary label says another, and the
ⓘ hint repeats the row's word — so a player hovering the symbol reads a term
they never saw in the row. Counts are distinct labels.

| Row says | Symbol says | Rows |
|---|---|---:|
| HP lost | Damage | 8 |
| triggered / trigger / triggers | Activation | 11 |
| Commons / common | Card (no `card_common` concept exists) | 9 |
| events | Unknown room | 4 |
| acquired | Taken | 4 |
| shop / shops | Merchant | 5 |
| HP healed / HP restored | Healing gained | 5 |
| slain | Kill | 2 |
| rest site / rest sites | Campfire | 2 |

Two of these are internal contradictions rather than synonyms. `card_common` is
absent while `card_rare` and `card_uncommon` exist, so "Commons picked" and
"Cards picked" render identically. And the map legend already says "Merchants
visited" and "Rest sites visited" in the same tooltips whose symbols are
labelled Merchant and Campfire, while relic rows elsewhere say "Shops skipped".

### Open: authored hints that never say what their symbols assert

Ten rows pass an authored `fullDescription` whose sentence never uses the word
its symbols stand for — for example "Common Attacks taken" hinting "Common
Attacks selected from Splash", or Girya's "Lift" carrying an Activation symbol
in a sentence that only says "successful Lift". The symbols are right; the
sentences should reuse their words.
