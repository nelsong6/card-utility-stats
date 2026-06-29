using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    public void RelicAggregate_ReptileTrinketFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.StrengthAdded);
    }

    [Fact]
    public void RelicAggregate_ReptileTrinketFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[ReptileTrinketRelicId] = new RelicAggregate
        {
            Activations = 3,
            StrengthAdded = 6m,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("strength_added", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[ReptileTrinketRelicId];
        Assert.Equal(3, agg.Activations);
        Assert.Equal(6m, agg.StrengthAdded);
    }

    [Fact]
    public void RelicTooltip_ReptileTrinket_ShowsActivationsAndStrengthAdded()
    {
        var agg = new RelicAggregate
        {
            Activations = 3,
            StrengthAdded = 6m,
        };

        var body = (string)(BuildReptileTrinketBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildReptileTrinketBodyBBCode returned null."));

        Assert.Contains("Activations", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("Strength added", body);
        Assert.Contains("[b]6[/b]", body);
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
    }
}
