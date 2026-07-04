using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

/// <summary>
/// Pins the per-card block-ledger attribution (issue #6): absorbed damage is
/// credited FIFO (oldest block first) as TotalBlockEffective, block cleared
/// unused at end of turn is charged LIFO (newest survivor first) as
/// TotalBlockWasted, and for every card-attributed chunk
/// TotalBlockGained == TotalBlockEffective + TotalBlockWasted (the invariant the
/// CardHover tooltip relies on — it divides absorbed% and wasted% by
/// TotalBlockGained).
/// </summary>
public class BlockLedgerConservationTests
{
    [Fact]
    public void AbsorbedThenCleared_ConservesEachCardsBlock()
    {
        // A gains 5, then B gains 3 (ledger FIFO order: A, B).
        // 6 damage blocked → A absorbs all 5, B absorbs 1 (FIFO).
        // 2 block cleared unused at turn end → B's surviving 2 wasted (LIFO).
        var pending = RunTracker.RunBlockLedgerForTest(
            gains: new (string?, int)[] { ("CARD.DEFEND#1", 5), ("CARD.DEFEND#2", 3) },
            blockedDamage: 6,
            clearedUnusedBlock: 2);

        var a = pending.CombatAggregates["CARD.DEFEND#1"];
        var b = pending.CombatAggregates["CARD.DEFEND#2"];

        Assert.Equal(5, a.TotalBlockGained);
        Assert.Equal(5, a.TotalBlockEffective);
        Assert.Equal(0, a.TotalBlockWasted);
        Assert.Equal(3, b.TotalBlockGained);
        Assert.Equal(1, b.TotalBlockEffective);
        Assert.Equal(2, b.TotalBlockWasted);

        // The real invariant: gained == absorbed + wasted, per card.
        Assert.Equal(a.TotalBlockGained, a.TotalBlockEffective + a.TotalBlockWasted);
        Assert.Equal(b.TotalBlockGained, b.TotalBlockEffective + b.TotalBlockWasted);
    }

    [Fact]
    public void UnattributedChunk_IsConsumedButCreditsNoCard()
    {
        // A null cardInstanceId chunk models relic/innate block: it absorbs
        // damage (removing it from the ledger) but must not mint a phantom
        // aggregate for any card, nor credit TotalBlockGained.
        var pending = RunTracker.RunBlockLedgerForTest(
            gains: new (string?, int)[] { (null, 4), ("CARD.DEFEND#1", 4) },
            blockedDamage: 6,
            clearedUnusedBlock: 0);

        // Only the card chunk should appear; the null chunk credits nothing.
        Assert.True(pending.CombatAggregates.ContainsKey("CARD.DEFEND#1"));
        Assert.Single(pending.CombatAggregates);

        var a = pending.CombatAggregates["CARD.DEFEND#1"];
        Assert.Equal(4, a.TotalBlockGained);
        // 6 blocked: null chunk eats 4 (FIFO first), card eats 2.
        Assert.Equal(2, a.TotalBlockEffective);
    }

    [Fact]
    public void AllBlockClearedUnused_EachCardFullyWasted()
    {
        // No damage taken; the whole turn's block is cleared unused. With the
        // total cleared, both cards are fully wasted regardless of order — this
        // pins conservation on the all-wasted path (NOT LIFO ordering; see the
        // partial-clear test for that).
        var pending = RunTracker.RunBlockLedgerForTest(
            gains: new (string?, int)[] { ("CARD.DEFEND#1", 5), ("CARD.DEFEND#2", 3) },
            blockedDamage: 0,
            clearedUnusedBlock: 8);

        var a = pending.CombatAggregates["CARD.DEFEND#1"];
        var b = pending.CombatAggregates["CARD.DEFEND#2"];

        Assert.Equal(0, a.TotalBlockEffective);
        Assert.Equal(5, a.TotalBlockWasted);
        Assert.Equal(3, b.TotalBlockWasted);
        Assert.Equal(a.TotalBlockGained, a.TotalBlockEffective + a.TotalBlockWasted);
        Assert.Equal(b.TotalBlockGained, b.TotalBlockEffective + b.TotalBlockWasted);
    }

    [Fact]
    public void PartialClear_ChargesNewestSurvivorFirst_Lifo()
    {
        // The distinguishing case for LIFO. A gains 5, then B gains 3; no damage
        // taken; only 3 block cleared unused. LIFO charges the newest survivor
        // (B) first, so B is fully wasted and A is left untouched. Under FIFO
        // this would instead waste A's 3 and leave B untouched — so this test
        // fails if AttributeUnusedBlockLocked ever flips to forward iteration.
        var pending = RunTracker.RunBlockLedgerForTest(
            gains: new (string?, int)[] { ("CARD.DEFEND#1", 5), ("CARD.DEFEND#2", 3) },
            blockedDamage: 0,
            clearedUnusedBlock: 3);

        var a = pending.CombatAggregates["CARD.DEFEND#1"];
        var b = pending.CombatAggregates["CARD.DEFEND#2"];

        Assert.Equal(3, b.TotalBlockWasted); // newest survivor charged first
        Assert.Equal(0, a.TotalBlockWasted); // oldest untouched (would be 3 under FIFO)
    }

    [Fact]
    public void FullyResolvedLedger_IsEmptied()
    {
        // Every chunk either absorbed damage or was cleared, so the ledger
        // holds no leftover block afterward — no double-counting on the next
        // gain.
        var pending = RunTracker.RunBlockLedgerForTest(
            gains: new (string?, int)[] { ("CARD.DEFEND#1", 5), ("CARD.DEFEND#2", 3) },
            blockedDamage: 5,
            clearedUnusedBlock: 3);

        Assert.Empty(pending.PlayerBlockLedger);
    }
}
