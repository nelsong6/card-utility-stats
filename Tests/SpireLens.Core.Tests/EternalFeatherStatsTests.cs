using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class EternalFeatherStatsTests
{
    private const string EternalFeatherRelicId = "RELIC.ETERNAL_FEATHER";

    private static readonly MethodInfo BuildEternalFeatherBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildEternalFeatherBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildEternalFeatherBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_EternalFeatherFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.TotalHealingAttempted);
        Assert.Equal(0m, agg.TotalHealingRestored);
        Assert.Equal(0m, agg.TotalHealingLost);
        Assert.Empty(agg.HealingLostReasons);
        Assert.Empty(agg.EternalFeatherHealingActivations);
        Assert.Equal(0, agg.EternalFeatherDeckCardsTotal);
        Assert.Equal(0, agg.EternalFeatherDeckCardsSamples);
    }

    [Fact]
    public void RelicAggregate_EternalFeatherFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        var agg = new RelicAggregate
        {
            Activations = 2,
            TotalHealingAttempted = 18m,
            TotalHealingRestored = 11m,
            TotalHealingLost = 7m,
            EternalFeatherDeckCardsTotal = 41,
            EternalFeatherDeckCardsSamples = 2,
        };
        agg.HealingLostReasons["full_hp"] = new HealingLostReasonAggregate
        {
            ReasonId = "full_hp",
            DisplayName = "full HP",
            Amount = 7m,
        };
        agg.EternalFeatherHealingActivations.Add(
            new EternalFeatherHealingActivationAggregate
            {
                Floor = 7,
                HpRestored = 11m,
            });
        run.RelicAggregates[EternalFeatherRelicId] = agg;

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("total_healing_attempted", json);
        Assert.Contains("healing_lost_reasons", json);
        Assert.Contains("eternal_feather_deck_cards_total", json);
        Assert.Contains("eternal_feather_deck_cards_samples", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[EternalFeatherRelicId];
        Assert.Equal(2, restoredAgg.Activations);
        Assert.Equal(18m, restoredAgg.TotalHealingAttempted);
        Assert.Equal(11m, restoredAgg.TotalHealingRestored);
        Assert.Equal(7m, restoredAgg.TotalHealingLost);
        Assert.Equal(7m, restoredAgg.HealingLostReasons["full_hp"].Amount);
        var activation = Assert.Single(restoredAgg.EternalFeatherHealingActivations);
        Assert.Equal(7, activation.Floor);
        Assert.Equal(11m, activation.HpRestored);
        Assert.Equal(41, restoredAgg.EternalFeatherDeckCardsTotal);
        Assert.Equal(2, restoredAgg.EternalFeatherDeckCardsSamples);
    }

    [Fact]
    public void MergeRelicAggregateInto_EternalFeatherDeckCards_Accumulates()
    {
        var target = new RelicAggregate
        {
            Activations = 1,
            EternalFeatherDeckCardsTotal = 21,
            EternalFeatherDeckCardsSamples = 1,
        };
        var source = new RelicAggregate
        {
            Activations = 2,
            EternalFeatherDeckCardsTotal = 45,
            EternalFeatherDeckCardsSamples = 2,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(3, target.Activations);
        Assert.Equal(66, target.EternalFeatherDeckCardsTotal);
        Assert.Equal(3, target.EternalFeatherDeckCardsSamples);
    }

    [Fact]
    public void RelicTooltip_EternalFeather_ShowsActivationsAndHealing()
    {
        var agg = new RelicAggregate
        {
            Activations = 2,
            TotalHealingRestored = 11m,
            TotalHealingLost = 7m,
        };
        agg.HealingLostReasons["full_hp"] = new HealingLostReasonAggregate
        {
            ReasonId = "full_hp",
            DisplayName = "full HP",
            Amount = 7m,
        };

        var body = (string)(BuildEternalFeatherBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildEternalFeatherBodyBBCode returned null."));

        Assert.Contains("Activations", body);
        Assert.Contains("HP healed", body);
        Assert.Contains("healing lost", body);
        Assert.DoesNotContain("lost to full HP", body);
        Assert.Contains("[b]11[/b]", body);
        Assert.Contains("[b]7[/b]", body);
    }

    [Fact]
    public void RelicTooltip_EternalFeather_ShowsAverageHealPerActivation()
    {
        var agg = new RelicAggregate
        {
            Activations = 2,
            TotalHealingRestored = 11m,
        };

        var body = (string)(BuildEternalFeatherBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildEternalFeatherBodyBBCode returned null."));

        Assert.Contains("Average HP actually restored per activation", body);
        Assert.Contains("[b]5.5[/b]", body);
    }

    [Fact]
    public void RelicTooltip_EternalFeather_ShowsAverageDeckCardsPerRestSite()
    {
        var agg = new RelicAggregate
        {
            Activations = 3,
            EternalFeatherDeckCardsTotal = 62,
            EternalFeatherDeckCardsSamples = 3,
        };

        var body = (string)(BuildEternalFeatherBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildEternalFeatherBodyBBCode returned null."));

        Assert.Contains("in deck", body);
        Assert.Contains("Average deck size observed when Eternal Feather triggered", body);
        Assert.Contains("[b]20.67[/b]", body);
    }

    [Fact]
    public void RelicTooltip_EternalFeather_ShowsZeroHealingRows()
    {
        var body = (string)(BuildEternalFeatherBodyMethod.Invoke(null, new object?[] { new RelicAggregate() })
            ?? throw new InvalidOperationException("BuildEternalFeatherBodyBBCode returned null."));

        Assert.Contains("Activations", body);
        Assert.Contains("HP healed", body);
        Assert.Contains("healing lost", body);
        Assert.Contains("Average HP actually restored per activation", body);
        Assert.Contains("Average deck size observed when Eternal Feather triggered", body);
        Assert.Contains("[b]0[/b]", body);
    }

    [Fact]
    public void RunData_OlderShapeWithoutEternalFeatherFields_DeserializesWithZeroDefaults()
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
                "RELIC.ETERNAL_FEATHER": {}
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        var agg = run!.RelicAggregates[EternalFeatherRelicId];
        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.TotalHealingAttempted);
        Assert.Equal(0m, agg.TotalHealingRestored);
        Assert.Equal(0m, agg.TotalHealingLost);
        Assert.Empty(agg.HealingLostReasons);
        Assert.Empty(agg.EternalFeatherHealingActivations);
        Assert.Equal(0, agg.EternalFeatherDeckCardsTotal);
        Assert.Equal(0, agg.EternalFeatherDeckCardsSamples);
    }
}
