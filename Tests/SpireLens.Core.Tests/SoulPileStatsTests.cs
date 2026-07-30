using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Cards;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class SoulPileStatsTests
{
    private static readonly MethodInfo AppendSoulPileStatsMethod =
        typeof(CardHoverShowPatch).GetMethod(
            "AppendSoulPileStats",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("AppendSoulPileStats not found.");

    [Fact]
    public void CardAggregate_SoulPileFields_DefaultToZero()
    {
        var agg = new CardAggregate();

        Assert.Equal(0, agg.SoulsAddedToDrawPile);
        Assert.Equal(0, agg.SoulsAddedToHand);
        Assert.Equal(0, agg.SoulsAddedToDiscardPile);
    }

    [Fact]
    public void RecordSoulAddedToPile_CountsOnlyTrackedDestinations()
    {
        var agg = new CardAggregate();

        RunTracker.RecordSoulAddedToPileForTest(agg, PileType.Draw, 4);
        RunTracker.RecordSoulAddedToPileForTest(agg, PileType.Hand, 2);
        RunTracker.RecordSoulAddedToPileForTest(agg, PileType.Discard, 3);
        RunTracker.RecordSoulAddedToPileForTest(agg, PileType.Exhaust, 9);
        RunTracker.RecordSoulAddedToPileForTest(agg, PileType.Draw, -1);

        Assert.Equal(4, agg.SoulsAddedToDrawPile);
        Assert.Equal(2, agg.SoulsAddedToHand);
        Assert.Equal(3, agg.SoulsAddedToDiscardPile);
    }

    [Fact]
    public void MergeAggregateInto_SoulPileFields_Accumulate()
    {
        var target = new CardAggregate
        {
            SoulsAddedToDrawPile = 1,
            SoulsAddedToHand = 2,
        };
        var source = new CardAggregate
        {
            SoulsAddedToDrawPile = 3,
            SoulsAddedToDiscardPile = 4,
        };

        RunTracker.MergeAggregateInto(target, source);

        Assert.Equal(4, target.SoulsAddedToDrawPile);
        Assert.Equal(2, target.SoulsAddedToHand);
        Assert.Equal(4, target.SoulsAddedToDiscardPile);
    }

    [Fact]
    public void CardTooltip_ShowsObservedSoulDestinations()
    {
        var agg = new CardAggregate
        {
            SoulsAddedToDrawPile = 4,
            SoulsAddedToHand = 2,
            SoulsAddedToDiscardPile = 3,
        };

        var sb = new StringBuilder();
        AppendSoulPileStatsMethod.Invoke(null, new object?[] { sb, agg });
        var body = sb.ToString();

        Assert.Contains("Souls added to draw pile", body);
        Assert.Contains("Souls added to hand", body);
        Assert.Contains("Souls added to discard pile", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]3[/b]", body);
    }
}
