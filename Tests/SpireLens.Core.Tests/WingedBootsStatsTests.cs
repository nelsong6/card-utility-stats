using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class WingedBootsStatsTests
{
    private const string WingedBootsRelicId = "RELIC.WINGED_BOOTS";

    private static readonly MethodInfo BuildWingedBootsBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildWingedBootsBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildWingedBootsBodyBBCode not found.");

    [Fact]
    public void RecordDestination_UsesAuthoritativeUseNumberAndOriginalPointCategory()
    {
        var agg = new RelicAggregate();

        Assert.True(RunTracker.RecordWingedBootsDestinationForTest(
            agg,
            useNumber: 2,
            MapPointType.Shop));
        Assert.True(RunTracker.RecordWingedBootsDestinationForTest(
            agg,
            useNumber: 3,
            MapPointType.Unknown));
        Assert.False(RunTracker.RecordWingedBootsDestinationForTest(
            agg,
            useNumber: 2,
            MapPointType.Elite));

        Assert.Collection(
            agg.WingedBootsDestinations,
            entry =>
            {
                Assert.Equal(2, entry.UseNumber);
                Assert.Equal("shop", entry.Destination);
            },
            entry =>
            {
                Assert.Equal(3, entry.UseNumber);
                Assert.Equal("question_mark", entry.Destination);
            });
    }

    [Fact]
    public void RelicAggregate_WingedBootsDestinations_JsonRoundtripPreservesOrder()
    {
        var run = new RunData();
        var agg = new RelicAggregate();
        RunTracker.RecordWingedBootsDestinationForTest(agg, 1, MapPointType.Monster);
        RunTracker.RecordWingedBootsDestinationForTest(agg, 2, MapPointType.Elite);
        run.RelicAggregates[WingedBootsRelicId] = agg;

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"winged_boots_destinations\"", json);
        Assert.NotNull(restored);
        Assert.Equal("combat", restored!.RelicAggregates[WingedBootsRelicId]
            .WingedBootsDestinations[0].Destination);
        Assert.Equal("elite", restored.RelicAggregates[WingedBootsRelicId]
            .WingedBootsDestinations[1].Destination);
    }

    [Fact]
    public void Tooltip_ShowsThreeNumberedDestinationsAndMarksUnobservedPriorUse()
    {
        var agg = new RelicAggregate
        {
            WingedBootsDestinations =
            [
                new WingedBootsDestinationAggregate
                {
                    UseNumber = 2,
                    Destination = "shop",
                },
            ],
        };

        var body = BuildBody(agg, liveTimesUsed: 2);

        Assert.Contains("1st floor destination", body);
        Assert.Contains("[b]not tracked[/b]", body);
        Assert.Contains("2nd floor destination", body);
        Assert.Contains(
            "res://images/atlases/ui_atlas.sprites/map/icons/map_shop.tres",
            body);
        Assert.DoesNotContain("[b]shop[/b]", body);
        Assert.Contains("3rd floor destination", body);
        Assert.Contains("[b]not used yet[/b]", body);
    }

    [Fact]
    public void Tooltip_DispatchesForWingedBoots()
    {
        var relic = (WingedBoots)RuntimeHelpers.GetUninitializedObject(typeof(WingedBoots));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Winged Boots", title);
        Assert.Contains("1st floor destination", body);
    }

    private static string BuildBody(RelicAggregate agg, int liveTimesUsed)
        => (string)(BuildWingedBootsBodyMethod.Invoke(null, new object?[] { agg, liveTimesUsed })
            ?? throw new InvalidOperationException("BuildWingedBootsBodyBBCode returned null."));
}
