using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

/// <summary>
/// Tests for Booming Conch relic stat data model and persistence. Booming Conch
/// reuses the existing <c>AdditionalCardsDrawn</c> aggregate (no schema change),
/// so these cover stat accumulation and run-data round-trip under the relic's
/// own id. Live RunTracker/Harmony integration is exercised by STS2 verification.
/// </summary>
public class BoomingConchStatsTests
{
    private const string BoomingConchRelicId = "RELIC.BOOMING_CONCH";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void BoomingConch_AdditionalCardsDrawn_JsonRoundtrip_PreservesField()
    {
        var agg = new RelicAggregate { AdditionalCardsDrawn = 2 };
        var run = new RunData();
        run.RelicAggregates[BoomingConchRelicId] = agg;

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("relic_aggregates", json);
        Assert.Contains("additional_cards_drawn", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);
        Assert.NotNull(restored);
        Assert.True(restored!.RelicAggregates.ContainsKey(BoomingConchRelicId));
        Assert.Equal(2, restored.RelicAggregates[BoomingConchRelicId].AdditionalCardsDrawn);
    }

    [Fact]
    public void BoomingConch_AdditionalCardsDrawn_AccumulatesAcrossElites()
    {
        var run = new RunData();

        if (!run.RelicAggregates.TryGetValue(BoomingConchRelicId, out var agg))
        {
            agg = new RelicAggregate();
            run.RelicAggregates[BoomingConchRelicId] = agg;
        }

        agg.AdditionalCardsDrawn += 2;
        agg.AdditionalCardsDrawn += 2;

        Assert.Equal(4, run.RelicAggregates[BoomingConchRelicId].AdditionalCardsDrawn);
    }
}
