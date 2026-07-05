using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class SandCastleStatsTests
{
    private const string SandCastleRelicId = "RELIC.SAND_CASTLE";

    private static readonly MethodInfo BuildSandCastleBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildSandCastleBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildSandCastleBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_SandCastleFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.CardsUpgraded);
        Assert.Empty(agg.UpgradedCards);
    }

    [Fact]
    public void RelicAggregate_SandCastleFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[SandCastleRelicId] = new RelicAggregate
        {
            CardsUpgraded = 2,
            UpgradedCards = { "Strike+", "Defend+" },
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("cards_upgraded", json);
        Assert.Contains("upgraded_cards", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[SandCastleRelicId];
        Assert.Equal(2, agg.CardsUpgraded);
        Assert.Equal(new[] { "Strike+", "Defend+" }, agg.UpgradedCards);
    }

    [Fact]
    public void RunTracker_SandCastleTestHelper_RecordsCardsAndCount()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordSandCastleUpgradesForTest(agg, new[] { "Strike+", "", "Defend+" });

        Assert.Equal(2, agg.CardsUpgraded);
        Assert.Equal(new[] { "Strike+", "Defend+" }, agg.UpgradedCards);
    }

    [Fact]
    public void RelicTooltip_SandCastle_ShowsCardsUpgradedAndCardList()
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
    public void RelicTooltip_SandCastle_ShowsZeroRowsWithoutStats()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Cards upgraded", body);
        Assert.Contains("[b]0[/b]", body);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildSandCastleBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildSandCastleBodyBBCode returned null."));
}
