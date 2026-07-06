using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class FragrantMushroomStatsTests
{
    private const string FragrantMushroomRelicId = "RELIC.FRAGRANT_MUSHROOM";

    private static readonly MethodInfo BuildFragrantMushroomBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildFragrantMushroomBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildFragrantMushroomBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_FragrantMushroomFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.CardsUpgraded);
        Assert.Empty(agg.UpgradedCards);
    }

    [Fact]
    public void RelicAggregate_FragrantMushroomFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[FragrantMushroomRelicId] = new RelicAggregate
        {
            CardsUpgraded = 2,
            UpgradedCards = { "Strike+", "Defend+" },
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("cards_upgraded", json);
        Assert.Contains("upgraded_cards", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[FragrantMushroomRelicId];
        Assert.Equal(2, agg.CardsUpgraded);
        Assert.Equal(new[] { "Strike+", "Defend+" }, agg.UpgradedCards);
    }

    [Fact]
    public void RunTracker_FragrantMushroomTestHelper_RecordsCardsAndCount()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordFragrantMushroomUpgradesForTest(agg, new[] { "Strike+", "", "Defend+" });

        Assert.Equal(2, agg.CardsUpgraded);
        Assert.Equal(new[] { "Strike+", "Defend+" }, agg.UpgradedCards);
    }

    [Fact]
    public void RelicTooltip_FragrantMushroom_ShowsCardsUpgradedAndCardList()
    {
        var body = BuildBody(new RelicAggregate
        {
            CardsUpgraded = 2,
            UpgradedCards = { "Strike+", "Defend+" },
        });

        Assert.Contains("Cards upgraded", body);
        Assert.Contains("Upgraded card", body);
        Assert.Contains("Strike+", body);
        Assert.Contains("Defend+", body);
        Assert.Contains("[b]2[/b]", body);
    }

    [Fact]
    public void RelicTooltip_FragrantMushroom_ShowsZeroRowsWithoutStats()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Cards upgraded", body);
        Assert.Contains("[b]0[/b]", body);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildFragrantMushroomBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildFragrantMushroomBodyBBCode returned null."));
}
