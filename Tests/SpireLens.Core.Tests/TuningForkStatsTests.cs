using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class TuningForkStatsTests
{
    private const string TuningForkRelicId = "RELIC.TUNING_FORK";

    private static readonly MethodInfo BuildTuningForkBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildTuningForkBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildTuningForkBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_TuningForkFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.TuningForkSkillsPlayed);
        Assert.Equal(0, agg.Activations);
        Assert.Equal(0, agg.AdditionalBlockGained);
        Assert.Equal(0, agg.TuningForkCombats);
        Assert.Equal(0, agg.TuningForkTurns);
        Assert.Equal(0, agg.TuningForkTurnsEndedOn8Charges);
        Assert.Equal(0, agg.TuningForkTurnsEndedOn9Charges);
        Assert.Equal(0, agg.TuningForkTurnEndChargeTotal);
        Assert.Equal(0, agg.TuningForkTurnEndChargeCount);
    }

    [Fact]
    public void RelicAggregate_TuningForkFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[TuningForkRelicId] = new RelicAggregate
        {
            TuningForkSkillsPlayed = 27,
            Activations = 3,
            AdditionalBlockGained = 18,
            TuningForkCombats = 2,
            TuningForkTurns = 7,
            TuningForkTurnsEndedOn8Charges = 2,
            TuningForkTurnsEndedOn9Charges = 1,
            TuningForkTurnEndChargeTotal = 31,
            TuningForkTurnEndChargeCount = 5,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("tuning_fork_skills_played", json);
        Assert.Contains("activations", json);
        Assert.Contains("additional_block_gained", json);
        Assert.Contains("tuning_fork_combats", json);
        Assert.Contains("tuning_fork_turns", json);
        Assert.Contains("tuning_fork_turns_ended_on8_charges", json);
        Assert.Contains("tuning_fork_turns_ended_on9_charges", json);
        Assert.Contains("tuning_fork_turn_end_charge_total", json);
        Assert.Contains("tuning_fork_turn_end_charge_count", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[TuningForkRelicId];
        Assert.Equal(27, agg.TuningForkSkillsPlayed);
        Assert.Equal(3, agg.Activations);
        Assert.Equal(18, agg.AdditionalBlockGained);
        Assert.Equal(2, agg.TuningForkCombats);
        Assert.Equal(7, agg.TuningForkTurns);
        Assert.Equal(2, agg.TuningForkTurnsEndedOn8Charges);
        Assert.Equal(1, agg.TuningForkTurnsEndedOn9Charges);
        Assert.Equal(31, agg.TuningForkTurnEndChargeTotal);
        Assert.Equal(5, agg.TuningForkTurnEndChargeCount);
    }

    [Fact]
    public void RunTracker_TuningForkHelpers_AccumulateAndClamp()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordTuningForkSkillPlayedForTest(agg, 27);
        RunTracker.RecordTuningForkSkillPlayedForTest(agg, -2);
        RunTracker.RecordTuningForkCombatForTest(agg, 2);
        RunTracker.RecordTuningForkCombatForTest(agg, -2);
        RunTracker.RecordTuningForkTurnForTest(agg, 7);
        RunTracker.RecordTuningForkTurnForTest(agg, -4);
        RunTracker.RecordTuningForkTurnEndChargeForTest(agg, 8);
        RunTracker.RecordTuningForkTurnEndChargeForTest(agg, 9);
        RunTracker.RecordTuningForkTurnEndChargeForTest(agg, 14);
        RunTracker.RecordTuningForkTurnEndChargeForTest(agg, -1);

        Assert.Equal(27, agg.TuningForkSkillsPlayed);
        Assert.Equal(2, agg.TuningForkCombats);
        Assert.Equal(7, agg.TuningForkTurns);
        Assert.Equal(1, agg.TuningForkTurnsEndedOn8Charges);
        Assert.Equal(1, agg.TuningForkTurnsEndedOn9Charges);
        Assert.Equal(21, agg.TuningForkTurnEndChargeTotal);
        Assert.Equal(3, agg.TuningForkTurnEndChargeCount);
    }

    [Fact]
    public void MergeRelicAggregateInto_TuningForkFields_Accumulates()
    {
        var target = new RelicAggregate
        {
            TuningForkSkillsPlayed = 12,
            Activations = 1,
            AdditionalBlockGained = 7,
            TuningForkCombats = 1,
            TuningForkTurns = 3,
            TuningForkTurnsEndedOn8Charges = 1,
            TuningForkTurnEndChargeTotal = 8,
            TuningForkTurnEndChargeCount = 1,
        };
        var source = new RelicAggregate
        {
            TuningForkSkillsPlayed = 15,
            Activations = 2,
            AdditionalBlockGained = 11,
            TuningForkCombats = 1,
            TuningForkTurns = 4,
            TuningForkTurnsEndedOn9Charges = 1,
            TuningForkTurnEndChargeTotal = 23,
            TuningForkTurnEndChargeCount = 4,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(27, target.TuningForkSkillsPlayed);
        Assert.Equal(3, target.Activations);
        Assert.Equal(18, target.AdditionalBlockGained);
        Assert.Equal(2, target.TuningForkCombats);
        Assert.Equal(7, target.TuningForkTurns);
        Assert.Equal(1, target.TuningForkTurnsEndedOn8Charges);
        Assert.Equal(1, target.TuningForkTurnsEndedOn9Charges);
        Assert.Equal(31, target.TuningForkTurnEndChargeTotal);
        Assert.Equal(5, target.TuningForkTurnEndChargeCount);
    }

    [Fact]
    public void RelicTooltip_TuningFork_ShowsCounterActivationBlockAndChargeRows()
    {
        var body = BuildBody(new RelicAggregate
        {
            TuningForkSkillsPlayed = 27,
            Activations = 3,
            AdditionalBlockGained = 18,
            TuningForkCombats = 2,
            TuningForkTurns = 7,
            TuningForkTurnsEndedOn8Charges = 2,
            TuningForkTurnsEndedOn9Charges = 1,
            TuningForkTurnEndChargeTotal = 31,
            TuningForkTurnEndChargeCount = 5,
        });

        Assert.Contains("Skills played", body);
        Assert.Contains("Activations", body);
        Assert.Contains("[img=16x16]res://images/ui/combat/block.png[/img] block gained", body);
        Assert.Contains("[img=16x16]res://images/ui/combat/block.png[/img] block gained per activation", body);
        Assert.Contains("Avg skills played per combat", body);
        Assert.Contains("Avg skills played per turn", body);
        Assert.Contains("Turns ended on 8 charges", body);
        Assert.Contains("Turns ended on 9 charges", body);
        Assert.Contains("Avg charge at turn end", body);
        Assert.Contains("[b]27[/b]", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[b]18[/b]", body);
        Assert.Contains("[b]6[/b]", body);
        Assert.Contains("[b]13.5[/b]", body);
        Assert.Contains("[b]3.86[/b]", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]1[/b]", body);
        Assert.Contains("[b]6.2[/b]", body);
    }

    [Fact]
    public void RelicTooltip_TuningFork_ShowsZeroRows()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Activations", body);
        Assert.Contains("Skills played", body);
        Assert.Contains("block gained", body);
        Assert.Contains("block gained per activation", body);
        Assert.Contains("Avg skills played per combat", body);
        Assert.Contains("Avg skills played per turn", body);
        Assert.Contains("Turns ended on 8 charges", body);
        Assert.Contains("Turns ended on 9 charges", body);
        Assert.Contains("Avg charge at turn end", body);
        Assert.Equal(9, CountOccurrences(body, "[b]0[/b]"));
    }

    [Fact]
    public void RunData_OlderShapeWithoutTuningForkFields_DeserializesWithZeroDefaults()
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
                "RELIC.TUNING_FORK": {
                  "activations": 3,
                  "additional_block_gained": 18
                }
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        var agg = run!.RelicAggregates[TuningForkRelicId];
        Assert.Equal(0, agg.TuningForkSkillsPlayed);
        Assert.Equal(3, agg.Activations);
        Assert.Equal(18, agg.AdditionalBlockGained);
        Assert.Equal(0, agg.TuningForkCombats);
        Assert.Equal(0, agg.TuningForkTurns);
        Assert.Equal(0, agg.TuningForkTurnsEndedOn8Charges);
        Assert.Equal(0, agg.TuningForkTurnsEndedOn9Charges);
        Assert.Equal(0, agg.TuningForkTurnEndChargeTotal);
        Assert.Equal(0, agg.TuningForkTurnEndChargeCount);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildTuningForkBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildTuningForkBodyBBCode returned null."));

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
