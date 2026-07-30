using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

/// <summary>
/// Pins Joss Paper's threshold, observed draw, denominator, and presentation
/// math. Live hook timing remains user-owned gameplay verification.
/// </summary>
public class JossPaperStatsTests
{
    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildJossPaperBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "BuildJossPaperBodyBBCode not found.");

    [Theory]
    [InlineData(4, 5, 0)]
    [InlineData(5, 5, 1)]
    [InlineData(9, 5, 1)]
    [InlineData(10, 5, 2)]
    [InlineData(10, 0, 0)]
    public void ActivationCount_UsesEveryCompletedThreshold(
        int cardsExhausted,
        int threshold,
        int expected)
    {
        Assert.Equal(
            expected,
            JossPaperDrawIfThresholdMetStatsPatch
                .CalculateActivationCountForTest(cardsExhausted, threshold));
    }

    [Fact]
    public void TrackingMath_AccumulatesExhaustsTurnsAndObservedDrawOutcomes()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordJossPaperCombatForTest(agg, 2);
        RunTracker.RecordJossPaperCardsExhaustedForTest(agg, 11);
        RunTracker.RecordJossPaperActivationForTest(agg, 2);
        RunTracker.RecordJossPaperDrawResultForTest(
            agg,
            cardsRequested: 2,
            cardsDrawn: 1);
        for (var counter = 0; counter <= 4; counter++)
            RunTracker.RecordJossPaperTurnEndForTest(agg, counter);

        Assert.Equal(2, agg.Activations);
        Assert.Equal(11, agg.JossPaperCardsExhausted);
        Assert.Equal(2, agg.JossPaperCombats);
        Assert.Equal(5, agg.JossPaperTurns);
        Assert.Equal(1, agg.JossPaperTurnsEndedOn0Counters);
        Assert.Equal(1, agg.JossPaperTurnsEndedOn1Counter);
        Assert.Equal(1, agg.JossPaperTurnsEndedOn2Counters);
        Assert.Equal(1, agg.JossPaperTurnsEndedOn3Counters);
        Assert.Equal(1, agg.JossPaperTurnsEndedOn4Counters);
        Assert.Equal(1, agg.AdditionalCardsDrawn);
        Assert.Equal(1, agg.AdditionalCardDrawsBlocked);
    }

    [Fact]
    public void MergeRelicAggregateInto_AccumulatesJossPaperFields()
    {
        var target = PopulatedAggregate();
        var source = PopulatedAggregate();

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(6, target.Activations);
        Assert.Equal(10, target.AdditionalCardsDrawn);
        Assert.Equal(2, target.AdditionalCardDrawsBlocked);
        Assert.Equal(22, target.JossPaperCardsExhausted);
        Assert.Equal(20, target.JossPaperTurns);
        Assert.Equal(4, target.JossPaperCombats);
        Assert.Equal(4, target.JossPaperTurnsEndedOn0Counters);
        Assert.Equal(6, target.JossPaperTurnsEndedOn1Counter);
        Assert.Equal(6, target.JossPaperTurnsEndedOn2Counters);
        Assert.Equal(4, target.JossPaperTurnsEndedOn3Counters);
        Assert.Equal(0, target.JossPaperTurnsEndedOn4Counters);
    }

    [Fact]
    public void Tooltip_ShowsThresholdCounterAndDrawRows()
    {
        var body = (string)(BuildBodyMethod.Invoke(
            null,
            new object?[] { PopulatedAggregate() })
            ?? throw new InvalidOperationException(
                "BuildJossPaperBodyBBCode returned null."));

        Assert.Contains("ended after Joss Paper activated and reset its counter to zero", body);
        Assert.Contains("ended with Joss Paper showing one", body);
        Assert.Contains("ended with Joss Paper showing two", body);
        Assert.Contains("ended with Joss Paper showing three", body);
        Assert.Contains("ended with Joss Paper showing four", body);
        Assert.Contains("Average activations per combat", body);
        Assert.Contains("[b]1.5[/b]", body);
        Assert.Contains("Average turns per combat", body);
        Assert.Contains("[b]5[/b]", body);
        Assert.Contains("Cards drawn — Joss Paper cards that actually reached the hand.", body);
        Assert.Contains("Joss Paper draws prevented by draw limits", body);
        Assert.Contains("Average cards drawn per combat", body);
        Assert.Contains("[b]2.5[/b]", body);
        Assert.Contains("exhaust_pile.png", body);
    }

    [Fact]
    public void TooltipDispatch_RecognizesJossPaper()
    {
        var relic = (JossPaper)RuntimeHelpers.GetUninitializedObject(
            typeof(JossPaper));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            PopulatedAggregate(),
            floorCount: null,
            bloodSoakedRoseCurseAgg: null,
            cursedPearlCurseAgg: null,
            neowsBonesCurseAggs: null,
            storybookBrightestFlameAgg: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Joss Paper", title);
        Assert.Contains("[b]3[/b]", body);
    }

    private static RelicAggregate PopulatedAggregate()
    {
        return new RelicAggregate
        {
            Activations = 3,
            AdditionalCardsDrawn = 5,
            AdditionalCardDrawsBlocked = 1,
            JossPaperCardsExhausted = 11,
            JossPaperTurns = 10,
            JossPaperCombats = 2,
            JossPaperTurnsEndedOn0Counters = 2,
            JossPaperTurnsEndedOn1Counter = 3,
            JossPaperTurnsEndedOn2Counters = 3,
            JossPaperTurnsEndedOn3Counters = 2,
        };
    }
}
