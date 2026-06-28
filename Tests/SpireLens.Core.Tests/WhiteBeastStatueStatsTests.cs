using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class WhiteBeastStatueStatsTests
{
    private const string WhiteBeastStatueRelicId = "RELIC.WHITE_BEAST_STATUE";

    private static readonly MethodInfo BuildWhiteBeastStatueBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildWhiteBeastStatueBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildWhiteBeastStatueBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_WhiteBeastStatueFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.PotionsGained);
        Assert.Equal(0, agg.CommonPotionsGained);
        Assert.Equal(0, agg.UncommonPotionsGained);
        Assert.Equal(0, agg.RarePotionsGained);
    }

    [Fact]
    public void RelicAggregate_WhiteBeastStatueFields_JsonRoundtrip_PreserveFields()
    {
        var agg = new RelicAggregate
        {
            PotionsGained = 5,
            CommonPotionsGained = 2,
            UncommonPotionsGained = 2,
            RarePotionsGained = 1,
        };
        var run = new RunData();
        run.RelicAggregates[WhiteBeastStatueRelicId] = agg;

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("potions_gained", json);
        Assert.Contains("common_potions_gained", json);
        Assert.Contains("uncommon_potions_gained", json);
        Assert.Contains("rare_potions_gained", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        Assert.True(restored!.RelicAggregates.ContainsKey(WhiteBeastStatueRelicId));
        var restoredAgg = restored.RelicAggregates[WhiteBeastStatueRelicId];
        Assert.Equal(5, restoredAgg.PotionsGained);
        Assert.Equal(2, restoredAgg.CommonPotionsGained);
        Assert.Equal(2, restoredAgg.UncommonPotionsGained);
        Assert.Equal(1, restoredAgg.RarePotionsGained);
    }

    [Fact]
    public void RelicTooltip_WhiteBeastStatueFields_ShowTotalAndRarityBreakdown()
    {
        var agg = new RelicAggregate
        {
            PotionsGained = 5,
            CommonPotionsGained = 2,
            UncommonPotionsGained = 2,
            RarePotionsGained = 1,
        };

        var body = (string)(BuildWhiteBeastStatueBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildWhiteBeastStatueBodyBBCode returned null."));

        Assert.Contains("Potions gained", body);
        Assert.Contains("[b]5[/b]", body);
        Assert.Contains("common potions", body);
        Assert.Contains("uncommon potions", body);
        Assert.Contains("rare potions", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]1[/b]", body);
    }

    [Fact]
    public void RunData_OlderShapeWithoutWhiteBeastStatueFields_DeserializesWithZeroDefault()
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
                "RELIC.WHITE_BEAST_STATUE": {
                  "activations": 0
                }
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        Assert.True(run!.RelicAggregates.ContainsKey(WhiteBeastStatueRelicId));
        var agg = run.RelicAggregates[WhiteBeastStatueRelicId];
        Assert.Equal(0, agg.PotionsGained);
        Assert.Equal(0, agg.CommonPotionsGained);
        Assert.Equal(0, agg.UncommonPotionsGained);
        Assert.Equal(0, agg.RarePotionsGained);
    }
}
