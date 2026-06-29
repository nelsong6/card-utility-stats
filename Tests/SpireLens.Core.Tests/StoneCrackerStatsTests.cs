using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    }

    [Fact]
    public void RelicAggregate_StoneCrackerFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[StoneCrackerRelicId] = new RelicAggregate
        {
            Activations = 3,
            CardsUpgraded = 6,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("cards_upgraded", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[StoneCrackerRelicId];
        Assert.Equal(3, agg.Activations);
        Assert.Equal(6, agg.CardsUpgraded);
    }

    [Fact]
    public void RelicTooltip_StoneCracker_ShowsActivationsAndCardsUpgraded()
    {
        var agg = new RelicAggregate
        {
            Activations = 3,
            CardsUpgraded = 6,
        };

        var body = (string)(BuildStoneCrackerBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildStoneCrackerBodyBBCode returned null."));

        Assert.Contains("Activations", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("Cards upgraded", body);
        Assert.Contains("[b]6[/b]", body);
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
    }
}
