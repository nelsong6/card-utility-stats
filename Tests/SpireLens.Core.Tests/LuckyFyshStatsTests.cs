using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class LuckyFyshStatsTests
{
    private const string LuckyFyshRelicId = "RELIC.LUCKY_FYSH";

    private static readonly MethodInfo BuildLuckyFyshBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildLuckyFyshBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildLuckyFyshBodyBBCode not found.");

    [Fact]
    public void Patch_TargetsLuckyFyshAfterCardChangedPiles()
    {
        var target = typeof(LuckyFysh).GetMethod(nameof(LuckyFysh.AfterCardChangedPiles));

        Assert.NotNull(target);
        var parameters = target!.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.Equal(typeof(CardModel), parameters[0].ParameterType);
        Assert.Equal(typeof(PileType), parameters[1].ParameterType);
        Assert.Equal("clonedBy", parameters[2].Name);
    }

    [Fact]
    public void RelicAggregate_LuckyFyshFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.GoldGained);
        Assert.Equal(0, agg.CardsAddedToDeck);
    }

    [Fact]
    public void RelicAggregate_LuckyFyshFields_JsonRoundtripPreservesValues()
    {
        var run = new RunData();
        run.RelicAggregates[LuckyFyshRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"gold_gained\"", json);
        Assert.Contains("\"cards_added_to_deck\"", json);
        Assert.NotNull(restored);
        AssertPopulatedAggregate(restored!.RelicAggregates[LuckyFyshRelicId]);
    }

    [Fact]
    public void RunTracker_LuckyFyshHelper_TracksCardsAndActualNonNegativeGoldDelta()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordLuckyFyshCardAddedForTest(agg, 100, 115);
        RunTracker.RecordLuckyFyshCardAddedForTest(agg, 115, 145);
        RunTracker.RecordLuckyFyshCardAddedForTest(agg, 145, 140);

        Assert.Equal(3, agg.CardsAddedToDeck);
        Assert.Equal(45, agg.GoldGained);
    }

    [Fact]
    public void MergeRelicAggregate_LuckyFyshFields_AreAdditive()
    {
        var target = new RelicAggregate
        {
            GoldGained = 15,
            CardsAddedToDeck = 1,
        };
        var source = new RelicAggregate
        {
            GoldGained = 30,
            CardsAddedToDeck = 2,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        AssertPopulatedAggregate(target);
    }

    [Fact]
    public void RelicTooltip_LuckyFysh_ShowsRequestedRows()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("Gold gained", body);
        Assert.Contains("Cards added to deck", body);
        Assert.Contains("[b]45[/b]", body);
        Assert.Contains("[b]3[/b]", body);
    }

    [Fact]
    public void RelicTooltip_LuckyFysh_DispatchesForModel()
    {
        var relic = (LuckyFysh)RuntimeHelpers.GetUninitializedObject(typeof(LuckyFysh));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            PopulatedAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Lucky Fysh", title);
        Assert.Contains("Gold gained", body);
    }

    private static RelicAggregate PopulatedAggregate()
        => new()
        {
            GoldGained = 45,
            CardsAddedToDeck = 3,
        };

    private static void AssertPopulatedAggregate(RelicAggregate agg)
    {
        Assert.Equal(45, agg.GoldGained);
        Assert.Equal(3, agg.CardsAddedToDeck);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildLuckyFyshBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildLuckyFyshBodyBBCode returned null."));
}
