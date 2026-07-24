using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Entities.Cards;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class StoneCrackerStatsTests
{
    private const string StoneCrackerRelicId = "RELIC.STONE_CRACKER";

    private static readonly MethodInfo BuildStoneCrackerBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildStoneCrackerBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildStoneCrackerBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_StoneCrackerFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0, agg.CardsUpgraded);
        Assert.Equal(0, agg.StoneCrackerUpgradedCommons);
        Assert.Equal(0, agg.StoneCrackerUpgradedUncommons);
        Assert.Equal(0, agg.StoneCrackerUpgradedRares);
        Assert.Equal(0, agg.StoneCrackerUpgradedCardPlays);
        Assert.Equal(0, agg.StoneCrackerCombats);
        Assert.Equal(0, agg.StoneCrackerTurns);
    }

    [Fact]
    public void RelicAggregate_StoneCrackerFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[StoneCrackerRelicId] = new RelicAggregate
        {
            Activations = 3,
            CardsUpgraded = 6,
            StoneCrackerUpgradedCommons = 3,
            StoneCrackerUpgradedUncommons = 2,
            StoneCrackerUpgradedRares = 1,
            StoneCrackerUpgradedCardPlays = 9,
            StoneCrackerCombats = 3,
            StoneCrackerTurns = 6,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("cards_upgraded", json);
        Assert.Contains("stone_cracker_upgraded_commons", json);
        Assert.Contains("stone_cracker_upgraded_uncommons", json);
        Assert.Contains("stone_cracker_upgraded_rares", json);
        Assert.Contains("stone_cracker_upgraded_card_plays", json);
        Assert.Contains("stone_cracker_combats", json);
        Assert.Contains("stone_cracker_turns", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[StoneCrackerRelicId];
        Assert.Equal(3, agg.Activations);
        Assert.Equal(6, agg.CardsUpgraded);
        Assert.Equal(3, agg.StoneCrackerUpgradedCommons);
        Assert.Equal(2, agg.StoneCrackerUpgradedUncommons);
        Assert.Equal(1, agg.StoneCrackerUpgradedRares);
        Assert.Equal(9, agg.StoneCrackerUpgradedCardPlays);
        Assert.Equal(3, agg.StoneCrackerCombats);
        Assert.Equal(6, agg.StoneCrackerTurns);
    }

    [Fact]
    public void RunTracker_StoneCrackerHelpers_RecordRaritiesPlaysAndDenominators()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordStoneCrackerActivationForTest(
            agg,
            new[]
            {
                CardRarity.Common,
                CardRarity.Common,
                CardRarity.Uncommon,
                CardRarity.Rare,
            });
        RunTracker.RecordStoneCrackerCombatForTest(agg, 3);
        RunTracker.RecordStoneCrackerCombatForTest(agg, -1);
        RunTracker.RecordStoneCrackerTurnForTest(agg, 6);
        RunTracker.RecordStoneCrackerTurnForTest(agg, -1);
        RunTracker.RecordStoneCrackerUpgradedCardPlayForTest(agg, 9);
        RunTracker.RecordStoneCrackerUpgradedCardPlayForTest(agg, -1);

        Assert.Equal(1, agg.Activations);
        Assert.Equal(4, agg.CardsUpgraded);
        Assert.Equal(2, agg.StoneCrackerUpgradedCommons);
        Assert.Equal(1, agg.StoneCrackerUpgradedUncommons);
        Assert.Equal(1, agg.StoneCrackerUpgradedRares);
        Assert.Equal(9, agg.StoneCrackerUpgradedCardPlays);
        Assert.Equal(3, agg.StoneCrackerCombats);
        Assert.Equal(6, agg.StoneCrackerTurns);
    }

    [Fact]
    public void MergeRelicAggregateInto_StoneCrackerFields_Accumulate()
    {
        var target = new RelicAggregate
        {
            StoneCrackerUpgradedCommons = 1,
            StoneCrackerUpgradedUncommons = 2,
            StoneCrackerUpgradedCardPlays = 3,
            StoneCrackerCombats = 1,
            StoneCrackerTurns = 2,
        };
        var source = new RelicAggregate
        {
            StoneCrackerUpgradedCommons = 2,
            StoneCrackerUpgradedRares = 1,
            StoneCrackerUpgradedCardPlays = 6,
            StoneCrackerCombats = 2,
            StoneCrackerTurns = 4,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(3, target.StoneCrackerUpgradedCommons);
        Assert.Equal(2, target.StoneCrackerUpgradedUncommons);
        Assert.Equal(1, target.StoneCrackerUpgradedRares);
        Assert.Equal(9, target.StoneCrackerUpgradedCardPlays);
        Assert.Equal(3, target.StoneCrackerCombats);
        Assert.Equal(6, target.StoneCrackerTurns);
    }

    [Fact]
    public void RelicTooltip_StoneCracker_ShowsUpgradeRaritiesAndPlayedCardAverages()
    {
        var agg = new RelicAggregate
        {
            Activations = 3,
            CardsUpgraded = 6,
            StoneCrackerUpgradedCommons = 3,
            StoneCrackerUpgradedUncommons = 2,
            StoneCrackerUpgradedRares = 1,
            StoneCrackerUpgradedCardPlays = 9,
            StoneCrackerCombats = 3,
            StoneCrackerTurns = 6,
        };

        var body = (string)(BuildStoneCrackerBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildStoneCrackerBodyBBCode returned null."));

        Assert.Contains("Activations", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("Cards upgraded", body);
        Assert.Contains("[b]6[/b]", body);
        Assert.Contains("Upgraded commons", body);
        Assert.Contains("Upgraded uncommons", body);
        Assert.Contains("Upgraded rares", body);
        Assert.Contains("Cards played upgraded by Stone Cracker", body);
        Assert.Contains("Avg cards played upgraded by Stone Cracker per turn", body);
        Assert.Contains("Avg cards played upgraded by Stone Cracker per combat", body);
        Assert.Contains("[b]9[/b]", body);
        Assert.Contains("[b]1.5[/b]", body);
    }

    [Fact]
    public void RunData_OlderShapeWithoutStoneCrackerFields_DeserializesWithZeroDefaults()
    {
        const string json = """
            {
              "run_id": "test",
              "started_at": "2026-01-01T00:00:00Z",
              "updated_at": "2026-01-01T00:00:00Z",
              "outcome": "in_progress",
              "aggregates": {},
              "events": [],
              "instance_numbers_by_def": {},
              "def_counters": {},
              "relic_aggregates": {
                "RELIC.STONE_CRACKER": {}
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        var agg = run!.RelicAggregates[StoneCrackerRelicId];
        Assert.Equal(0, agg.Activations);
        Assert.Equal(0, agg.CardsUpgraded);
        Assert.Equal(0, agg.StoneCrackerUpgradedCommons);
        Assert.Equal(0, agg.StoneCrackerUpgradedUncommons);
        Assert.Equal(0, agg.StoneCrackerUpgradedRares);
        Assert.Equal(0, agg.StoneCrackerUpgradedCardPlays);
        Assert.Equal(0, agg.StoneCrackerCombats);
        Assert.Equal(0, agg.StoneCrackerTurns);
    }
}
