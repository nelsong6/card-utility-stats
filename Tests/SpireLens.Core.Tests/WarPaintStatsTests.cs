using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class WarPaintStatsTests
{
    private const string WarPaintRelicId = "RELIC.WAR_PAINT";

    private static readonly MethodInfo BuildWarPaintBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildWarPaintBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildWarPaintBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_WarPaintFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.CardsUpgraded);
        Assert.Empty(agg.UpgradedCards);
    }

    [Fact]
    public void RelicAggregate_WarPaintFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[WarPaintRelicId] = new RelicAggregate
        {
            CardsUpgraded = 2,
            UpgradedCards = { "Defend+", "Battle Trance+" },
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("cards_upgraded", json);
        Assert.Contains("upgraded_cards", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[WarPaintRelicId];
        Assert.Equal(2, agg.CardsUpgraded);
        Assert.Equal(new[] { "Defend+", "Battle Trance+" }, agg.UpgradedCards);
    }

    [Fact]
    public void RunTracker_WarPaintTestHelper_RecordsCardsAndCount()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordWarPaintUpgradesForTest(agg, new[] { "Defend+", "", "Battle Trance+" });

        Assert.Equal(2, agg.CardsUpgraded);
        Assert.Equal(new[] { "Defend+", "Battle Trance+" }, agg.UpgradedCards);
    }

    [Fact]
    public void RelicTooltip_WarPaint_ShowsCardsUpgradedAndCardList()
    {
        var body = BuildBody(new RelicAggregate
        {
            CardsUpgraded = 2,
            UpgradedCards = { "Defend+", "Battle Trance+" },
        });

        Assert.Contains("Cards upgraded", body);
        Assert.Contains("Upgraded card", body);
        Assert.Contains("Defend+", body);
        Assert.Contains("Battle Trance+", body);
        Assert.Contains("[b]2[/b]", body);
    }

    [Fact]
    public void RelicTooltip_WarPaint_ShowsZeroRowsWithoutStats()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Cards upgraded", body);
        Assert.Contains("[b]0[/b]", body);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildWarPaintBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildWarPaintBodyBBCode returned null."));
}
