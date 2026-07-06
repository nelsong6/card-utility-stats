using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class PrecariousShearsStatsTests
{
    private const string PrecariousShearsRelicId = "RELIC.PRECARIOUS_SHEARS";

    private static readonly MethodInfo BuildPrecariousShearsBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildPrecariousShearsBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildPrecariousShearsBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_PrecariousShearsFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Empty(agg.CardsRemoved);
        Assert.Null(agg.OriginalMaxHp);
        Assert.Null(agg.NewMaxHp);
        Assert.Null(agg.StartingMaxHp);
        Assert.Null(agg.ResultingMaxHp);
    }

    [Fact]
    public void RelicAggregate_PrecariousShearsFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[PrecariousShearsRelicId] = new RelicAggregate
        {
            CardsRemoved = { "Strike", "Defend+" },
            OriginalMaxHp = 70m,
            NewMaxHp = 63m,
            StartingMaxHp = 70m,
            ResultingMaxHp = 63m,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("cards_removed", json);
        Assert.Contains("original_max_hp", json);
        Assert.Contains("new_max_hp", json);
        Assert.Contains("starting_max_hp", json);
        Assert.Contains("resulting_max_hp", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[PrecariousShearsRelicId];
        Assert.Equal(new[] { "Strike", "Defend+" }, agg.CardsRemoved);
        Assert.Equal(70m, agg.OriginalMaxHp);
        Assert.Equal(63m, agg.NewMaxHp);
        Assert.Equal(70m, agg.StartingMaxHp);
        Assert.Equal(63m, agg.ResultingMaxHp);
    }

    [Fact]
    public void RunTracker_PrecariousShearsTestHelper_RecordsCardsAndMaxHp()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordPrecariousShearsPickupForTest(
            agg,
            new[] { "Strike", "", "Defend+" },
            startingMaxHp: 70m,
            resultingMaxHp: 63m);

        Assert.Equal(new[] { "Strike", "Defend+" }, agg.CardsRemoved);
        Assert.Equal(70m, agg.OriginalMaxHp);
        Assert.Equal(63m, agg.NewMaxHp);
        Assert.Equal(70m, agg.StartingMaxHp);
        Assert.Equal(63m, agg.ResultingMaxHp);
    }

    [Fact]
    public void RelicTooltip_PrecariousShears_ShowsRemovedCardsAndMaxHp()
    {
        var body = BuildBody(new RelicAggregate
        {
            CardsRemoved = { "Strike", "Defend+" },
            StartingMaxHp = 70m,
            ResultingMaxHp = 63m,
        });

        Assert.Contains("Cards removed", body);
        Assert.Contains("Removed card", body);
        Assert.Contains("Strike", body);
        Assert.Contains("Defend+", body);
        Assert.Contains("Original max HP", body);
        Assert.Contains("New max HP", body);
        Assert.Contains("Max HP lost", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]70[/b]", body);
        Assert.Contains("[b]63[/b]", body);
        Assert.Contains("[b]7[/b]", body);
    }

    [Fact]
    public void RelicTooltip_PrecariousShears_ShowsZeroRowsWithoutStats()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Cards removed", body);
        Assert.Contains("Original max HP", body);
        Assert.Contains("New max HP", body);
        Assert.Contains("Max HP lost", body);
        Assert.Contains("[b]0[/b]", body);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildPrecariousShearsBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildPrecariousShearsBodyBBCode returned null."));
}
