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
            CardsUpgraded = 3,
            UpgradedCards = { "Grave Warden+", "Reap+", "Grave Warden+" },
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);
        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[FishingRodRelicId];
        Assert.Equal(3, agg.CardsUpgraded);
        Assert.Equal(
            new[] { "Grave Warden+", "Reap+", "Grave Warden+" },
            agg.UpgradedCards);
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
    public void RelicTooltip_FishingRod_ListsEveryUpgradedCardWithoutNarrowTableCells()
    {
        var body = BuildBody(new RelicAggregate
        {
            CardsUpgraded = 2,
            UpgradedCards = { "Grave Warden+", "Reap+" },
        });

        Assert.Contains("Cards upgraded", body);
        Assert.Contains("[b]2[/b]", body);
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
