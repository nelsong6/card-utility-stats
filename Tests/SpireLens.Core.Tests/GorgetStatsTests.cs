using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class GorgetStatsTests
{
    private const string GorgetRelicId = "RELIC.GORGET";

    private static readonly MethodInfo BuildGorgetBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildGorgetBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildGorgetBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_GorgetFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.PlatingAdded);
    }

    [Fact]
    public void RelicAggregate_GorgetFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[GorgetRelicId] = new RelicAggregate
        {
            Activations = 4,
            PlatingAdded = 12m,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("plating_added", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[GorgetRelicId];
        Assert.Equal(4, agg.Activations);
        Assert.Equal(12m, agg.PlatingAdded);
    }

    [Fact]
    public void RelicTooltip_Gorget_ShowsActivationsAndPlatingAdded()
    {
        var agg = new RelicAggregate
        {
            Activations = 4,
            PlatingAdded = 12m,
        };

        var body = (string)(BuildGorgetBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildGorgetBodyBBCode returned null."));

        Assert.Contains("Activations", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.Contains("Plating added", body);
        Assert.Contains("[b]12[/b]", body);
    }

    [Fact]
    public void RunData_OlderShapeWithoutGorgetFields_DeserializesWithZeroDefaults()
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
                "RELIC.GORGET": {}
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        var agg = run!.RelicAggregates[GorgetRelicId];
        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.PlatingAdded);
    }
}
