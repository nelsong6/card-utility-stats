using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class SignetRingStatsTests
{
    private const string SignetRingRelicId = "RELIC.SIGNET_RING";

    private static readonly MethodInfo BuildSignetRingBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildSignetRingBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildSignetRingBodyBBCode not found.");

    [Fact]
    public void RelicAggregate_SignetRingField_DefaultsToUnset()
    {
        var agg = new RelicAggregate();

        Assert.Null(agg.FloorsTraveledUntilNextShop);
    }

    [Fact]
    public void RelicAggregate_SignetRingField_JsonRoundtripPreservesValue()
    {
        var run = new RunData();
        run.RelicAggregates[SignetRingRelicId] = new RelicAggregate
        {
            FloorsTraveledUntilNextShop = 4,
        };

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.NotNull(restored);
        Assert.Equal(4, restored!.RelicAggregates[SignetRingRelicId].FloorsTraveledUntilNextShop);
    }

    [Fact]
    public void RunTracker_SignetRingShopHelper_RecordsFloorDistanceOnce()
    {
        var agg = new RelicAggregate();

        Assert.True(RunTracker.RecordSignetRingFloorsToNextShopForTest(agg, pickupFloor: 7, shopFloor: 11));
        Assert.Equal(4, agg.FloorsTraveledUntilNextShop);
        Assert.False(RunTracker.RecordSignetRingFloorsToNextShopForTest(agg, pickupFloor: 7, shopFloor: 15));
        Assert.Equal(4, agg.FloorsTraveledUntilNextShop);
    }

    [Fact]
    public void RunTracker_SignetRingShopHelper_AcceptsRunStartPickupAndClampsDistance()
    {
        var runStartAgg = new RelicAggregate();
        var sameFloorAgg = new RelicAggregate();

        Assert.True(RunTracker.RecordSignetRingFloorsToNextShopForTest(runStartAgg, pickupFloor: 0, shopFloor: 3));
        Assert.Equal(3, runStartAgg.FloorsTraveledUntilNextShop);

        Assert.True(RunTracker.RecordSignetRingFloorsToNextShopForTest(sameFloorAgg, pickupFloor: 5, shopFloor: 4));
        Assert.Equal(0, sameFloorAgg.FloorsTraveledUntilNextShop);
    }

    [Fact]
    public void MergeRelicAggregate_SignetRingField_PreservesFirstResolvedShop()
    {
        var target = new RelicAggregate();
        var firstShop = new RelicAggregate { FloorsTraveledUntilNextShop = 4 };
        var laterShop = new RelicAggregate { FloorsTraveledUntilNextShop = 8 };

        RunTracker.MergeRelicAggregateInto(target, firstShop);
        RunTracker.MergeRelicAggregateInto(target, laterShop);

        Assert.Equal(4, target.FloorsTraveledUntilNextShop);
    }

    [Fact]
    public void RelicTooltip_SignetRing_ShowsRequestedRow()
    {
        var body = BuildBody(new RelicAggregate { FloorsTraveledUntilNextShop = 4 });

        Assert.Contains("Floors traveled until next shop reached", body);
        Assert.Contains("[b]4[/b]", body);
    }

    [Fact]
    public void RelicTooltip_SignetRing_DispatchesForModel()
    {
        var relic = (SignetRing)RuntimeHelpers.GetUninitializedObject(typeof(SignetRing));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate { FloorsTraveledUntilNextShop = 4 },
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Signet Ring", title);
        Assert.Contains("Floors traveled until next shop reached", body);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildSignetRingBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildSignetRingBodyBBCode returned null."));
}
