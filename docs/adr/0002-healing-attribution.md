# 0002. Healing Attribution Tracks Attempted, Restored, And Lost Healing

Date: 2026-06-28

Status: Accepted

## Context

Healing text usually describes an attempted amount, but the run value is the HP
actually restored. Healing can be wasted because the target is already near or
at full HP, and future mechanics may reduce, prevent, redirect, or otherwise
modify healing. If SpireLens only records the requested amount, healing relics
and cards look better than they were.

Book Repair Knife is the first concrete case. Its owner-specific callback
receives the Doom-death payload and requests `HealVar * killedCreatureCount`.
That requested amount can exceed missing HP, so the observed result may be
smaller than the requested heal.

## Decision

Healing attribution must separate:

1. attempted healing: what the owner mechanic requested
2. restored healing: HP actually gained by the target
3. lost healing: attempted minus restored
4. lost-healing reasons: stable buckets explaining why healing did not land

Record attempted healing from the owner-specific callback while source context
is still clear. Record restored healing from an observed HP-change hook or a
before/after target HP snapshot. Do not use attempted healing as restored
healing.

Lost healing must be reasoned, not collapsed into one anonymous waste number.
Known buckets:

- `full_hp`: attempted healing exceeded the target's missing HP
- `other`: the remaining gap when actual restored healing is below the amount
  that could have fit under max HP; this covers prevention/modification until a
  more specific blocker hook is identified

When a future mechanic exposes a specific healing blocker or modifier, add a
new stable reason id for that blocker rather than folding it into `other`.

## Consequences

Healing stats require a pending attribution window. The owner callback provides
the requested amount and target; the HP-change observation supplies the actual
restored amount; finalization computes lost healing and reason buckets after
the heal task has resolved.

Tooltip rows should emphasize restored healing and lost healing. Attempted
healing is useful for audits and derived calculations, but it should not crowd
the tooltip unless it adds clarity.
