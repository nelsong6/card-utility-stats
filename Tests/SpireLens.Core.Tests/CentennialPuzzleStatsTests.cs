using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class CentennialPuzzleStatsTests
{
    private const string CentennialPuzzleRelicId = "RELIC.CENTENNIAL_PUZZLE";

    private static readonly MethodInfo BuildCentennialPuzzleBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildCentennialPuzzleBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildCentennialPuzzleBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_CentennialPuzzleFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0, agg.AdditionalCardsDrawn);
    }

    [Fact]
    public void RelicAggregate_CentennialPuzzleFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[CentennialPuzzleRelicId] = new RelicAggregate
        {
            Activations = 4,
            AdditionalCardsDrawn = 11,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("additional_cards_drawn", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[CentennialPuzzleRelicId];
        Assert.Equal(4, agg.Activations);
        Assert.Equal(11, agg.AdditionalCardsDrawn);
    }

    [Fact]
    public void RunTracker_CentennialPuzzleTestHelper_AccumulatesAndClamps()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordCentennialPuzzleStatsForTest(agg, activations: 4, cardsDrawn: 11);
        RunTracker.RecordCentennialPuzzleStatsForTest(agg, activations: -1, cardsDrawn: -2);

        Assert.Equal(4, agg.Activations);
        Assert.Equal(11, agg.AdditionalCardsDrawn);
    }

    [Fact]
    public void RelicTooltip_CentennialPuzzle_ShowsActivationTotalAndAverageRows()
    {
        var body = BuildBody(new RelicAggregate
        {
            Activations = 4,
            AdditionalCardsDrawn = 11,
        });

        Assert.Contains("Activations", body);
        Assert.Contains("Cards drawn total", body);
        Assert.Contains("Avg cards drawn per combat", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.Contains("[b]11[/b]", body);
        Assert.Contains("[b]2.75[/b]", body);
    }

    [Fact]
    public void RelicTooltip_CentennialPuzzle_ShowsZeroAverageWithoutActivations()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Cards drawn total", body);
        Assert.Contains("Avg cards drawn per combat", body);
        Assert.Contains("[b]0[/b]", body);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildCentennialPuzzleBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildCentennialPuzzleBodyBBCode returned null."));
}
