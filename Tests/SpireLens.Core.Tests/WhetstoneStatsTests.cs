using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class WhetstoneStatsTests
{
    private const string WhetstoneRelicId = "RELIC.WHETSTONE";

    private static readonly MethodInfo BuildWhetstoneBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildWhetstoneBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildWhetstoneBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_WhetstoneFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.CardsUpgraded);
        Assert.Empty(agg.UpgradedCards);
    }

    [Fact]
    public void RelicAggregate_WhetstoneFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[WhetstoneRelicId] = new RelicAggregate
        {
            CardsUpgraded = 2,
            UpgradedCards = { "Strike+", "Pommel Strike+" },
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("cards_upgraded", json);
        Assert.Contains("upgraded_cards", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[WhetstoneRelicId];
        Assert.Equal(2, agg.CardsUpgraded);
        Assert.Equal(new[] { "Strike+", "Pommel Strike+" }, agg.UpgradedCards);
    }

    [Fact]
    public void RunTracker_WhetstoneTestHelper_RecordsCardsAndCount()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordWhetstoneUpgradesForTest(agg, new[] { "Strike+", "", "Pommel Strike+" });

        Assert.Equal(2, agg.CardsUpgraded);
        Assert.Equal(new[] { "Strike+", "Pommel Strike+" }, agg.UpgradedCards);
    }

    [Fact]
    public void RelicTooltip_Whetstone_UsesAttackConceptForCountAndList()
    {
        var body = BuildBody(new RelicAggregate
        {
            CardsUpgraded = 2,
            UpgradedCards = { "Strike+", "Pommel Strike+" },
        });

        Assert.Contains("Attacks upgraded", body);
        Assert.Contains("Upgraded attack", body);
        Assert.DoesNotContain("Cards upgraded", body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("attack"), body);
        Assert.DoesNotContain(StatConceptGlossary.RenderHintedGlyph("card"), body);
        Assert.Contains("Strike+", body);
        Assert.Contains("Pommel Strike+", body);
        Assert.Contains("[b]2[/b]", body);
    }

    [Fact]
    public void RelicTooltip_Whetstone_ShowsZeroRowsWithoutStats()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Attacks upgraded", body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("attack"), body);
        Assert.DoesNotContain(StatConceptGlossary.RenderHintedGlyph("card"), body);
        Assert.Contains("[b]0[/b]", body);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildWhetstoneBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildWhetstoneBodyBBCode returned null."));
}
