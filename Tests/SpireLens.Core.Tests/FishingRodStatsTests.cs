using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class FishingRodStatsTests
{
    private const string FishingRodRelicId = "RELIC.FISHING_ROD";

    private static readonly MethodInfo BuildFishingRodBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildFishingRodBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildFishingRodBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void FishingRodPatch_TargetsAfterCombatEnd()
    {
        var targetMethod = typeof(FishingRodAfterCombatEndStatsPatch).GetMethod(
            "TargetMethod",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("TargetMethod not found.");
        var target = targetMethod.Invoke(null, null) as MethodBase;

        Assert.NotNull(target);
        Assert.Equal(nameof(FishingRod.AfterCombatEnd), target!.Name);
        var parameter = Assert.Single(target.GetParameters());
        Assert.Equal("room", parameter.Name);
        Assert.Equal(typeof(CombatRoom), parameter.ParameterType);
    }

    [Fact]
    public void RelicAggregate_FishingRodFields_JsonRoundtripPreservesEveryUpgrade()
    {
        var run = new RunData();
        run.RelicAggregates[FishingRodRelicId] = new RelicAggregate
        {
            FloorAcquired = 5,
            CardsUpgraded = 3,
            UpgradedCards = { "Grave Warden+", "Reap+", "Grave Warden+" },
            FishingRodCombatFloorDistances = { 2, 2, 3, 3, 4, 1 },
            FishingRodLastCombatFloor = 20,
            FishingRodUpgradeFloorDistances = { 7, 8 },
            FishingRodLastUpgradeFloor = 20,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);
        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[FishingRodRelicId];
        Assert.Equal(5, agg.FloorAcquired);
        Assert.Equal(3, agg.CardsUpgraded);
        Assert.Equal(
            new[] { "Grave Warden+", "Reap+", "Grave Warden+" },
            agg.UpgradedCards);
        Assert.Equal(new[] { 2, 2, 3, 3, 4, 1 }, agg.FishingRodCombatFloorDistances);
        Assert.Equal(20, agg.FishingRodLastCombatFloor);
        Assert.Equal(new[] { 7, 8 }, agg.FishingRodUpgradeFloorDistances);
        Assert.Equal(20, agg.FishingRodLastUpgradeFloor);
    }

    [Fact]
    public void RunTracker_FishingRodTestHelper_RecordsCardsInOrderIncludingDuplicates()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordFishingRodUpgradesForTest(
            agg,
            new[] { "Grave Warden+", "", "Reap+", "Grave Warden+" });

        Assert.Equal(3, agg.CardsUpgraded);
        Assert.Equal(
            new[] { "Grave Warden+", "Reap+", "Grave Warden+" },
            agg.UpgradedCards);
    }

    [Fact]
    public void RunTracker_FishingRodFloorTiming_IncludesAcquisitionThenConsecutiveEvents()
    {
        var agg = new RelicAggregate { FloorAcquired = 5 };

        RunTracker.RecordFishingRodCombatFloorForTest(
            agg, floor: 7, includeAcquisitionInterval: true);
        RunTracker.RecordFishingRodCombatFloorForTest(
            agg, floor: 9, includeAcquisitionInterval: false);
        RunTracker.RecordFishingRodCombatFloorForTest(
            agg, floor: 12, includeAcquisitionInterval: false);
        RunTracker.RecordFishingRodUpgradeFloorForTest(
            agg, floor: 12, includeAcquisitionInterval: true);
        RunTracker.RecordFishingRodUpgradesForTest(agg, ["Grave Warden+"]);
        RunTracker.RecordFishingRodCombatFloorForTest(
            agg, floor: 15, includeAcquisitionInterval: false);
        RunTracker.RecordFishingRodCombatFloorForTest(
            agg, floor: 19, includeAcquisitionInterval: false);
        RunTracker.RecordFishingRodCombatFloorForTest(
            agg, floor: 20, includeAcquisitionInterval: false);
        RunTracker.RecordFishingRodUpgradeFloorForTest(
            agg, floor: 20, includeAcquisitionInterval: false);
        RunTracker.RecordFishingRodUpgradesForTest(agg, ["Reap+"]);

        Assert.Equal(new[] { 2, 2, 3, 3, 4, 1 }, agg.FishingRodCombatFloorDistances);
        Assert.Equal(20, agg.FishingRodLastCombatFloor);
        Assert.Equal(new[] { 7, 8 }, agg.FishingRodUpgradeFloorDistances);
        Assert.Equal(20, agg.FishingRodLastUpgradeFloor);
        Assert.Equal(
            2.5m,
            RelicHoverShowPatch.CalculateAverageFishingRodFloorDistance(
                agg.FishingRodCombatFloorDistances));
        Assert.Equal(
            7.5m,
            RelicHoverShowPatch.CalculateAverageFishingRodFloorDistance(
                agg.FishingRodUpgradeFloorDistances));
    }

    [Fact]
    public void RunTracker_FishingRodFloorTiming_MidRunTrackingStartsWithABaseline()
    {
        var agg = new RelicAggregate
        {
            FloorAcquired = 5,
            CardsUpgraded = 1,
        };

        RunTracker.RecordFishingRodCombatFloorForTest(
            agg, floor: 10, includeAcquisitionInterval: false);
        RunTracker.RecordFishingRodCombatFloorForTest(
            agg, floor: 12, includeAcquisitionInterval: false);
        RunTracker.RecordFishingRodUpgradeFloorForTest(
            agg, floor: 12, includeAcquisitionInterval: false);
        RunTracker.RecordFishingRodUpgradeFloorForTest(
            agg, floor: 20, includeAcquisitionInterval: false);

        Assert.Equal(new[] { 2 }, agg.FishingRodCombatFloorDistances);
        Assert.Equal(new[] { 8 }, agg.FishingRodUpgradeFloorDistances);
    }

    [Fact]
    public void RelicTooltip_FishingRod_ListsEveryUpgradedCardWithoutNarrowTableCells()
    {
        var body = BuildBody(new RelicAggregate
        {
            CardsUpgraded = 2,
            UpgradedCards = { "Grave Warden+", "Reap+" },
            FishingRodCombatFloorDistances = { 2, 2, 3, 3 },
            FishingRodUpgradeFloorDistances = { 7, 8 },
        });

        Assert.Contains("Cards upgraded", body);
        Assert.Contains("first normal Monster combat", body);
        Assert.Contains("first successful card upgrade", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]2.5[/b]", body);
        Assert.Contains("[b]7.5[/b]", body);
        Assert.Contains("Grave Warden+", body);
        Assert.Contains("Reap+", body);
        Assert.Contains("[hint=\"Upgraded:", body);
    }

    [Fact]
    public void RelicTooltip_FishingRod_DispatchesForFishingRodModel()
    {
        var relic = (FishingRod)RuntimeHelpers.GetUninitializedObject(typeof(FishingRod));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate
            {
                CardsUpgraded = 1,
                UpgradedCards = { "Reap+" },
            },
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Fishing Rod", title);
        Assert.Contains("Reap+", body);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildFishingRodBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildFishingRodBodyBBCode returned null."));
}
