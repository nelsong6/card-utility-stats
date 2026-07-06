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
        Assert.Null(agg.OriginalMaxHp);
        Assert.Null(agg.NewMaxHp);
    }

    [Fact]
    public void RelicAggregate_ChosenCheeseFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[ChosenCheeseRelicId] = new RelicAggregate
        {
            MaxHpGained = 3m,
            OriginalMaxHp = 70m,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("max_hp_gained", json);
        Assert.Contains("original_max_hp", json);
        Assert.DoesNotContain("new_max_hp", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[ChosenCheeseRelicId];
        Assert.Equal(3m, restoredAgg.MaxHpGained);
        Assert.Equal(70m, restoredAgg.OriginalMaxHp);
        Assert.Null(restoredAgg.NewMaxHp);
    }

    [Fact]
    public void RunTracker_RecordChosenCheeseStartingMaxHpForTest_SetsPickupBoundaryOnce()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordChosenCheeseStartingMaxHpForTest(agg, 70m);
        RunTracker.RecordChosenCheeseStartingMaxHpForTest(agg, 75m);
        RunTracker.RecordChosenCheeseStartingMaxHpForTest(agg, -1m);

        Assert.Equal(70m, agg.OriginalMaxHp);
        Assert.Null(agg.NewMaxHp);
    }

    [Fact]
    public void RunTracker_RecordChosenCheeseMaxHpGainedForTest_AccumulatesOnlyGain()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordChosenCheeseStartingMaxHpForTest(agg, 70m);
        RunTracker.RecordChosenCheeseMaxHpGainedForTest(agg, 1m);
        RunTracker.RecordChosenCheeseMaxHpGainedForTest(agg, 2m);
        RunTracker.RecordChosenCheeseMaxHpGainedForTest(agg, 0m);
        RunTracker.RecordChosenCheeseMaxHpGainedForTest(agg, -1m);

        Assert.Equal(3m, agg.MaxHpGained);
        Assert.Equal(70m, agg.OriginalMaxHp);
        Assert.Null(agg.NewMaxHp);
    }

    [Fact]
    public void RelicTooltip_ChosenCheese_ShowsStartingMaxHpAndMaxHpGained()
    {
        var agg = new RelicAggregate
        {
            MaxHpGained = 3m,
            OriginalMaxHp = 70m,
            NewMaxHp = 99m,
        };

        var body = InvokeTooltipBuilder(agg);

        Assert.Contains("Starting max HP", body);
        Assert.Contains("Max HP gained", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[b]70[/b]", body);
        Assert.DoesNotContain("Activations", body);
        Assert.DoesNotContain("Original max HP", body);
        Assert.DoesNotContain("New max HP", body);
        Assert.DoesNotContain("[b]99[/b]", body);
        Assert.DoesNotContain("HP healed", body);
    }

    [Fact]
    public void RelicTooltip_ChosenCheese_ShowsZeroRowsForEmptyAggregate()
    {
        var body = InvokeTooltipBuilder(new RelicAggregate());

        Assert.Contains("Starting max HP", body);
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
