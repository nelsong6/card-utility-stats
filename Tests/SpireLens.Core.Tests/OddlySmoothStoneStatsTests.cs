using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class OddlySmoothStoneStatsTests
{
    private const string RelicId = "RELIC.ODDLY_SMOOTH_STONE";

    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildOddlySmoothStoneBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "BuildOddlySmoothStoneBodyBBCode not found.");

    [Fact]
    public void BlockCardClassification_UsesGameGainsBlockProperty()
    {
        Assert.True(RunTracker.IsOddlySmoothStoneBlockCard(
            Uninitialized<DefendIronclad>()));
        Assert.False(RunTracker.IsOddlySmoothStoneBlockCard(
            Uninitialized<Shadowmeld>()));
        Assert.False(RunTracker.IsOddlySmoothStoneBlockCard(null));
    }

    [Fact]
    public void TrackingHelper_AccumulatesAndIgnoresNegativeCounts()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordOddlySmoothStoneBlockCardPlayedForTest(agg);
        RunTracker.RecordOddlySmoothStoneBlockCardPlayedForTest(agg, 3);
        RunTracker.RecordOddlySmoothStoneBlockCardPlayedForTest(agg, -2);

        Assert.Equal(4, agg.OddlySmoothStoneBlockCardsPlayed);
    }

    [Fact]
    public void RelicAggregate_JsonRoundtripPreservesBlockCardsPlayed()
    {
        var run = new RunData();
        run.RelicAggregates[RelicId] = new RelicAggregate
        {
            OddlySmoothStoneBlockCardsPlayed = 7,
        };

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(
            json,
            RunStorage.Options);

        Assert.Contains("\"oddly_smooth_stone_block_cards_played\":7", json);
        Assert.NotNull(restored);
        Assert.Equal(
            7,
            restored!.RelicAggregates[RelicId]
                .OddlySmoothStoneBlockCardsPlayed);
    }

    [Fact]
    public void MergeRelicAggregateInto_AccumulatesBlockCardsPlayed()
    {
        var target = new RelicAggregate
        {
            OddlySmoothStoneBlockCardsPlayed = 4,
        };
        var source = new RelicAggregate
        {
            OddlySmoothStoneBlockCardsPlayed = 3,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(7, target.OddlySmoothStoneBlockCardsPlayed);
    }

    [Fact]
    public void Tooltip_ShowsBlockCardsPlayedWithBlockIcon()
    {
        var body = BuildBody(new RelicAggregate
        {
            OddlySmoothStoneBlockCardsPlayed = 7,
        });

        Assert.Contains("Block cards played", body);
        Assert.Contains("block.png", body);
        Assert.Contains("[b]7[/b]", body);
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void TooltipDispatch_RecognizesOddlySmoothStone()
    {
        var relic = Uninitialized<OddlySmoothStone>();

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Oddly Smooth Stone", title);
        Assert.Contains("Block cards played", body);
    }

    [Fact]
    public void OlderShape_DefaultsBlockCardsPlayedToZero()
    {
        var agg = JsonSerializer.Deserialize<RelicAggregate>(
            "{}",
            RunStorage.Options);

        Assert.NotNull(agg);
        Assert.Equal(0, agg!.OddlySmoothStoneBlockCardsPlayed);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildBodyMethod.Invoke(null, [agg])
            ?? throw new InvalidOperationException(
                "BuildOddlySmoothStoneBodyBBCode returned null."));

    private static T Uninitialized<T>() where T : class
        => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
}
