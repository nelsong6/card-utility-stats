using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

/// <summary>
/// Tests Pocketwatch's persisted observation math. Live hook timing remains
/// user-owned gameplay verification.
/// </summary>
public class PocketwatchStatsTests
{
    private const string PocketwatchRelicId = "RELIC.POCKETWATCH";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void PocketwatchFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.AdditionalCardsDrawn);
        Assert.Equal(0, agg.PocketwatchTurns);
        Assert.Equal(0, agg.PocketwatchCombats);
        Assert.Equal(0, agg.PocketwatchTurnEndCountTotal);
        Assert.Equal(0, agg.PocketwatchTurnsActivationMissed);
        Assert.Equal(0, agg.PocketwatchActivatedTurnEndCountTotal);
        Assert.Equal(0, agg.PocketwatchActivationValueSamples);
        Assert.Equal(0, agg.PocketwatchMissedTurnEndCountTotal);
    }

    [Fact]
    public void PocketwatchFields_JsonRoundtrip_PreservesObservationState()
    {
        var agg = new RelicAggregate
        {
            Activations = 3,
            AdditionalCardsDrawn = 9,
            PocketwatchTurns = 7,
            PocketwatchCombats = 2,
            PocketwatchTurnEndCountTotal = 19,
            PocketwatchTurnsActivationMissed = 2,
            PocketwatchActivatedTurnEndCountTotal = 5,
            PocketwatchActivationValueSamples = 3,
            PocketwatchMissedTurnEndCountTotal = 11,
        };
        var run = new RunData();
        run.RelicAggregates[PocketwatchRelicId] = agg;

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("relic_aggregates", json);
        Assert.Contains("additional_cards_drawn", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);
        Assert.NotNull(restored);
        Assert.True(restored!.RelicAggregates.ContainsKey(PocketwatchRelicId));
        var restoredAgg = restored.RelicAggregates[PocketwatchRelicId];
        Assert.Equal(3, restoredAgg.Activations);
        Assert.Equal(9, restoredAgg.AdditionalCardsDrawn);
        Assert.Equal(7, restoredAgg.PocketwatchTurns);
        Assert.Equal(2, restoredAgg.PocketwatchCombats);
        Assert.Equal(19, restoredAgg.PocketwatchTurnEndCountTotal);
        Assert.Equal(2, restoredAgg.PocketwatchTurnsActivationMissed);
        Assert.Equal(5, restoredAgg.PocketwatchActivatedTurnEndCountTotal);
        Assert.Equal(3, restoredAgg.PocketwatchActivationValueSamples);
        Assert.Equal(11, restoredAgg.PocketwatchMissedTurnEndCountTotal);
    }

    [Fact]
    public void ObservationMath_DistinguishesQualifiedActivationsFromMissedTurns()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordPocketwatchCombatForTest(agg, 2);
        RunTracker.RecordPocketwatchTurnEndForTest(agg, cardCount: 2, activationThreshold: 3);
        RunTracker.RecordPocketwatchTurnEndForTest(agg, cardCount: 5, activationThreshold: 3);
        RunTracker.RecordPocketwatchTurnEndForTest(agg, cardCount: 7, activationThreshold: 3);
        RunTracker.RecordPocketwatchActivationForTest(agg, cardsDrawn: 3, cardsPlayedLastTurn: 2);
        RunTracker.RecordPocketwatchActivationForTest(agg, cardsDrawn: 3, cardsPlayedLastTurn: 0);

        Assert.Equal(2, agg.PocketwatchCombats);
        Assert.Equal(3, agg.PocketwatchTurns);
        Assert.Equal(14, agg.PocketwatchTurnEndCountTotal);
        Assert.Equal(2, agg.PocketwatchTurnsActivationMissed);
        Assert.Equal(12, agg.PocketwatchMissedTurnEndCountTotal);
        Assert.Equal(2, agg.Activations);
        Assert.Equal(6, agg.AdditionalCardsDrawn);
        Assert.Equal(2, agg.PocketwatchActivatedTurnEndCountTotal);
        Assert.Equal(2, agg.PocketwatchActivationValueSamples);
    }

    [Fact]
    public void MergeRelicAggregateInto_AccumulatesPocketwatchNumeratorsAndDenominators()
    {
        var target = new RelicAggregate
        {
            Activations = 1,
            AdditionalCardsDrawn = 3,
            PocketwatchTurns = 2,
            PocketwatchCombats = 1,
            PocketwatchTurnEndCountTotal = 5,
            PocketwatchActivatedTurnEndCountTotal = 2,
            PocketwatchActivationValueSamples = 1,
        };
        var source = new RelicAggregate
        {
            Activations = 2,
            AdditionalCardsDrawn = 6,
            PocketwatchTurns = 3,
            PocketwatchCombats = 1,
            PocketwatchTurnEndCountTotal = 12,
            PocketwatchTurnsActivationMissed = 2,
            PocketwatchActivatedTurnEndCountTotal = 3,
            PocketwatchActivationValueSamples = 2,
            PocketwatchMissedTurnEndCountTotal = 10,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(3, target.Activations);
        Assert.Equal(9, target.AdditionalCardsDrawn);
        Assert.Equal(5, target.PocketwatchTurns);
        Assert.Equal(2, target.PocketwatchCombats);
        Assert.Equal(17, target.PocketwatchTurnEndCountTotal);
        Assert.Equal(2, target.PocketwatchTurnsActivationMissed);
        Assert.Equal(5, target.PocketwatchActivatedTurnEndCountTotal);
        Assert.Equal(3, target.PocketwatchActivationValueSamples);
        Assert.Equal(10, target.PocketwatchMissedTurnEndCountTotal);
    }

    [Fact]
    public void RunData_OlderShapeWithoutAdditionalCardsDrawn_DeserializesWithZeroDefault()
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
                "RELIC.POCKETWATCH": {
                  "enemies_affected": 0,
                  "vulnerable_applied": 0,
                  "weak_applied": 0
                }
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        Assert.True(run!.RelicAggregates.ContainsKey(PocketwatchRelicId));
        var agg = run.RelicAggregates[PocketwatchRelicId];
        Assert.Equal(0, agg.AdditionalCardsDrawn);
        Assert.Equal(0, agg.PocketwatchTurns);
        Assert.Equal(0, agg.PocketwatchCombats);
        Assert.Equal(0, agg.PocketwatchTurnsActivationMissed);
    }
}
