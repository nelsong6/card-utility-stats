using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class ChosenCheeseStatsTests
{
    private const string ChosenCheeseRelicId = "RELIC.CHOSEN_CHEESE";

    private static readonly MethodInfo BuildChosenCheeseBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildChosenCheeseBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildChosenCheeseBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_ChosenCheeseFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.MaxHpGained);
    }

    [Fact]
    public void RelicAggregate_ChosenCheeseFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[ChosenCheeseRelicId] = new RelicAggregate
        {
            Activations = 3,
            MaxHpGained = 3m,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("max_hp_gained", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[ChosenCheeseRelicId];
        Assert.Equal(3, restoredAgg.Activations);
        Assert.Equal(3m, restoredAgg.MaxHpGained);
    }

    [Fact]
    public void RunTracker_RecordChosenCheeseMaxHpGainedForTest_AccumulatesObservedMaxHp()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordChosenCheeseMaxHpGainedForTest(agg, 1m);
        RunTracker.RecordChosenCheeseMaxHpGainedForTest(agg, 2m);
        RunTracker.RecordChosenCheeseMaxHpGainedForTest(agg, 0m);
        RunTracker.RecordChosenCheeseMaxHpGainedForTest(agg, -1m);

        Assert.Equal(3, agg.Activations);
        Assert.Equal(3m, agg.MaxHpGained);
    }

    [Fact]
    public void RelicTooltip_ChosenCheese_ShowsActivationsAndMaxHpGained()
    {
        var agg = new RelicAggregate
        {
            Activations = 3,
            MaxHpGained = 3m,
        };

        var body = InvokeTooltipBuilder(agg);

        Assert.Contains("Activations", body);
        Assert.Contains("Max HP gained", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.DoesNotContain("HP healed", body);
    }

    [Fact]
    public void RelicTooltip_ChosenCheese_ShowsZeroRowsForEmptyAggregate()
    {
        var body = InvokeTooltipBuilder(new RelicAggregate());

        Assert.Contains("Activations", body);
        Assert.Contains("Max HP gained", body);
        Assert.Equal(2, CountOccurrences(body, "[b]0[/b]"));
    }

    [Fact]
    public void RunData_OlderShapeWithoutChosenCheeseMaxHp_DeserializesWithZeroDefault()
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
                "RELIC.CHOSEN_CHEESE": {
                  "activations": 0
                }
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        var agg = run!.RelicAggregates[ChosenCheeseRelicId];
        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.MaxHpGained);
    }

    private static string InvokeTooltipBuilder(RelicAggregate agg)
    {
        return (string)(BuildChosenCheeseBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildChosenCheeseBodyBBCode returned null."));
    }

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
