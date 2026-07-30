using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class BagOfPreparationStatsTests
{
    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildBagOfPreparationBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildBagOfPreparationBodyBBCode not found.");

    [Theory]
    [InlineData(5, 2, 7, 2)]
    [InlineData(5, 2, 6, 1)]
    [InlineData(5, 2, 5, 0)]
    [InlineData(7, 0, 7, 0)]
    [InlineData(5, 2, 0, 0)]
    public void ObservedContribution_UsesOnlyCardsBeyondCounterfactualHandDraw(
        int cardsRequestedWithoutBag,
        int maximumBagContribution,
        int totalCardsDrawn,
        int expected)
    {
        var actual = BagOfPreparationCardPileDrawPatch.CalculateObservedContributionForTest(
            cardsRequestedWithoutBag,
            maximumBagContribution,
            totalCardsDrawn);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TrackingMath_AccumulatesOnlyPositiveActivationAndDrawObservations()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordBagOfPreparationStatsForTest(agg, activations: 1, cardsDrawn: 2);
        RunTracker.RecordBagOfPreparationStatsForTest(agg, activations: 1, cardsDrawn: 1);
        RunTracker.RecordBagOfPreparationStatsForTest(agg, activations: -1, cardsDrawn: -2);

        Assert.Equal(2, agg.Activations);
        Assert.Equal(3, agg.AdditionalCardsDrawn);
    }

    [Fact]
    public void Tooltip_ShowsActivationsAndObservedCardsDrawn()
    {
        var agg = new RelicAggregate
        {
            Activations = 3,
            AdditionalCardsDrawn = 5,
        };

        var body = (string)(BuildBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildBagOfPreparationBodyBBCode returned null."));

        Assert.Contains("Activations", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("drawn", body);
        Assert.Contains("[b]5[/b]", body);
    }

    [Fact]
    public void TooltipDispatch_RecognizesBagOfPreparation()
    {
        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            (BagOfPreparation)RuntimeHelpers.GetUninitializedObject(
                typeof(BagOfPreparation)),
            new RelicAggregate { Activations = 1, AdditionalCardsDrawn = 2 },
            floorCount: null,
            bloodSoakedRoseCurseAgg: null,
            cursedPearlCurseAgg: null,
            neowsBonesCurseAggs: null,
            storybookBrightestFlameAgg: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Bag of Preparation", title);
        Assert.Contains("[b]1[/b]", body);
        Assert.Contains("[b]2[/b]", body);
    }
}
