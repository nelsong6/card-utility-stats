# 0001. Prefer Owner-Specific Attribution Hooks

Date: 2026-06-27

Status: Accepted

## Context

SpireLens tracks what a card or relic actually caused in a run. That can tempt
an implementation to hook the first observable downstream outcome: a death, a
damage entry, a pile movement, a resource mutation, or a broad `Hook.*` callback.

Those hooks can be factually real while still being semantically too broad. A
global Doom-death hook, for example, can prove that a creature died to Doom, but
it does not by itself prove which relic or card received, owned, amplified, or
rewarded that outcome. A user who understands both the game and the code can
catch that mismatch, but the design should not rely on the user re-explaining
this principle every time a new stat is added.

## Decision

When adding attribution for a card, relic, power, or other mechanic, prefer the
narrowest reliable hook owned by that mechanic before falling back to generic
outcome observation.

Use this order of preference:

1. Patch the mechanic's own trigger callback when it exists.
2. If the mechanic mutates a resource, pile, power, or counter, measure the
   actual before/after delta while that mechanic-specific callback is resolving.
3. If the owner callback reports a payload, determine whether the mechanic is
   semantically per callback invocation or per payload item. Do not assume a
   batched callback means one trigger.
4. Use generic observed outcomes only when the owner-specific hook does not
   exist, does not fire reliably, or does not expose enough context.
5. If the implementation must use a generic hook, document the attribution
   window and why it is narrow enough to be trusted.

For "per enemy killed" or similar per-entity effects, the payload item count is
usually the trigger count/value unit. The game may batch multiple killed
creatures into one owner callback for convenience, but if the effect applies
once per creature, the user-facing stat should count creatures, not callback
invocations.

For example, Book Repair Knife should be tracked from
`BookRepairKnife.AfterDiedToDoom`, because that is the relic-specific callback
the game invokes after Doom deaths. Its implementation counts the creatures in
that callback and heals `HealVar * count`, so each killed creature is the
meaningful trigger/value unit. A tooltip row such as "Doom kills" is better than
showing callback invocations as "Times triggered", because one callback carrying
three killed creatures still represents three relic effects.

If a per-entity mechanic also produces a resource, record the actual resource
delta when it can diverge from the requested amount. For Book Repair Knife, that
would mean measuring actual HP restored around the relic-owned heal, not merely
displaying `HealVar * killCount`.

Avoid counting all Doom deaths globally and assuming Book Repair Knife mattered.
That records a real game event, but not a relic-owned attribution fact.

## Consequences

This costs more up-front runtime inspection. Implementers may need to reflect
over game assemblies, inspect callback signatures, or add short-lived
diagnostics before writing the tracker path.

The payoff is that stats stay owner-specific and explainable. Tooltip rows can
say what the card or relic itself did, not merely what happened nearby.

This also means a single mechanic may need multiple counters, but only when
they represent distinct user-facing truths. Callback invocation count, payload
item count, semantic trigger count, and resource delta are not automatically
interchangeable. Choose the row that matches the mechanic, and avoid surfacing
implementation-shaped counters that make the stat harder to read.
