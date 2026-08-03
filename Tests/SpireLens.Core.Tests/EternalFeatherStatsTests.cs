using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class EternalFeatherStatsTests
{
    private const string EternalFeatherRelicId = "RELIC.ETERNAL_FEATHER";

    private static readonly MethodInfo BuildEternalFeatherBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildEternalFeatherBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildEternalFeatherBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_EternalFeatherFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.TotalHealingAttempted);
        Assert.Equal(0m, agg.TotalHealingRestored);
        Assert.Equal(0m, agg.TotalHealingLost);
        Assert.Empty(agg.HealingLostReasons);
        Assert.Empty(agg.EternalFeatherHealingActivations);
    }

    [Fact]
    public void RelicAggregate_EternalFeatherFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        var agg = new RelicAggregate
        {
            Activations = 2,
            TotalHealingAttempted = 18m,
            TotalHealingRestored = 11m,
            TotalHealingLost = 7m,
        };
        agg.HealingLostReasons["full_hp"] = new HealingLostReasonAggregate
        {
            ReasonId = "full_hp",
            DisplayName = "full HP",
            Amount = 7m,
        };
        agg.EternalFeatherHealingActivations.Add(
            new EternalFeatherHealingActivationAggregate
            {
                Floor = 7,
                HpRestored = 11m,
            });
        run.RelicAggregates[EternalFeatherRelicId] = agg;

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("total_healing_attempted", json);
        Assert.Contains("healing_lost_reasons", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[EternalFeatherRelicId];
        Assert.Equal(2, restoredAgg.Activations);
        Assert.Equal(18m, restoredAgg.TotalHealingAttempted);
        Assert.Equal(11m, restoredAgg.TotalHealingRestored);
        Assert.Equal(7m, restoredAgg.TotalHealingLost);
        Assert.Equal(7m, restoredAgg.HealingLostReasons["full_hp"].Amount);
        var activation = Assert.Single(restoredAgg.EternalFeatherHealingActivations);
        Assert.Equal(7, activation.Floor);
        Assert.Equal(11m, activation.HpRestored);
    }

    [Fact]
    public void RelicTooltip_EternalFeather_ShowsActivationsAndHealing()
    {
        var agg = new RelicAggregate
        {
            Activations = 2,
            TotalHealingRestored = 11m,
            TotalHealingLost = 7m,
        };
        agg.HealingLostReasons["full_hp"] = new HealingLostReasonAggregate
        {
            ReasonId = "full_hp",
            DisplayName = "full HP",
            Amount = 7m,
        };

        var body = (string)(BuildEternalFeatherBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildEternalFeatherBodyBBCode returned null."));

        Assert.Contains("Activations", body);
        Assert.Contains("HP healed", body);
        Assert.Contains("healing lost", body);
        Assert.DoesNotContain("lost to full HP", body);
        Assert.Contains("[b]11[/b]", body);
        Assert.Contains("[b]7[/b]", body);
    }

    [Fact]
    public void RelicTooltip_EternalFeather_ShowsZeroHealingRows()
    {
        var body = (string)(BuildEternalFeatherBodyMethod.Invoke(null, new object?[] { new RelicAggregate() })
            ?? throw new InvalidOperationException("BuildEternalFeatherBodyBBCode returned null."));

        Assert.Contains("Activations", body);
        Assert.Contains("HP healed", body);
        Assert.Contains("healing lost", body);
        Assert.Contains("[b]0[/b]", body);
    }

    [Fact]
    public void RunData_OlderShapeWithoutEternalFeatherFields_DeserializesWithZeroDefaults()
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
                "RELIC.ETERNAL_FEATHER": {}
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        var agg = run!.RelicAggregates[EternalFeatherRelicId];
        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.TotalHealingAttempted);
        Assert.Equal(0m, agg.TotalHealingRestored);
        Assert.Equal(0m, agg.TotalHealingLost);
        Assert.Empty(agg.HealingLostReasons);
        Assert.Empty(agg.EternalFeatherHealingActivations);
    }
}
