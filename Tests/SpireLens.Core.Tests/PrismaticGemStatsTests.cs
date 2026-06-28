using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class PrismaticGemStatsTests
{
    private const string PrismaticGemRelicId = "RELIC.PRISMATIC_GEM";

    private static readonly MethodInfo BuildPrismaticGemBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildPrismaticGemBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildPrismaticGemBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_PrismaticGemFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.EnergyGenerated);
        Assert.Equal(0, agg.CardRewardsAffected);
        Assert.Empty(agg.CardRewardCategories);
    }

    [Fact]
    public void RelicAggregate_PrismaticGemFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[PrismaticGemRelicId] = new RelicAggregate
        {
            EnergyGenerated = 4,
            CardRewardsAffected = 2,
            CardRewardCategories =
            {
                ["defect"] = new CardRewardCategoryAggregate { DisplayName = "Defect", Count = 3 },
                ["colorless"] = new CardRewardCategoryAggregate { DisplayName = "Colorless", Count = 1 },
            },
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("energy_generated", json);
        Assert.Contains("card_rewards_affected", json);
        Assert.Contains("card_reward_categories", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[PrismaticGemRelicId];
        Assert.Equal(4, agg.EnergyGenerated);
        Assert.Equal(2, agg.CardRewardsAffected);
        Assert.Equal(3, agg.CardRewardCategories["defect"].Count);
        Assert.Equal("Defect", agg.CardRewardCategories["defect"].DisplayName);
        Assert.Equal(1, agg.CardRewardCategories["colorless"].Count);
        Assert.Equal("Colorless", agg.CardRewardCategories["colorless"].DisplayName);
    }

    [Fact]
    public void RelicTooltip_PrismaticGem_ShowsEnergyAndRewardTotals()
    {
        var agg = new RelicAggregate
        {
            EnergyGenerated = 4,
            CardRewardsAffected = 2,
            CardRewardCategories =
            {
                ["defect"] = new CardRewardCategoryAggregate { DisplayName = "Defect", Count = 3 },
                ["colorless"] = new CardRewardCategoryAggregate { DisplayName = "Colorless", Count = 1 },
            },
        };

        var body = (string)(BuildPrismaticGemBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildPrismaticGemBodyBBCode returned null."));

        Assert.Contains("Energy generated", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.Contains("Card rewards affected", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("Defect rewards", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("Colorless rewards", body);
        Assert.Contains("[b]1[/b]", body);
    }

    [Fact]
    public void RunData_OlderShapeWithoutCardRewardsAffected_DeserializesWithZeroDefault()
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
                "RELIC.PRISMATIC_GEM": {
                  "energy_generated": 3
                }
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        var agg = run!.RelicAggregates[PrismaticGemRelicId];
        Assert.Equal(3, agg.EnergyGenerated);
        Assert.Equal(0, agg.CardRewardsAffected);
        Assert.Empty(agg.CardRewardCategories);
    }
}
