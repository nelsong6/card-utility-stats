using System.Collections.Generic;
using Xunit;

namespace SpireLens.Core.Tests;

public class BufferChargeLedgerTests
{
    private const string CardA = "CARD.BUFFER#1";
    private const string CardB = "CARD.BUFFER#2";

    [Fact]
    public void SpendsChargesOldestFirstAcrossSources()
    {
        var potionHistory = new List<PotionRunHistoryEntry>
        {
            new() { Sequence = 3, PotionId = "POTION.LUCKY_TONIC" },
        };

        // Card A grants 1, then Lucky Tonic grants 2. Three hits land.
        var pending = RunTracker.RunBufferLedgerForTest(
            grants:
            [
                (CardA, null, 1),
                (null, 3, 2),
            ],
            spends: [10m, 7m, 5m],
            potionHistory: potionHistory);

        var cardAgg = pending.CombatAggregates[CardA];
        Assert.Equal(1, cardAgg.BufferChargesUsed);
        Assert.Equal(10m, cardAgg.BufferDamagePrevented);

        var potion = potionHistory[0];
        Assert.Equal(2, potion.BufferChargesUsed);
        Assert.Equal(12m, potion.BufferDamagePrevented);
    }

    [Fact]
    public void UnspentChargesCreditNoPrevention()
    {
        var pending = RunTracker.RunBufferLedgerForTest(
            grants: [(CardA, null, 3)],
            spends: [8m]);

        var cardAgg = pending.CombatAggregates[CardA];
        Assert.Equal(3, cardAgg.BufferChargesGranted);
        Assert.Equal(1, cardAgg.BufferChargesUsed);
        Assert.Equal(8m, cardAgg.BufferDamagePrevented);
    }

    [Fact]
    public void SpendsBeyondTheLedgerCreditNobody()
    {
        var pending = RunTracker.RunBufferLedgerForTest(
            grants: [(CardA, null, 1)],
            spends: [6m, 9m]);

        var cardAgg = pending.CombatAggregates[CardA];
        Assert.Equal(1, cardAgg.BufferChargesUsed);
        Assert.Equal(6m, cardAgg.BufferDamagePrevented);
    }

    [Fact]
    public void SecondCopyOwnsOnlyItsOwnCharges()
    {
        var pending = RunTracker.RunBufferLedgerForTest(
            grants:
            [
                (CardA, null, 1),
                (CardB, null, 1),
            ],
            spends: [4m, 11m]);

        Assert.Equal(4m, pending.CombatAggregates[CardA].BufferDamagePrevented);
        Assert.Equal(11m, pending.CombatAggregates[CardB].BufferDamagePrevented);
    }
}
