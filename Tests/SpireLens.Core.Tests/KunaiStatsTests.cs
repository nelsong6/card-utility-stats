using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class KunaiStatsTests
{
    private const string KunaiRelicId = "RELIC.KUNAI";

    private static readonly MethodInfo BuildKunaiBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildKunaiBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildKunaiBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_KunaiFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.KunaiAttacksPlayed);
        Assert.Equal(0, agg.Activations);
        Assert.Equal(0, agg.KunaiDexterityGained);
        Assert.Equal(0, agg.KunaiTurnsEndedAt1Charge);
        Assert.Equal(0, agg.KunaiTurnsEndedAt2Charges);
        Assert.Equal(0, agg.KunaiTurnEndChargeTotal);
        Assert.Equal(0, agg.KunaiTurnEndChargeCount);
    }

    [Fact]
    public void RelicAggregate_KunaiFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[KunaiRelicId] = new RelicAggregate
        {
            KunaiAttacksPlayed = 14,
            Activations = 4,
            KunaiDexterityGained = 4,
            KunaiTurnsEndedAt1Charge = 2,
            KunaiTurnsEndedAt2Charges = 3,
            KunaiTurnEndChargeTotal = 11,
            KunaiTurnEndChargeCount = 7,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("kunai_attacks_played", json);
        Assert.Contains("activations", json);
        Assert.Contains("kunai_dexterity_gained", json);
        Assert.Contains("kunai_turns_ended_at1_charge", json);
        Assert.Contains("kunai_turns_ended_at2_charges", json);
        Assert.Contains("kunai_turn_end_charge_total", json);
        Assert.Contains("kunai_turn_end_charge_count", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[KunaiRelicId];
        Assert.Equal(14, agg.KunaiAttacksPlayed);
        Assert.Equal(4, agg.Activations);
        Assert.Equal(4, agg.KunaiDexterityGained);
        Assert.Equal(2, agg.KunaiTurnsEndedAt1Charge);
        Assert.Equal(3, agg.KunaiTurnsEndedAt2Charges);
        Assert.Equal(11, agg.KunaiTurnEndChargeTotal);
        Assert.Equal(7, agg.KunaiTurnEndChargeCount);
    }

    [Fact]
    public void RunTracker_KunaiHelpers_AccumulateAndClamp()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordKunaiAttackPlayedForTest(agg, 14);
        RunTracker.RecordKunaiAttackPlayedForTest(agg, -2);
        RunTracker.RecordKunaiActivationForTest(agg, 4);
        RunTracker.RecordKunaiActivationForTest(agg, -3);
        RunTracker.RecordKunaiTurnEndChargeForTest(agg, 1);
        RunTracker.RecordKunaiTurnEndChargeForTest(agg, 2);
        RunTracker.RecordKunaiTurnEndChargeForTest(agg, 5);
        RunTracker.RecordKunaiTurnEndChargeForTest(agg, -1);

        Assert.Equal(14, agg.KunaiAttacksPlayed);
        Assert.Equal(2, agg.Activations);
        Assert.Equal(4, agg.KunaiDexterityGained);
        Assert.Equal(1, agg.KunaiTurnsEndedAt1Charge);
        Assert.Equal(2, agg.KunaiTurnsEndedAt2Charges);
        Assert.Equal(5, agg.KunaiTurnEndChargeTotal);
        Assert.Equal(3, agg.KunaiTurnEndChargeCount);
    }

    [Fact]
    public void MergeRelicAggregateInto_KunaiFields_Accumulates()
    {
        var target = new RelicAggregate
        {
            KunaiAttacksPlayed = 5,
            Activations = 1,
            KunaiDexterityGained = 1,
            KunaiTurnsEndedAt1Charge = 1,
            KunaiTurnEndChargeTotal = 1,
            KunaiTurnEndChargeCount = 1,
        };
        var source = new RelicAggregate
        {
            KunaiAttacksPlayed = 9,
            Activations = 3,
            KunaiDexterityGained = 3,
            KunaiTurnsEndedAt2Charges = 2,
            KunaiTurnEndChargeTotal = 4,
            KunaiTurnEndChargeCount = 2,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(14, target.KunaiAttacksPlayed);
        Assert.Equal(4, target.Activations);
        Assert.Equal(4, target.KunaiDexterityGained);
        Assert.Equal(1, target.KunaiTurnsEndedAt1Charge);
        Assert.Equal(2, target.KunaiTurnsEndedAt2Charges);
        Assert.Equal(5, target.KunaiTurnEndChargeTotal);
        Assert.Equal(3, target.KunaiTurnEndChargeCount);
    }

    [Fact]
    public void RelicTooltip_Kunai_ShowsAttackActivationAndChargeRows()
    {
        var body = BuildBody(new RelicAggregate
        {
            KunaiAttacksPlayed = 14,
            Activations = 4,
            KunaiDexterityGained = 4,
            KunaiTurnsEndedAt1Charge = 2,
            KunaiTurnsEndedAt2Charges = 3,
            KunaiTurnEndChargeTotal = 11,
            KunaiTurnEndChargeCount = 7,
        });

        Assert.Contains("Attacks played", body);
        Assert.Contains("Activations", body);
        Assert.Contains("Dexterity gained", body);
        Assert.Contains("Turns ended at 1 charge", body);
        Assert.Contains("Turns ended at 2 charges", body);
        Assert.Contains("Avg charge at turn end", body);
        Assert.Contains("[b]14[/b]", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[b]1.57[/b]", body);
    }

    [Fact]
    public void RelicTooltip_Kunai_ShowsZeroRowsForEmptyAggregate()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Attacks played", body);
        Assert.Contains("Activations", body);
        Assert.Contains("Dexterity gained", body);
        Assert.Contains("Turns ended at 1 charge", body);
        Assert.Contains("Turns ended at 2 charges", body);
        Assert.Contains("Avg charge at turn end", body);
        Assert.Equal(6, CountOccurrences(body, "[b]0[/b]"));
    }

    [Fact]
    public void RunData_OlderShapeWithoutKunaiFields_DeserializesWithZeroDefaults()
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
                "RELIC.KUNAI": {}
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        var agg = run!.RelicAggregates[KunaiRelicId];
        Assert.Equal(0, agg.KunaiAttacksPlayed);
        Assert.Equal(0, agg.Activations);
        Assert.Equal(0, agg.KunaiDexterityGained);
        Assert.Equal(0, agg.KunaiTurnsEndedAt1Charge);
        Assert.Equal(0, agg.KunaiTurnsEndedAt2Charges);
        Assert.Equal(0, agg.KunaiTurnEndChargeTotal);
        Assert.Equal(0, agg.KunaiTurnEndChargeCount);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildKunaiBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildKunaiBodyBBCode returned null."));

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
