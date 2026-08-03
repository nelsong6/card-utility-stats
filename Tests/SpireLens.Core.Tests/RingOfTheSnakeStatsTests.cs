using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class RingOfTheSnakeStatsTests
{
    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildRingOfTheSnakeBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildRingOfTheSnakeBodyBBCode not found.");

    [Fact]
    public void TrackingMath_AccumulatesObservedAndBlockedDraws()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordRingOfTheSnakeStatsForTest(
            agg, activations: 1, cardsRequested: 2, cardsDrawn: 2);
        RunTracker.RecordRingOfTheSnakeStatsForTest(
            agg, activations: 1, cardsRequested: 2, cardsDrawn: 1);
        RunTracker.RecordRingOfTheSnakeStatsForTest(
            agg, activations: -1, cardsRequested: -2, cardsDrawn: -2);

        Assert.Equal(2, agg.Activations);
        Assert.Equal(3, agg.AdditionalCardsDrawn);
        Assert.Equal(1, agg.AdditionalCardDrawsBlocked);
    }

    [Fact]
    public void Tooltip_ShowsActivationsAndObservedCardsDrawn()
    {
        var agg = new RelicAggregate
        {
            Activations = 3,
            AdditionalCardsDrawn = 5,
            AdditionalCardDrawsBlocked = 1,
        };

        var body = (string)(BuildBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildRingOfTheSnakeBodyBBCode returned null."));

        Assert.Contains("Activations", body);
        Assert.Contains("Ring of the Snake", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("drawn", body);
        Assert.Contains("[b]5[/b]", body);
        Assert.Contains("Card draws blocked", body);
        Assert.Contains("[b]1[/b]", body);
    }

    [Fact]
    public void TooltipDispatch_RecognizesRingOfTheSnake()
    {
        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            (RingOfTheSnake)RuntimeHelpers.GetUninitializedObject(
                typeof(RingOfTheSnake)),
            new RelicAggregate { Activations = 1, AdditionalCardsDrawn = 2 },
            floorCount: null,
            bloodSoakedRoseCurseAgg: null,
            cursedPearlCurseAgg: null,
            neowsBonesCurseAggs: null,
            storybookBrightestFlameAgg: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Ring of the Snake", title);
        Assert.Contains("[b]1[/b]", body);
        Assert.Contains("[b]2[/b]", body);
    }
}
