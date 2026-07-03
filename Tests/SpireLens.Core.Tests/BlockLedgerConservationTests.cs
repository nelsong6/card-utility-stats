using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

/// <summary>
/// Pins the per-card block-ledger attribution (issue #6): absorbed damage is
/// credited FIFO (oldest block first) as TotalBlockEffective, block cleared
/// unused at end of turn is charged LIFO (newest survivor first) as
/// TotalBlockWasted, and for every card-attributed chunk
/// effective + wasted == the block it contributed.
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

        Assert.Equal(5, a.TotalBlockEffective);
        Assert.Equal(0, a.TotalBlockWasted);
        Assert.Equal(1, b.TotalBlockEffective);
        Assert.Equal(2, b.TotalBlockWasted);

        // Conservation: each card's gained block == absorbed + wasted.
        Assert.Equal(5, a.TotalBlockEffective + a.TotalBlockWasted);
        Assert.Equal(3, b.TotalBlockEffective + b.TotalBlockWasted);
    }

    [Fact]
    public void UnattributedChunk_IsConsumedButCreditsNoCard()
    {
        // A null cardInstanceId chunk models relic/innate block: it absorbs
        // damage (removing it from the ledger) but must not mint a phantom
        // aggregate for any card.
        var pending = RunTracker.RunBlockLedgerForTest(
            gains: new (string?, int)[] { (null, 4), ("CARD.DEFEND#1", 4) },
            blockedDamage: 6,
            clearedUnusedBlock: 0);

        // Only the card chunk should appear; the null chunk credits nothing.
        Assert.True(pending.CombatAggregates.ContainsKey("CARD.DEFEND#1"));
        Assert.Single(pending.CombatAggregates);

        // 6 blocked: null chunk eats 4 (FIFO first), card eats 2.
        Assert.Equal(2, pending.CombatAggregates["CARD.DEFEND#1"].TotalBlockEffective);
    }

    [Fact]
    public void ClearedBlockChargesNewestSurvivorFirst()
    {
        // No damage taken; the whole turn's block is cleared unused. LIFO
        // means the last card to contribute is charged first — here both are
        // fully wasted, so each card's waste equals its own contribution.
        var pending = RunTracker.RunBlockLedgerForTest(
            gains: new (string?, int)[] { ("CARD.DEFEND#1", 5), ("CARD.DEFEND#2", 3) },
            blockedDamage: 0,
            clearedUnusedBlock: 8);

        Assert.Equal(0, pending.CombatAggregates["CARD.DEFEND#1"].TotalBlockEffective);
        Assert.Equal(5, pending.CombatAggregates["CARD.DEFEND#1"].TotalBlockWasted);
        Assert.Equal(3, pending.CombatAggregates["CARD.DEFEND#2"].TotalBlockWasted);
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
