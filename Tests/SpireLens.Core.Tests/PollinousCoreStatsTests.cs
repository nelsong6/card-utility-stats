using System;
using System.Reflection;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

/// <summary>
/// Pins Pollinous Core's observation and presentation math. Live hook timing
/// remains user-owned gameplay verification.
/// </summary>
public class PollinousCoreStatsTests
{
    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildPollinousCoreBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "BuildPollinousCoreBodyBBCode not found.");

    [Theory]
    [InlineData(5, 2, 7, 2)]
    [InlineData(5, 2, 6, 1)]
    [InlineData(5, 2, 5, 0)]
    [InlineData(8, 2, 8, 0)]
    public void ObservedContribution_UsesOnlyCardsBeyondCounterfactualHandDraw(
        int cardsRequestedWithoutPollinousCore,
        int maximumContribution,
        int totalCardsDrawn,
        int expected)
    {
        var actual =
            PollinousCoreCardPileDrawStatsPatch.CalculateObservedContributionForTest(
                cardsRequestedWithoutPollinousCore,
                maximumContribution,
                totalCardsDrawn);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TrackingMath_AccumulatesTurnsActivationsAndObservedDrawOutcomes()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordPollinousCoreCombatForTest(agg, 2);
        RunTracker.RecordPollinousCoreTurnEndForTest(agg, 0);
        RunTracker.RecordPollinousCoreTurnEndForTest(agg, 1);
        RunTracker.RecordPollinousCoreTurnEndForTest(agg, 2);
        RunTracker.RecordPollinousCoreTurnEndForTest(agg, 3);
        RunTracker.RecordPollinousCoreActivationForTest(agg);
        RunTracker.RecordPollinousCoreDrawResultForTest(
            agg,
            cardsRequested: 2,
            cardsDrawn: 1);

        Assert.Equal(1, agg.Activations);
        Assert.Equal(2, agg.PollinousCoreCombats);
        Assert.Equal(4, agg.PollinousCoreTurns);
        Assert.Equal(1, agg.PollinousCoreTurnsEndedOn0Counters);
        Assert.Equal(1, agg.PollinousCoreTurnsEndedOn1Counter);
        Assert.Equal(1, agg.PollinousCoreTurnsEndedOn2Counters);
        Assert.Equal(1, agg.PollinousCoreTurnsEndedOn3Counters);
        Assert.Equal(1, agg.AdditionalCardsDrawn);
        Assert.Equal(1, agg.AdditionalCardDrawsBlocked);
    }

    [Fact]
    public void MergeRelicAggregateInto_AccumulatesPollinousCoreFields()
    {
        var target = new RelicAggregate
        {
            Activations = 1,
            AdditionalCardsDrawn = 2,
            PollinousCoreTurns = 4,
            PollinousCoreCombats = 1,
            PollinousCoreTurnsEndedOn0Counters = 1,
            PollinousCoreTurnsEndedOn1Counter = 1,
            PollinousCoreTurnsEndedOn2Counters = 1,
            PollinousCoreTurnsEndedOn3Counters = 1,
        };
        var source = new RelicAggregate
        {
            Activations = 2,
            AdditionalCardsDrawn = 3,
            AdditionalCardDrawsBlocked = 1,
            PollinousCoreTurns = 7,
            PollinousCoreCombats = 2,
            PollinousCoreTurnsEndedOn0Counters = 2,
            PollinousCoreTurnsEndedOn1Counter = 2,
            PollinousCoreTurnsEndedOn2Counters = 2,
            PollinousCoreTurnsEndedOn3Counters = 1,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(3, target.Activations);
        Assert.Equal(5, target.AdditionalCardsDrawn);
        Assert.Equal(1, target.AdditionalCardDrawsBlocked);
        Assert.Equal(11, target.PollinousCoreTurns);
        Assert.Equal(3, target.PollinousCoreCombats);
        Assert.Equal(3, target.PollinousCoreTurnsEndedOn0Counters);
        Assert.Equal(3, target.PollinousCoreTurnsEndedOn1Counter);
        Assert.Equal(3, target.PollinousCoreTurnsEndedOn2Counters);
        Assert.Equal(2, target.PollinousCoreTurnsEndedOn3Counters);
    }

    [Fact]
    public void Tooltip_ShowsRequestedRowsAndAverages()
    {
        var agg = new RelicAggregate
        {
            Activations = 3,
            AdditionalCardsDrawn = 5,
            AdditionalCardDrawsBlocked = 1,
            PollinousCoreTurns = 10,
            PollinousCoreCombats = 2,
            PollinousCoreTurnsEndedOn0Counters = 2,
            PollinousCoreTurnsEndedOn1Counter = 3,
            PollinousCoreTurnsEndedOn2Counters = 3,
            PollinousCoreTurnsEndedOn3Counters = 2,
        };

        var body = (string)(BuildBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException(
                "BuildPollinousCoreBodyBBCode returned null."));

        Assert.Contains("Activations", body);
        Assert.Contains("Turns ended on 0 counters", body);
        Assert.Contains("Turns ended on 1 counter", body);
        Assert.Contains("Turns ended on 2 counters", body);
        Assert.Contains("Turns ended on 3 counters", body);
        Assert.Contains("Avg activations/combat", body);
        Assert.Contains("[b]1.5[/b]", body);
        Assert.Contains("Avg turns/combat", body);
        Assert.Contains("[b]5[/b]", body);
        Assert.Contains("Cards drawn", body);
        Assert.Contains("Card draws blocked", body);
        Assert.Contains("Avg cards drawn/combat", body);
        Assert.Contains("[b]2.5[/b]", body);
    }

    [Fact]
    public void TooltipDispatch_RecognizesPollinousCore()
    {
        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            new PollinousCore(),
            new RelicAggregate
            {
                Activations = 1,
                AdditionalCardsDrawn = 2,
                PollinousCoreTurns = 4,
                PollinousCoreCombats = 1,
            },
            floorCount: null,
            bloodSoakedRoseCurseAgg: null,
            cursedPearlCurseAgg: null,
            neowsBonesCurseAggs: null,
            storybookBrightestFlameAgg: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Pollinous Core", title);
        Assert.Contains("[b]1[/b]", body);
        Assert.Contains("[b]2[/b]", body);
    }
}
