using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class GoldenPearlStatsTests
{
    private const string GoldenPearlRelicId = "RELIC.GOLDEN_PEARL";

    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildGoldenPearlBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "BuildGoldenPearlBodyBBCode not found.");

    [Fact]
    public void RelicAggregate_GoldenPearlField_DefaultsToUnset()
    {
        var aggregate = new RelicAggregate();

        Assert.Null(aggregate.FloorsBeforeFirstGoldExpense);
    }

    [Fact]
    public void RelicAggregate_GoldenPearlField_JsonRoundtripPreservesValue()
    {
        var run = new RunData();
        run.RelicAggregates[GoldenPearlRelicId] = new RelicAggregate
        {
            FloorsBeforeFirstGoldExpense = 5,
        };

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("floors_before_first_gold_expense", json);
        Assert.NotNull(restored);
        Assert.Equal(
            5,
            restored!.RelicAggregates[GoldenPearlRelicId]
                .FloorsBeforeFirstGoldExpense);
    }

    [Fact]
    public void FirstExpenseHelper_RecordsFloorDistanceOnce()
    {
        var aggregate = new RelicAggregate();

        Assert.True(RunTracker.RecordGoldenPearlFirstGoldExpenseForTest(
            aggregate,
            pickupFloor: 0,
            expenseFloor: 5));
        Assert.Equal(5, aggregate.FloorsBeforeFirstGoldExpense);
        Assert.False(RunTracker.RecordGoldenPearlFirstGoldExpenseForTest(
            aggregate,
            pickupFloor: 0,
            expenseFloor: 8));
        Assert.Equal(5, aggregate.FloorsBeforeFirstGoldExpense);
    }

    [Fact]
    public void FirstExpenseHelper_ClampsSameOrEarlierFloorToZero()
    {
        var aggregate = new RelicAggregate();

        Assert.True(RunTracker.RecordGoldenPearlFirstGoldExpenseForTest(
            aggregate,
            pickupFloor: 5,
            expenseFloor: 4));
        Assert.Equal(0, aggregate.FloorsBeforeFirstGoldExpense);
    }

    [Fact]
    public void MergeRelicAggregate_GoldenPearlField_PreservesFirstExpense()
    {
        var target = new RelicAggregate();

        RunTracker.MergeRelicAggregateInto(
            target,
            new RelicAggregate { FloorsBeforeFirstGoldExpense = 5 });
        RunTracker.MergeRelicAggregateInto(
            target,
            new RelicAggregate { FloorsBeforeFirstGoldExpense = 8 });

        Assert.Equal(5, target.FloorsBeforeFirstGoldExpense);
    }

    [Fact]
    public void RelicTooltip_GoldenPearl_ShowsPendingAndResolvedValues()
    {
        var pending = BuildBody(new RelicAggregate());
        var resolved = BuildBody(new RelicAggregate
        {
            FloorsBeforeFirstGoldExpense = 5,
        });

        Assert.Contains("Floors before first gold expense", pending);
        Assert.Contains("[b]not yet[/b]", pending);
        Assert.Contains("[b]5[/b]", resolved);
        Assert.Contains("lost or stolen gold does not count", resolved);
    }

    [Fact]
    public void RelicTooltip_GoldenPearl_DispatchesForModel()
    {
        var relic = (GoldenPearl)
            RuntimeHelpers.GetUninitializedObject(typeof(GoldenPearl));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate { FloorsBeforeFirstGoldExpense = 5 },
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Golden Pearl", title);
        Assert.Contains("[b]5[/b]", body);
    }

    private static string BuildBody(RelicAggregate aggregate)
        => (string)(BuildBodyMethod.Invoke(null, new object?[] { aggregate })
            ?? throw new InvalidOperationException(
                "BuildGoldenPearlBodyBBCode returned null."));
}
