using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class RippleBasinStatsTests
{
    private const string RippleBasinRelicId = "RELIC.RIPPLE_BASIN";

    private static readonly MethodInfo BuildRippleBasinBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildRippleBasinBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildRippleBasinBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_RippleBasinFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0, agg.AdditionalBlockGained);
        Assert.Equal(0, agg.RippleBasinTurns);
        Assert.Equal(0, agg.RippleBasinCombats);
    }

    [Fact]
    public void RelicAggregate_RippleBasinFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[RippleBasinRelicId] = new RelicAggregate
        {
            Activations = 3,
            AdditionalBlockGained = 12,
            RippleBasinTurns = 6,
            RippleBasinCombats = 2,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("additional_block_gained", json);
        Assert.Contains("ripple_basin_turns", json);
        Assert.Contains("ripple_basin_combats", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[RippleBasinRelicId];
        Assert.Equal(3, agg.Activations);
        Assert.Equal(12, agg.AdditionalBlockGained);
        Assert.Equal(6, agg.RippleBasinTurns);
        Assert.Equal(2, agg.RippleBasinCombats);
    }

    [Fact]
    public void MergeRelicAggregateInto_RippleBasinFields_Accumulates()
    {
        var target = new RelicAggregate
        {
            Activations = 1,
            AdditionalBlockGained = 4,
            RippleBasinTurns = 2,
            RippleBasinCombats = 1,
        };
        var source = new RelicAggregate
        {
            Activations = 2,
            AdditionalBlockGained = 8,
            RippleBasinTurns = 4,
            RippleBasinCombats = 1,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(3, target.Activations);
        Assert.Equal(12, target.AdditionalBlockGained);
        Assert.Equal(6, target.RippleBasinTurns);
        Assert.Equal(2, target.RippleBasinCombats);
    }

    [Fact]
    public void RecordRippleBasinDenominators_IncludeHeldTurnsAndCombats()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordRippleBasinTurnForTest(agg, 6);
        RunTracker.RecordRippleBasinCombatForTest(agg, 2);
        RunTracker.RecordRippleBasinTurnForTest(agg, -1);
        RunTracker.RecordRippleBasinCombatForTest(agg, -1);

        Assert.Equal(6, agg.RippleBasinTurns);
        Assert.Equal(2, agg.RippleBasinCombats);
    }

    [Fact]
    public void RelicTooltip_RippleBasin_ShowsActivationBlockAndAverageRows()
    {
        var body = BuildBody(new RelicAggregate
        {
            Activations = 3,
            AdditionalBlockGained = 12,
            RippleBasinTurns = 6,
            RippleBasinCombats = 2,
        });

        Assert.Contains("Activations", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[hint=\"Block gained:", body);
        Assert.Contains("block gained", body);
        Assert.Contains("[b]12[/b]", body);
        Assert.Contains("[hint=\"Activation:", body);
        Assert.Contains("block gained per activation", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.Contains("[hint=\"Average:", body);
        Assert.Contains("[hint=\"Turn:", body);
        Assert.Contains("avg block gained per turn", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[hint=\"Combat:", body);
        Assert.Contains("avg block gained per combat", body);
        Assert.Contains("[b]6[/b]", body);
    }

    [Fact]
    public void RelicTooltip_RippleBasin_ShowsZeroRows()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Activations", body);
        Assert.Contains("block gained", body);
        Assert.Contains("block gained per activation", body);
        Assert.Contains("avg block gained per turn", body);
        Assert.Contains("avg block gained per combat", body);
        Assert.Equal(5, CountOccurrences(body, "[b]0[/b]"));
    }

    [Fact]
    public void RunData_OlderShapeWithoutRippleBasinFields_DeserializesWithZeroDefaults()
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
                "RELIC.RIPPLE_BASIN": {}
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        var agg = run!.RelicAggregates[RippleBasinRelicId];
        Assert.Equal(0, agg.Activations);
        Assert.Equal(0, agg.AdditionalBlockGained);
        Assert.Equal(0, agg.RippleBasinTurns);
        Assert.Equal(0, agg.RippleBasinCombats);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildRippleBasinBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildRippleBasinBodyBBCode returned null."));

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
