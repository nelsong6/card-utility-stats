using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class OldCoinStatsTests
{
    private const string OldCoinRelicId = "RELIC.OLD_COIN";

    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildOldCoinBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "BuildOldCoinBodyBBCode not found.");

    [Fact]
    public void FifoLedger_SpendsPreexistingGoldBeforeOldCoinGrant()
    {
        var ledger = new List<GoldAttributionChunk>();
        var agg = new RelicAggregate();

        RunTracker.BeginOldCoinGoldAttributionForTest(
            ledger,
            agg,
            initialGold: 100,
            currentGold: 400);

        Assert.Equal(300, agg.OldCoinGoldGranted);
        Assert.Collection(
            ledger,
            chunk =>
            {
                Assert.Null(chunk.SourceRelicId);
                Assert.Equal(100, chunk.AmountRemaining);
            },
            chunk =>
            {
                Assert.Equal(OldCoinRelicId, chunk.SourceRelicId);
                Assert.Equal(300, chunk.AmountRemaining);
            });

        var oldCoinSpent = RunTracker.ApplyGoldLossToAttributionForTest(
            ledger,
            initialGold: 400,
            currentGold: 250,
            countsAsSpent: true);

        Assert.Equal(50, oldCoinSpent);
        var remaining = Assert.Single(ledger);
        Assert.Equal(OldCoinRelicId, remaining.SourceRelicId);
        Assert.Equal(250, remaining.AmountRemaining);
    }

    [Fact]
    public void FifoLedger_PutsLaterGoldBehindOldCoinGrant()
    {
        var ledger = new List<GoldAttributionChunk>();
        var agg = new RelicAggregate();
        RunTracker.BeginOldCoinGoldAttributionForTest(
            ledger,
            agg,
            initialGold: 0,
            currentGold: 300);

        var oldCoinSpent = RunTracker.ApplyGoldLossToAttributionForTest(
            ledger,
            initialGold: 340,
            currentGold: 240,
            countsAsSpent: true);

        Assert.Equal(100, oldCoinSpent);
        Assert.Collection(
            ledger,
            chunk =>
            {
                Assert.Equal(OldCoinRelicId, chunk.SourceRelicId);
                Assert.Equal(200, chunk.AmountRemaining);
            },
            chunk =>
            {
                Assert.Null(chunk.SourceRelicId);
                Assert.Equal(40, chunk.AmountRemaining);
            });
    }

    [Fact]
    public void FifoLedger_LostGoldConsumesGrantWithoutCountingAsSpent()
    {
        var ledger = new List<GoldAttributionChunk>();
        var agg = new RelicAggregate();
        RunTracker.BeginOldCoinGoldAttributionForTest(
            ledger,
            agg,
            initialGold: 0,
            currentGold: 300);

        var oldCoinSpent = RunTracker.ApplyGoldLossToAttributionForTest(
            ledger,
            initialGold: 300,
            currentGold: 225,
            countsAsSpent: false);

        Assert.Equal(0, oldCoinSpent);
        var remaining = Assert.Single(ledger);
        Assert.Equal(225, remaining.AmountRemaining);
    }

    [Fact]
    public void RelicTooltip_OldCoin_ShowsSpentOverObservedGrant()
    {
        var body = BuildBody(new RelicAggregate
        {
            OldCoinGoldGranted = 300,
            OldCoinGoldSpent = 125,
        });

        Assert.Contains("Granted gold spent", body);
        Assert.Contains("[b]125/300[/b]", body);
        Assert.Contains("consumed by purchases", body);
    }

    [Fact]
    public void RelicTooltip_OldCoin_DispatchesForModel()
    {
        var relic = (OldCoin)RuntimeHelpers.GetUninitializedObject(typeof(OldCoin));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate(),
            floorCount: null,
            out var title,
            out _);

        Assert.True(recognized);
        Assert.Equal("Old Coin", title);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException(
                "BuildOldCoinBodyBBCode returned null."));
}
