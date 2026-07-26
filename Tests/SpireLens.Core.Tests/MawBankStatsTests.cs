using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class MawBankStatsTests
{
    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildMawBankBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildMawBankBodyBBCode not found.");

    [Fact]
    public void Patch_TargetsMawBankOwnerRoomEntry()
    {
        var target = typeof(MawBank).GetMethod(
            nameof(MawBank.AfterRoomEntered),
            [typeof(AbstractRoom)]);

        Assert.NotNull(target);
        Assert.Equal(typeof(System.Threading.Tasks.Task), target!.ReturnType);
    }

    [Fact]
    public void RoomVisitState_CountsOnlyDistinctUnspentShopExits()
    {
        var aggregate = new RelicAggregate();
        int? pendingShopFloor = null;

        Assert.True(RunTracker.RecordMawBankRoomEntryForTest(
            aggregate,
            ref pendingShopFloor,
            currentFloor: 5,
            enteredShop: true,
            hasItemBeenBought: false));
        Assert.Equal(5, pendingShopFloor);
        Assert.Equal(0, aggregate.MawBankShopsSkipped);

        Assert.False(RunTracker.RecordMawBankRoomEntryForTest(
            aggregate,
            ref pendingShopFloor,
            currentFloor: 5,
            enteredShop: true,
            hasItemBeenBought: false));
        Assert.Equal(0, aggregate.MawBankShopsSkipped);

        Assert.True(RunTracker.RecordMawBankRoomEntryForTest(
            aggregate,
            ref pendingShopFloor,
            currentFloor: 6,
            enteredShop: false,
            hasItemBeenBought: false));
        Assert.Null(pendingShopFloor);
        Assert.Equal(1, aggregate.MawBankShopsSkipped);

        Assert.True(RunTracker.RecordMawBankRoomEntryForTest(
            aggregate,
            ref pendingShopFloor,
            currentFloor: 8,
            enteredShop: true,
            hasItemBeenBought: false));
        Assert.True(RunTracker.RecordMawBankRoomEntryForTest(
            aggregate,
            ref pendingShopFloor,
            currentFloor: 9,
            enteredShop: false,
            hasItemBeenBought: true));
        Assert.Null(pendingShopFloor);
        Assert.Equal(1, aggregate.MawBankShopsSkipped);
    }

    [Fact]
    public void ActivationState_UsesCompletedNonNegativeGoldDelta()
    {
        var aggregate = new RelicAggregate();

        RunTracker.RecordMawBankActivationForTest(aggregate, 100, 112);
        RunTracker.RecordMawBankActivationForTest(aggregate, 112, 130);
        RunTracker.RecordMawBankActivationForTest(aggregate, 130, 125);

        Assert.Equal(3, aggregate.Activations);
        Assert.Equal(30, aggregate.GoldGained);
    }

    [Fact]
    public void MergeRelicAggregate_MawBankFields_AreAdditive()
    {
        var target = new RelicAggregate
        {
            Activations = 2,
            GoldGained = 24,
            MawBankShopsSkipped = 1,
        };
        var source = new RelicAggregate
        {
            Activations = 4,
            GoldGained = 48,
            MawBankShopsSkipped = 2,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(6, target.Activations);
        Assert.Equal(72, target.GoldGained);
        Assert.Equal(3, target.MawBankShopsSkipped);
    }

    [Fact]
    public void RelicTooltip_MawBank_ShowsRequestedRows()
    {
        var body = BuildBody(new RelicAggregate
        {
            Activations = 6,
            GoldGained = 72,
            MawBankShopsSkipped = 2,
        });

        Assert.Contains("completed room entries where Maw Bank was still active", body);
        Assert.Contains("Gold gained", body);
        Assert.Contains("Shops skipped", body);
        Assert.Contains("[b]6[/b]", body);
        Assert.Contains("[b]72[/b]", body);
        Assert.Contains("[b]2[/b]", body);
    }

    [Fact]
    public void RelicTooltip_MawBank_DispatchesForModel()
    {
        var relic = (MawBank)RuntimeHelpers.GetUninitializedObject(typeof(MawBank));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate(),
            floorCount: null,
            out var title,
            out _);

        Assert.True(recognized);
        Assert.Equal("Maw Bank", title);
    }

    private static string BuildBody(RelicAggregate aggregate)
        => (string)(BuildBodyMethod.Invoke(null, new object?[] { aggregate })
            ?? throw new InvalidOperationException("BuildMawBankBodyBBCode returned null."));
}
