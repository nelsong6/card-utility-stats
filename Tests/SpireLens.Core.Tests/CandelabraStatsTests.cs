using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class CandelabraStatsTests
{
    private const string CandelabraRelicId = "RELIC.CANDELABRA";

    private static readonly MethodInfo BuildCandelabraBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildCandelabraBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildCandelabraBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_CandelabraFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0, agg.EnergyGenerated);
        Assert.Equal(0, agg.SecondTurnsEndedWithExcessEnergy);
    }

    [Fact]
    public void RelicAggregate_CandelabraFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[CandelabraRelicId] = new RelicAggregate
        {
            Activations = 4,
            EnergyGenerated = 8,
            SecondTurnsEndedWithExcessEnergy = 2,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("energy_generated", json);
        Assert.Contains("second_turns_ended_with_excess_energy", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[CandelabraRelicId];
        Assert.Equal(4, agg.Activations);
        Assert.Equal(8, agg.EnergyGenerated);
        Assert.Equal(2, agg.SecondTurnsEndedWithExcessEnergy);
    }

    [Fact]
    public void MergeRelicAggregateInto_CandelabraFields_Accumulates()
    {
        var target = new RelicAggregate
        {
            Activations = 1,
            EnergyGenerated = 2,
            SecondTurnsEndedWithExcessEnergy = 1,
        };
        var source = new RelicAggregate
        {
            Activations = 3,
            EnergyGenerated = 6,
            SecondTurnsEndedWithExcessEnergy = 2,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(4, target.Activations);
        Assert.Equal(8, target.EnergyGenerated);
        Assert.Equal(3, target.SecondTurnsEndedWithExcessEnergy);
    }

    [Fact]
    public void RelicTooltip_Candelabra_ShowsRequestedRowsAndZeroValues()
    {
        var body = (string)(BuildCandelabraBodyMethod.Invoke(null, new object?[] { new RelicAggregate() })
            ?? throw new InvalidOperationException("BuildCandelabraBodyBBCode returned null."));

        Assert.Contains("Activations", body);
        Assert.Contains("2nd turns ended with excess energy", body);
        Assert.Contains("2nd turns ended with excess energy[/color]  [b]0[/b]", body);
        Assert.Equal(2, CountOccurrences(body, "[b]0[/b]"));
    }

    [Fact]
    public void RelicTooltip_Candelabra_ShowsTrackedCounts()
    {
        var agg = new RelicAggregate
        {
            Activations = 4,
            SecondTurnsEndedWithExcessEnergy = 2,
        };

        var body = (string)(BuildCandelabraBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildCandelabraBodyBBCode returned null."));

        Assert.Contains("Activations", body);
        Assert.Contains("2nd turns ended with excess energy", body);
        Assert.Contains("2nd turns ended with excess energy[/color]  [b]2[/b]", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.Contains("[b]2[/b]", body);
    }

    [Fact]
    public void RunData_OlderShapeWithoutCandelabraFields_DeserializesWithZeroDefaults()
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
                "RELIC.CANDELABRA": {}
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        var agg = run!.RelicAggregates[CandelabraRelicId];
        Assert.Equal(0, agg.Activations);
        Assert.Equal(0, agg.EnergyGenerated);
        Assert.Equal(0, agg.SecondTurnsEndedWithExcessEnergy);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var start = 0;
        while (true)
        {
            var index = haystack.IndexOf(needle, start, StringComparison.Ordinal);
            if (index < 0) return count;
            count += 1;
            start = index + needle.Length;
        }
    }
}
