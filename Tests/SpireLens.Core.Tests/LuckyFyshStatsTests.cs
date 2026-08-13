using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
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
        Assert.Equal(0, agg.CursesAddedToDeck);
        Assert.Equal(0, agg.LuckyFyshCardsAddedInCombats);
        Assert.Equal(0, agg.LuckyFyshCardsAddedInShops);
        Assert.Equal(0, agg.LuckyFyshCardsAddedInEvents);
        Assert.Equal(0, agg.LuckyFyshCardsAddedInCampfires);
        Assert.Equal(0, agg.LuckyFyshGoldGainedInCombats);
        Assert.Equal(0, agg.LuckyFyshGoldGainedInShops);
        Assert.Equal(0, agg.LuckyFyshGoldGainedInEvents);
        Assert.Equal(0, agg.LuckyFyshGoldGainedInCampfires);
        Assert.Equal(0, agg.LuckyFyshCombatsHeld);
        Assert.Equal(0, agg.LuckyFyshShopsHeld);
        Assert.Equal(0, agg.LuckyFyshEventsHeld);
        Assert.Equal(0, agg.LuckyFyshCampfiresHeld);
        Assert.Null(agg.LuckyFyshLastCombatFloorHeld);
        Assert.Null(agg.LuckyFyshLastShopFloorHeld);
        Assert.Null(agg.LuckyFyshLastEventFloorHeld);
        Assert.Null(agg.LuckyFyshLastCampfireFloorHeld);
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
        Assert.Contains("\"curses_added_to_deck\"", json);
        Assert.Contains("\"lucky_fysh_cards_added_in_combats\"", json);
        Assert.Contains("\"lucky_fysh_cards_added_in_shops\"", json);
        Assert.Contains("\"lucky_fysh_cards_added_in_events\"", json);
        Assert.Contains("\"lucky_fysh_cards_added_in_campfires\"", json);
        Assert.Contains("\"lucky_fysh_gold_gained_in_combats\"", json);
        Assert.Contains("\"lucky_fysh_gold_gained_in_shops\"", json);
        Assert.Contains("\"lucky_fysh_gold_gained_in_events\"", json);
        Assert.Contains("\"lucky_fysh_gold_gained_in_campfires\"", json);
        Assert.Contains("\"lucky_fysh_combats_held\"", json);
        Assert.Contains("\"lucky_fysh_shops_held\"", json);
        Assert.Contains("\"lucky_fysh_events_held\"", json);
        Assert.Contains("\"lucky_fysh_campfires_held\"", json);
        Assert.Contains("\"lucky_fysh_last_combat_floor_held\"", json);
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
    public void RunTracker_LuckyFyshHelper_CountsOnlyCurseAdditionsAsCurses()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordLuckyFyshCardAddedForTest(agg, 0, 15, isCurse: true);
        RunTracker.RecordLuckyFyshCardAddedForTest(agg, 15, 30, isCurse: false);
        RunTracker.RecordLuckyFyshCardAddedForTest(agg, 30, 45, isCurse: true);

        Assert.Equal(3, agg.CardsAddedToDeck);
        Assert.Equal(2, agg.CursesAddedToDeck);
    }

    [Theory]
    [InlineData(RoomType.Monster)]
    [InlineData(RoomType.Elite)]
    [InlineData(RoomType.Boss)]
    public void RunTracker_LuckyFyshHelper_AttributesEveryCombatRoomTypeToCombats(
        RoomType roomType)
    {
        var agg = new RelicAggregate();

        RunTracker.RecordLuckyFyshCardAddedForTest(agg, 0, 15, false, roomType, 7);

        Assert.Equal(1, agg.LuckyFyshCardsAddedInCombats);
        Assert.Equal(1, agg.LuckyFyshCombatsHeld);
    }

    [Fact]
    public void RunTracker_LuckyFyshHelper_SplitsAdditionsAndGoldByObservedRoom()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordLuckyFyshCardAddedForTest(agg, 0, 15, false, RoomType.Monster, 5);
        RunTracker.RecordLuckyFyshCardAddedForTest(agg, 15, 35, false, RoomType.Shop, 6);
        RunTracker.RecordLuckyFyshCardAddedForTest(agg, 35, 60, true, RoomType.Event, 7);
        RunTracker.RecordLuckyFyshCardAddedForTest(agg, 60, 90, false, RoomType.RestSite, 8);
        RunTracker.RecordLuckyFyshCardAddedForTest(agg, 90, 125, false, RoomType.Treasure, 9);

        Assert.Equal(5, agg.CardsAddedToDeck);
        Assert.Equal(1, agg.LuckyFyshCardsAddedInCombats);
        Assert.Equal(1, agg.LuckyFyshCardsAddedInShops);
        Assert.Equal(1, agg.LuckyFyshCardsAddedInEvents);
        Assert.Equal(1, agg.LuckyFyshCardsAddedInCampfires);
        Assert.Equal(15, agg.LuckyFyshGoldGainedInCombats);
        Assert.Equal(20, agg.LuckyFyshGoldGainedInShops);
        Assert.Equal(25, agg.LuckyFyshGoldGainedInEvents);
        Assert.Equal(30, agg.LuckyFyshGoldGainedInCampfires);

        // The Treasure-room addition has no bucket, so its 35 gold reaches the
        // lifetime total without landing in any split.
        Assert.Equal(125, agg.GoldGained);
    }

    [Fact]
    public void RunTracker_LuckyFyshHelper_CountsTheRoomAnAdditionHappenedIn()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordLuckyFyshCardAddedForTest(agg, 0, 15, false, RoomType.Shop, 6);
        RunTracker.RecordLuckyFyshCardAddedForTest(agg, 15, 30, false, RoomType.Shop, 6);

        Assert.Equal(2, agg.LuckyFyshCardsAddedInShops);
        Assert.Equal(1, agg.LuckyFyshShopsHeld);
        Assert.Equal(6, agg.LuckyFyshLastShopFloorHeld);
    }

    [Fact]
    public void RunTracker_LuckyFyshRoomHeld_CountsOncePerFloorPerRoomType()
    {
        var agg = new RelicAggregate();

        Assert.True(RunTracker.RecordLuckyFyshRoomHeldForTest(agg, RoomType.Monster, 3));
        Assert.False(RunTracker.RecordLuckyFyshRoomHeldForTest(agg, RoomType.Monster, 3));
        Assert.True(RunTracker.RecordLuckyFyshRoomHeldForTest(agg, RoomType.Monster, 4));
        Assert.True(RunTracker.RecordLuckyFyshRoomHeldForTest(agg, RoomType.RestSite, 4));
        Assert.False(RunTracker.RecordLuckyFyshRoomHeldForTest(agg, RoomType.Treasure, 5));

        Assert.Equal(2, agg.LuckyFyshCombatsHeld);
        Assert.Equal(1, agg.LuckyFyshCampfiresHeld);
        Assert.Equal(4, agg.LuckyFyshLastCombatFloorHeld);
        Assert.Equal(4, agg.LuckyFyshLastCampfireFloorHeld);
    }

    [Fact]
    public void MergeRelicAggregate_LuckyFyshFields_AreAdditive()
    {
        var target = new RelicAggregate
        {
            GoldGained = 15,
            CardsAddedToDeck = 1,
            CursesAddedToDeck = 1,
            LuckyFyshCardsAddedInCombats = 1,
            LuckyFyshCardsAddedInShops = 1,
            LuckyFyshCardsAddedInEvents = 0,
            LuckyFyshCardsAddedInCampfires = 1,
            LuckyFyshGoldGainedInCombats = 10,
            LuckyFyshGoldGainedInShops = 12,
            LuckyFyshGoldGainedInEvents = 0,
            LuckyFyshGoldGainedInCampfires = 9,
            LuckyFyshCombatsHeld = 2,
            LuckyFyshShopsHeld = 1,
            LuckyFyshEventsHeld = 1,
            LuckyFyshCampfiresHeld = 1,
            LuckyFyshLastCombatFloorHeld = 9,
            LuckyFyshLastShopFloorHeld = 8,
            LuckyFyshLastEventFloorHeld = 7,
            LuckyFyshLastCampfireFloorHeld = 6,
        };
        var source = new RelicAggregate
        {
            GoldGained = 30,
            CardsAddedToDeck = 2,
            CursesAddedToDeck = 1,
            LuckyFyshCardsAddedInCombats = 1,
            LuckyFyshCardsAddedInShops = 0,
            LuckyFyshCardsAddedInEvents = 1,
            LuckyFyshCardsAddedInCampfires = 0,
            LuckyFyshGoldGainedInCombats = 14,
            LuckyFyshGoldGainedInShops = 0,
            LuckyFyshGoldGainedInEvents = 16,
            LuckyFyshGoldGainedInCampfires = 0,
            LuckyFyshCombatsHeld = 2,
            LuckyFyshShopsHeld = 1,
            LuckyFyshEventsHeld = 1,
            LuckyFyshCampfiresHeld = 1,
            LuckyFyshLastCombatFloorHeld = 11,
            LuckyFyshLastShopFloorHeld = 10,
            LuckyFyshLastEventFloorHeld = 9,
            LuckyFyshLastCampfireFloorHeld = 8,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        AssertPopulatedAggregate(target);
    }

    [Fact]
    public void MergeRelicAggregate_LuckyFyshFloorMarkers_KeepTheLaterFloor()
    {
        var target = new RelicAggregate { LuckyFyshLastCombatFloorHeld = 12 };
        var source = new RelicAggregate { LuckyFyshLastCombatFloorHeld = 4 };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(12, target.LuckyFyshLastCombatFloorHeld);
    }

    [Fact]
    public void RelicTooltip_LuckyFysh_ShowsRequestedRows()
    {
        var body = BuildBody(PopulatedAggregate(), currentFloor: 12);

        Assert.Contains("Gold gained", body);
        Assert.Contains("Cards added to deck", body);
        Assert.Contains("Curses added to deck", body);
        Assert.Contains("Average cards added per floor", body);
        Assert.Contains("Average cards added per combat", body);
        Assert.Contains("Average cards added per merchant", body);
        Assert.Contains("Average cards added per event", body);
        Assert.Contains("Average cards added per campfire", body);
        Assert.Contains("[b]45[/b]", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[b]2[/b]", body);
    }

    [Fact]
    public void RelicTooltip_LuckyFysh_DividesEachRoomAverageByItsHeldRooms()
    {
        var body = BuildBody(RateAggregate(), currentFloor: 14);

        // 6 additions over floors 3..14 inclusive, 3 over 6 combats, 2 over
        // 2 merchants, 1 over 4 events.
        Assert.Contains("[b]0.5[/b]", body);
        Assert.Contains("[b]1[/b]", body);
        Assert.Contains("[b]0.25[/b]", body);
    }

    [Fact]
    public void RelicTooltip_LuckyFysh_SplitsTheGoldHalfOfEachRateBesideTheCards()
    {
        var body = BuildBody(RateAggregate(), currentFloor: 14);

        // Same denominators as the card halves: 90 gold over 12 floors, 45
        // over 6 combats, 30 over 2 merchants, 15 over 4 events.
        Assert.Contains(" 7.5[/color]", body);
        Assert.Contains(" 15[/color]", body);
        Assert.Contains(" 3.75[/color]", body);
    }

    [Fact]
    public void RelicTooltip_LuckyFysh_ReportsRoomTypesNotYetEnteredAsUnmeasured()
    {
        var agg = new RelicAggregate { CardsAddedToDeck = 2 };

        var body = BuildBody(agg, currentFloor: 4);

        Assert.Contains("not yet", body);
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
        Assert.Contains("Curses added to deck", body);
    }

    /// <summary>
    /// Divides evenly against a floor 3 pickup viewed on floor 14, so every
    /// rate in the tooltip is an exact decimal on both halves of the split.
    /// </summary>
    private static RelicAggregate RateAggregate()
        => new()
        {
            FloorAcquired = 3,
            GoldGained = 90,
            CardsAddedToDeck = 6,
            LuckyFyshCardsAddedInCombats = 3,
            LuckyFyshCardsAddedInShops = 2,
            LuckyFyshCardsAddedInEvents = 1,
            LuckyFyshGoldGainedInCombats = 45,
            LuckyFyshGoldGainedInShops = 30,
            LuckyFyshGoldGainedInEvents = 15,
            LuckyFyshCombatsHeld = 6,
            LuckyFyshShopsHeld = 2,
            LuckyFyshEventsHeld = 4,
        };

    private static RelicAggregate PopulatedAggregate()
        => new()
        {
            GoldGained = 45,
            CardsAddedToDeck = 3,
            CursesAddedToDeck = 2,
            LuckyFyshCardsAddedInCombats = 2,
            LuckyFyshCardsAddedInShops = 1,
            LuckyFyshCardsAddedInEvents = 1,
            LuckyFyshCardsAddedInCampfires = 1,
            LuckyFyshGoldGainedInCombats = 24,
            LuckyFyshGoldGainedInShops = 12,
            LuckyFyshGoldGainedInEvents = 16,
            LuckyFyshGoldGainedInCampfires = 9,
            LuckyFyshCombatsHeld = 4,
            LuckyFyshShopsHeld = 2,
            LuckyFyshEventsHeld = 2,
            LuckyFyshCampfiresHeld = 2,
            LuckyFyshLastCombatFloorHeld = 11,
            LuckyFyshLastShopFloorHeld = 10,
            LuckyFyshLastEventFloorHeld = 9,
            LuckyFyshLastCampfireFloorHeld = 8,
        };

    private static void AssertPopulatedAggregate(RelicAggregate agg)
    {
        Assert.Equal(45, agg.GoldGained);
        Assert.Equal(3, agg.CardsAddedToDeck);
        Assert.Equal(2, agg.CursesAddedToDeck);
        Assert.Equal(2, agg.LuckyFyshCardsAddedInCombats);
        Assert.Equal(1, agg.LuckyFyshCardsAddedInShops);
        Assert.Equal(1, agg.LuckyFyshCardsAddedInEvents);
        Assert.Equal(1, agg.LuckyFyshCardsAddedInCampfires);
        Assert.Equal(24, agg.LuckyFyshGoldGainedInCombats);
        Assert.Equal(12, agg.LuckyFyshGoldGainedInShops);
        Assert.Equal(16, agg.LuckyFyshGoldGainedInEvents);
        Assert.Equal(9, agg.LuckyFyshGoldGainedInCampfires);
        Assert.Equal(4, agg.LuckyFyshCombatsHeld);
        Assert.Equal(2, agg.LuckyFyshShopsHeld);
        Assert.Equal(2, agg.LuckyFyshEventsHeld);
        Assert.Equal(2, agg.LuckyFyshCampfiresHeld);
        Assert.Equal(11, agg.LuckyFyshLastCombatFloorHeld);
        Assert.Equal(10, agg.LuckyFyshLastShopFloorHeld);
        Assert.Equal(9, agg.LuckyFyshLastEventFloorHeld);
        Assert.Equal(8, agg.LuckyFyshLastCampfireFloorHeld);
    }

    private static string BuildBody(RelicAggregate agg, int? currentFloor = null)
        => (string)(BuildLuckyFyshBodyMethod.Invoke(
                null,
                new object?[] { agg, currentFloor, null })
            ?? throw new InvalidOperationException("BuildLuckyFyshBodyBBCode returned null."));
}
