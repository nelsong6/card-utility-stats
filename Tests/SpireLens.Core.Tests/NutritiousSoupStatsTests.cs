using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class NutritiousSoupStatsTests
{
    private const string NutritiousSoupRelicId = "RELIC.NUTRITIOUS_SOUP";

    private static readonly MethodInfo BuildNutritiousSoupBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildNutritiousSoupBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildNutritiousSoupBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_NutritiousSoupFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.NutritiousSoupEnchantedStrikesPlayed);
    }

    [Fact]
    public void RelicAggregate_NutritiousSoupFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[NutritiousSoupRelicId] = new RelicAggregate
        {
            Activations = 2,
            NutritiousSoupEnchantedStrikesPlayed = 7,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("nutritious_soup_enchanted_strikes_played", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[NutritiousSoupRelicId];
        Assert.Equal(2, restoredAgg.Activations);
        Assert.Equal(7, restoredAgg.NutritiousSoupEnchantedStrikesPlayed);
    }

    [Fact]
    public void RunTracker_NutritiousSoupHelper_AccumulatesAndClamps()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordNutritiousSoupEnchantedStrikePlayedForTest(agg);
        RunTracker.RecordNutritiousSoupEnchantedStrikePlayedForTest(agg, 3);
        RunTracker.RecordNutritiousSoupEnchantedStrikePlayedForTest(agg, -2);

        Assert.Equal(4, agg.NutritiousSoupEnchantedStrikesPlayed);
    }

    [Fact]
    public void RelicTooltip_NutritiousSoup_ShowsEnchantedStrikePlaysIncludingZero()
    {
        var emptyBody = BuildBody(new RelicAggregate());

        Assert.Contains("Combats held", emptyBody);
        Assert.Contains("Enchanted Strikes played", emptyBody);
        Assert.Contains("Avg Enchanted Strikes/combat", emptyBody);
        Assert.Contains("[b]0[/b]", emptyBody);

        var body = BuildBody(new RelicAggregate
        {
            Activations = 2,
            NutritiousSoupEnchantedStrikesPlayed = 5,
        });

        Assert.Contains("Combats held", body);
        Assert.Contains("Enchanted Strikes played", body);
        Assert.Contains("Avg Enchanted Strikes/combat", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]5[/b]", body);
        Assert.Contains("[b]2.5[/b]", body);
    }

    private static string BuildBody(RelicAggregate agg)
    {
        return (string)(BuildNutritiousSoupBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildNutritiousSoupBodyBBCode returned null."));
    }
}
