using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class GremlinHornStatsTests
{
    private const string GremlinHornRelicId = "RELIC.GREMLIN_HORN";

    private static readonly MethodInfo BuildGremlinHornBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildGremlinHornBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildGremlinHornBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_GremlinHornFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0, agg.EnergyGenerated);
        Assert.Equal(0, agg.AdditionalCardsDrawn);
        Assert.Equal(0, agg.GremlinHornTurns);
        Assert.Equal(0, agg.GremlinHornCombats);
    }

    [Fact]
    public void RelicAggregate_GremlinHornFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[GremlinHornRelicId] = new RelicAggregate
        {
            Activations = 3,
            EnergyGenerated = 3,
            AdditionalCardsDrawn = 2,
            GremlinHornTurns = 5,
            GremlinHornCombats = 2,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("energy_generated", json);
        Assert.Contains("additional_cards_drawn", json);
        Assert.Contains("gremlin_horn_turns", json);
        Assert.Contains("gremlin_horn_combats", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[GremlinHornRelicId];
        Assert.Equal(3, agg.Activations);
        Assert.Equal(3, agg.EnergyGenerated);
        Assert.Equal(2, agg.AdditionalCardsDrawn);
        Assert.Equal(5, agg.GremlinHornTurns);
        Assert.Equal(2, agg.GremlinHornCombats);
    }

    [Fact]
    public void RelicTooltip_GremlinHorn_ShowsEnergyGainAndActivationRates()
    {
        var agg = new RelicAggregate
        {
            Activations = 6,
            EnergyGenerated = 5,
            AdditionalCardsDrawn = 4,
            GremlinHornTurns = 8,
            GremlinHornCombats = 3,
        };

        var body = (string)(BuildGremlinHornBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildGremlinHornBodyBBCode returned null."));

        Assert.Contains("Activations", body);
        Assert.Contains("Energy gained", body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("energy_gained"), body);
        Assert.DoesNotContain("Energy generated", body);
        Assert.Contains("Cards drawn", body);
        Assert.Contains("Average activations per turn", body);
        Assert.Contains("Average activations per combat", body);
        Assert.Contains("[b]0.75[/b]", body);
        Assert.Contains("[b]2[/b]", body);
    }

    [Fact]
    public void HeldPeriodDenominatorsAndMerge_AreZeroInclusiveAndAdditive()
    {
        var target = new RelicAggregate
        {
            GremlinHornTurns = 3,
            GremlinHornCombats = 1,
        };
        var source = new RelicAggregate
        {
            GremlinHornTurns = 5,
            GremlinHornCombats = 2,
        };

        RunTracker.RecordGremlinHornTurnForTest(target, 2);
        RunTracker.RecordGremlinHornCombatForTest(target);
        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(10, target.GremlinHornTurns);
        Assert.Equal(4, target.GremlinHornCombats);
    }

    [Fact]
    public void RunData_OlderShapeWithoutGremlinHornFields_DeserializesWithZeroDefaults()
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
                "RELIC.GREMLIN_HORN": {}
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        var agg = run!.RelicAggregates[GremlinHornRelicId];
        Assert.Equal(0, agg.Activations);
        Assert.Equal(0, agg.EnergyGenerated);
        Assert.Equal(0, agg.AdditionalCardsDrawn);
        Assert.Equal(0, agg.GremlinHornTurns);
        Assert.Equal(0, agg.GremlinHornCombats);
    }
}
