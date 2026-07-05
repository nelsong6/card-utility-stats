using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class GamblingChipStatsTests
{
    private const string GamblingChipRelicId = "RELIC.GAMBLING_CHIP";

    private static readonly MethodInfo BuildGamblingChipBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildGamblingChipBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildGamblingChipBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_GamblingChipFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0, agg.CardsDiscarded);
    }

    [Fact]
    public void RelicAggregate_GamblingChipFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[GamblingChipRelicId] = new RelicAggregate
        {
            Activations = 3,
            CardsDiscarded = 7,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("cards_discarded", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[GamblingChipRelicId];
        Assert.Equal(3, restoredAgg.Activations);
        Assert.Equal(7, restoredAgg.CardsDiscarded);
    }

    [Fact]
    public void RunTracker_GamblingChipTestHelper_AccumulatesAndClamps()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordGamblingChipDiscardForTest(agg, activations: 3, cardsDiscarded: 7);
        RunTracker.RecordGamblingChipDiscardForTest(agg, activations: -1, cardsDiscarded: -2);

        Assert.Equal(3, agg.Activations);
        Assert.Equal(7, agg.CardsDiscarded);
    }

    [Fact]
    public void RelicTooltip_GamblingChip_ShowsDiscardRowsAndAverage()
    {
        var body = BuildBody(new RelicAggregate
        {
            Activations = 3,
            CardsDiscarded = 7,
        });

        Assert.Contains("Cards discarded", body);
        Assert.Contains("Avg discarded per combat", body);
        Assert.Contains("[b]7[/b]", body);
        Assert.Contains("[b]2.33[/b]", body);
    }

    [Fact]
    public void RelicTooltip_GamblingChip_ShowsZeroAverageWithoutActivations()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Cards discarded", body);
        Assert.Contains("Avg discarded per combat", body);
        Assert.Contains("[b]0[/b]", body);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildGamblingChipBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildGamblingChipBodyBBCode returned null."));
}
