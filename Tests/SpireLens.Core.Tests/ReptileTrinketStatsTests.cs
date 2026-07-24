using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class ReptileTrinketStatsTests
{
    private const string ReptileTrinketRelicId = "RELIC.REPTILE_TRINKET";

    private static readonly MethodInfo BuildReptileTrinketBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildReptileTrinketBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildReptileTrinketBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void Patches_TargetOwnerPotionCallbackAndPlayerTurnStart()
    {
        Assert.NotNull(typeof(ReptileTrinket).GetMethod(
            nameof(ReptileTrinket.AfterPotionUsed)));
        Assert.NotNull(typeof(Hook).GetMethod(
            nameof(Hook.AfterPlayerTurnStart),
            new[] { typeof(ICombatState), typeof(PlayerChoiceContext), typeof(Player) }));
    }

    [Fact]
    public void RelicAggregate_ReptileTrinketFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.StrengthAdded);
        Assert.Equal(0, agg.ReptileTrinketTurns);
        Assert.Equal(0, agg.ReptileTrinketCombats);
        Assert.Equal(0, agg.ReptileTrinketTurnsWithExactlyTwoActivations);
        Assert.Equal(0, agg.ReptileTrinketTurnsWithMoreThanTwoActivations);
    }

    [Fact]
    public void RelicAggregate_ReptileTrinketFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[ReptileTrinketRelicId] = new RelicAggregate
        {
            Activations = 3,
            StrengthAdded = 6m,
            ReptileTrinketTurns = 4,
            ReptileTrinketCombats = 2,
            ReptileTrinketTurnsWithExactlyTwoActivations = 1,
            ReptileTrinketTurnsWithMoreThanTwoActivations = 1,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("strength_added", json);
        Assert.Contains("reptile_trinket_turns", json);
        Assert.Contains("reptile_trinket_combats", json);
        Assert.Contains("reptile_trinket_turns_with_exactly_two_activations", json);
        Assert.Contains("reptile_trinket_turns_with_more_than_two_activations", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[ReptileTrinketRelicId];
        Assert.Equal(3, agg.Activations);
        Assert.Equal(6m, agg.StrengthAdded);
        Assert.Equal(4, agg.ReptileTrinketTurns);
        Assert.Equal(2, agg.ReptileTrinketCombats);
        Assert.Equal(1, agg.ReptileTrinketTurnsWithExactlyTwoActivations);
        Assert.Equal(1, agg.ReptileTrinketTurnsWithMoreThanTwoActivations);
    }

    [Fact]
    public void RelicTooltip_ReptileTrinket_ShowsActivationRatesAndTurnBuckets()
    {
        var agg = new RelicAggregate
        {
            Activations = 9,
            StrengthAdded = 18m,
            ReptileTrinketTurns = 6,
            ReptileTrinketCombats = 3,
            ReptileTrinketTurnsWithExactlyTwoActivations = 2,
            ReptileTrinketTurnsWithMoreThanTwoActivations = 1,
        };

        var body = (string)(BuildReptileTrinketBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildReptileTrinketBodyBBCode returned null."));

        Assert.Contains("Activations", body);
        Assert.Contains("[b]9[/b]", body);
        Assert.Contains("Strength added", body);
        Assert.Contains("[b]18[/b]", body);
        Assert.Contains("Avg activations per turn", body);
        Assert.Contains("[b]1.5[/b]", body);
        Assert.Contains("Avg activations per combat", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("Turns with exactly 2 activations", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("Turns with more than 2 activations", body);
        Assert.Contains("[b]1[/b]", body);
    }

    [Fact]
    public void RunTracker_ReptileTrinketHelpers_MoveThirdActivationToOverTwoBucket()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordReptileTrinketTurnForTest(agg, 3);
        RunTracker.RecordReptileTrinketCombatForTest(agg, 2);

        RecordActivation(agg, 0, 1);
        RecordActivation(agg, 1, 2);
        RecordActivation(agg, 2, 3);
        RecordActivation(agg, 0, 1);
        RecordActivation(agg, 1, 2);

        Assert.Equal(5, agg.Activations);
        Assert.Equal(15m, agg.StrengthAdded);
        Assert.Equal(3, agg.ReptileTrinketTurns);
        Assert.Equal(2, agg.ReptileTrinketCombats);
        Assert.Equal(1, agg.ReptileTrinketTurnsWithExactlyTwoActivations);
        Assert.Equal(1, agg.ReptileTrinketTurnsWithMoreThanTwoActivations);
    }

    [Fact]
    public void RelicAggregate_ReptileTrinketFields_Merge()
    {
        var target = PopulatedAggregate();

        RunTracker.MergeRelicAggregateInto(target, PopulatedAggregate());

        Assert.Equal(18, target.Activations);
        Assert.Equal(36m, target.StrengthAdded);
        Assert.Equal(12, target.ReptileTrinketTurns);
        Assert.Equal(6, target.ReptileTrinketCombats);
        Assert.Equal(4, target.ReptileTrinketTurnsWithExactlyTwoActivations);
        Assert.Equal(2, target.ReptileTrinketTurnsWithMoreThanTwoActivations);
    }

    [Fact]
    public void RunData_OlderShapeWithoutReptileTrinketFields_DeserializesWithZeroDefaults()
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
                "RELIC.REPTILE_TRINKET": {}
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        var agg = run!.RelicAggregates[ReptileTrinketRelicId];
        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.StrengthAdded);
        Assert.Equal(0, agg.ReptileTrinketTurns);
        Assert.Equal(0, agg.ReptileTrinketCombats);
        Assert.Equal(0, agg.ReptileTrinketTurnsWithExactlyTwoActivations);
        Assert.Equal(0, agg.ReptileTrinketTurnsWithMoreThanTwoActivations);
    }

    private static RelicAggregate PopulatedAggregate()
        => new()
        {
            Activations = 9,
            StrengthAdded = 18m,
            ReptileTrinketTurns = 6,
            ReptileTrinketCombats = 3,
            ReptileTrinketTurnsWithExactlyTwoActivations = 2,
            ReptileTrinketTurnsWithMoreThanTwoActivations = 1,
        };

    private static void RecordActivation(
        RelicAggregate agg,
        int previousTurnActivations,
        int currentTurnActivations)
    {
        RunTracker.RecordReptileTrinketActivationForTest(agg, 3m);
        RunTracker.RecordReptileTrinketTurnActivationTransitionForTest(
            agg,
            previousTurnActivations,
            currentTurnActivations);
    }
}
