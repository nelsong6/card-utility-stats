using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class BookOfFiveRingsStatsTests
{
    private const string BookOfFiveRingsRelicId = "RELIC.BOOK_OF_FIVE_RINGS";

    private static readonly MethodInfo BuildBookOfFiveRingsBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildBookOfFiveRingsBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildBookOfFiveRingsBodyBBCode not found.");

    [Fact]
    public void Patch_TargetsBookOfFiveRingsAfterCardChangedPiles()
    {
        var target = typeof(BookOfFiveRings).GetMethod(
            nameof(BookOfFiveRings.AfterCardChangedPiles));

        Assert.NotNull(target);
        var parameters = target!.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.Equal(typeof(CardModel), parameters[0].ParameterType);
        Assert.Equal(typeof(PileType), parameters[1].ParameterType);
        Assert.Equal("clonedBy", parameters[2].Name);
        Assert.NotNull(typeof(CardReward).GetMethod(nameof(CardReward.OnSkipped)));
    }

    [Fact]
    public void RelicAggregate_BookOfFiveRingsFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.CardsAddedToDeck);
        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.TotalHealingAttempted);
        Assert.Equal(0m, agg.TotalHealingRestored);
        Assert.Equal(0m, agg.TotalHealingLost);
        Assert.Equal(0, agg.CardRewardsSkipped);
        Assert.Null(agg.FloorAcquired);
    }

    [Fact]
    public void RelicAggregate_BookOfFiveRingsFields_JsonRoundtripPreservesValues()
    {
        var run = new RunData();
        run.RelicAggregates[BookOfFiveRingsRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"cards_added_to_deck\"", json);
        Assert.Contains("\"card_rewards_skipped\"", json);
        Assert.Contains("\"floor_acquired\"", json);
        Assert.NotNull(restored);
        AssertPopulatedAggregate(restored!.RelicAggregates[BookOfFiveRingsRelicId]);
    }

    [Fact]
    public void RunTracker_BookOfFiveRingsHelpers_TrackCardsAndSkippedRewards()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordBookOfFiveRingsCardAddedForTest(agg, 8);
        RunTracker.RecordBookOfFiveRingsCardAddedForTest(agg, -2);
        RunTracker.RecordBookOfFiveRingsCardRewardSkippedForTest(agg, 3);
        RunTracker.RecordBookOfFiveRingsCardRewardSkippedForTest(agg, -1);

        Assert.Equal(8, agg.CardsAddedToDeck);
        Assert.Equal(3, agg.CardRewardsSkipped);
    }

    [Fact]
    public void MergeRelicAggregate_BookOfFiveRingsFields_AreAdditive()
    {
        var target = new RelicAggregate
        {
            CardsAddedToDeck = 3,
            Activations = 1,
            TotalHealingAttempted = 20m,
            TotalHealingRestored = 10m,
            TotalHealingLost = 10m,
            CardRewardsSkipped = 1,
            FloorAcquired = 8,
        };
        var source = new RelicAggregate
        {
            CardsAddedToDeck = 5,
            TotalHealingRestored = 2m,
            CardRewardsSkipped = 2,
            FloorAcquired = 9,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        AssertPopulatedAggregate(target);
    }

    [Fact]
    public void RelicTooltip_BookOfFiveRings_ShowsRequestedRowsAndHeldFloorAverage()
    {
        var body = BuildBody(PopulatedAggregate(), currentFloor: 11);

        Assert.Contains("Total cards added to deck", body);
        Assert.Contains("Avg cards added per floor", body);
        Assert.Contains("Total times triggered", body);
        Assert.Contains("Total HP healed", body);
        Assert.Contains("Total HP healing blocked", body);
        Assert.Contains("Card rewards skipped", body);
        Assert.Contains("[b]8[/b]", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]12[/b]", body);
        Assert.Contains("[b]3[/b]", body);
    }

    [Fact]
    public void RelicTooltip_BookOfFiveRings_DispatchesForModel()
    {
        var relic = (BookOfFiveRings)RuntimeHelpers.GetUninitializedObject(
            typeof(BookOfFiveRings));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            PopulatedAggregate(),
            floorCount: 11,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Book of Five Rings", title);
        Assert.Contains("Avg cards added per floor", body);
    }

    private static RelicAggregate PopulatedAggregate()
        => new()
        {
            CardsAddedToDeck = 8,
            Activations = 1,
            TotalHealingAttempted = 20m,
            TotalHealingRestored = 12m,
            TotalHealingLost = 8m,
            CardRewardsSkipped = 3,
            FloorAcquired = 8,
        };

    private static void AssertPopulatedAggregate(RelicAggregate agg)
    {
        Assert.Equal(8, agg.CardsAddedToDeck);
        Assert.Equal(1, agg.Activations);
        Assert.Equal(20m, agg.TotalHealingAttempted);
        Assert.Equal(12m, agg.TotalHealingRestored);
        Assert.Equal(8m, agg.TotalHealingLost);
        Assert.Equal(3, agg.CardRewardsSkipped);
        Assert.Equal(8, agg.FloorAcquired);
    }

    private static string BuildBody(
        RelicAggregate agg,
        int? currentFloor,
        int? floorAcquiredFallback = null)
        => (string)(BuildBookOfFiveRingsBodyMethod.Invoke(
                null,
                new object?[] { agg, currentFloor, floorAcquiredFallback })
            ?? throw new InvalidOperationException(
                "BuildBookOfFiveRingsBodyBBCode returned null."));
}
