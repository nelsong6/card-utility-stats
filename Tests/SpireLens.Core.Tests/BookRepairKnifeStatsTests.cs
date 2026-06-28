using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class BookRepairKnifeStatsTests
{
    private const string BookRepairKnifeRelicId = "RELIC.BOOK_REPAIR_KNIFE";

    private static readonly MethodInfo BuildBookRepairKnifeBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildBookRepairKnifeBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildBookRepairKnifeBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_BookRepairKnifeFields_DefaultToZero()
    {
        var agg = new RelicAggregate();
        Assert.Equal(0, agg.DoomDeathTriggers);
        Assert.Equal(0, agg.DoomKills);
        Assert.Equal(0m, agg.TotalHealingAttempted);
        Assert.Equal(0m, agg.TotalHealingRestored);
        Assert.Equal(0m, agg.TotalHealingLost);
        Assert.Empty(agg.HealingLostReasons);
    }

    [Fact]
    public void RelicAggregate_BookRepairKnifeFields_JsonRoundtrip_PreserveFields()
    {
        var agg = new RelicAggregate
        {
            DoomDeathTriggers = 4,
            DoomKills = 4,
            TotalHealingAttempted = 24m,
            TotalHealingRestored = 15m,
            TotalHealingLost = 9m,
        };
        agg.HealingLostReasons["full_hp"] = new HealingLostReasonAggregate
        {
            ReasonId = "full_hp",
            DisplayName = "full HP",
            Amount = 9m,
        };
        var run = new RunData();
        run.RelicAggregates[BookRepairKnifeRelicId] = agg;

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("doom_death_triggers", json);
        Assert.Contains("doom_kills", json);
        Assert.Contains("total_healing_attempted", json);
        Assert.Contains("total_healing_restored", json);
        Assert.Contains("total_healing_lost", json);
        Assert.Contains("healing_lost_reasons", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);
        Assert.NotNull(restored);
        Assert.True(restored!.RelicAggregates.ContainsKey(BookRepairKnifeRelicId));
        Assert.Equal(4, restored.RelicAggregates[BookRepairKnifeRelicId].DoomDeathTriggers);
        Assert.Equal(4, restored.RelicAggregates[BookRepairKnifeRelicId].DoomKills);
        Assert.Equal(24m, restored.RelicAggregates[BookRepairKnifeRelicId].TotalHealingAttempted);
        Assert.Equal(15m, restored.RelicAggregates[BookRepairKnifeRelicId].TotalHealingRestored);
        Assert.Equal(9m, restored.RelicAggregates[BookRepairKnifeRelicId].TotalHealingLost);
        Assert.Equal(9m, restored.RelicAggregates[BookRepairKnifeRelicId].HealingLostReasons["full_hp"].Amount);
    }

    [Fact]
    public void RelicAggregate_BookRepairKnifeFields_SeparateTriggersFromPayload()
    {
        var run = new RunData();
        var agg = new RelicAggregate();
        run.RelicAggregates[BookRepairKnifeRelicId] = agg;

        agg.DoomDeathTriggers++;
        agg.DoomKills += 1;
        agg.DoomDeathTriggers += 2;
        agg.DoomKills += 2;

        Assert.Equal(3, run.RelicAggregates[BookRepairKnifeRelicId].DoomDeathTriggers);
        Assert.Equal(3, run.RelicAggregates[BookRepairKnifeRelicId].DoomKills);
    }

    [Fact]
    public void RelicTooltip_BookRepairKnifeFields_ShowDoomKillPayloadTotal()
    {
        var agg = new RelicAggregate
        {
            DoomDeathTriggers = 3,
            DoomKills = 3,
            TotalHealingRestored = 10m,
            TotalHealingLost = 8m,
        };
        agg.HealingLostReasons["full_hp"] = new HealingLostReasonAggregate
        {
            ReasonId = "full_hp",
            DisplayName = "full HP",
            Amount = 6m,
        };
        agg.HealingLostReasons["other"] = new HealingLostReasonAggregate
        {
            ReasonId = "other",
            DisplayName = "other/prevented",
            Amount = 2m,
        };

        var body = (string)(BuildBookRepairKnifeBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildBookRepairKnifeBodyBBCode returned null."));

        Assert.DoesNotContain("Times triggered", body);
        Assert.Contains("Doom kills", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("HP healed", body);
        Assert.Contains("[b]10[/b]", body);
        Assert.Contains("healing lost", body);
        Assert.Contains("[b]8[/b]", body);
        Assert.Contains("lost to full HP", body);
        Assert.Contains("[b]6[/b]", body);
        Assert.Contains("lost to other/prevented", body);
        Assert.Contains("[b]2[/b]", body);
    }

    [Fact]
    public void RunData_OlderShapeWithoutBookRepairKnifeFields_DeserializesWithZeroDefaults()
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
                "RELIC.BOOK_REPAIR_KNIFE": {
                  "enemies_affected": 0,
                  "vulnerable_applied": 0,
                  "weak_applied": 0
                }
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        Assert.True(run!.RelicAggregates.ContainsKey(BookRepairKnifeRelicId));
        Assert.Equal(0, run.RelicAggregates[BookRepairKnifeRelicId].DoomDeathTriggers);
        Assert.Equal(0, run.RelicAggregates[BookRepairKnifeRelicId].DoomKills);
        Assert.Equal(0m, run.RelicAggregates[BookRepairKnifeRelicId].TotalHealingAttempted);
        Assert.Equal(0m, run.RelicAggregates[BookRepairKnifeRelicId].TotalHealingRestored);
        Assert.Equal(0m, run.RelicAggregates[BookRepairKnifeRelicId].TotalHealingLost);
        Assert.Empty(run.RelicAggregates[BookRepairKnifeRelicId].HealingLostReasons);
    }
}
