using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class IntimidatingHelmetStatsTests
{
    private const string IntimidatingHelmetRelicId = "RELIC.INTIMIDATING_HELMET";

    private static readonly MethodInfo BuildIntimidatingHelmetBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildIntimidatingHelmetBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildIntimidatingHelmetBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_IntimidatingHelmetFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0, agg.AdditionalBlockGained);
        Assert.Equal(0, agg.IntimidatingHelmetTurns);
        Assert.Equal(0, agg.IntimidatingHelmetCombats);
    }

    [Fact]
    public void RelicAggregate_IntimidatingHelmetFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[IntimidatingHelmetRelicId] = BuildPopulatedAggregate();

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("additional_block_gained", json);
        Assert.Contains("intimidating_helmet_turns", json);
        Assert.Contains("intimidating_helmet_combats", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        AssertPopulated(restored!.RelicAggregates[IntimidatingHelmetRelicId]);
    }

    [Fact]
    public void RunTracker_IntimidatingHelmetEnergyValueThreshold_IncludesTwoAndAbove()
    {
        Assert.False(RunTracker.IntimidatingHelmetEnergyValueQualifiesForTest(1));
        Assert.True(RunTracker.IntimidatingHelmetEnergyValueQualifiesForTest(2));
        Assert.True(RunTracker.IntimidatingHelmetEnergyValueQualifiesForTest(3));
    }

    [Fact]
    public void RunTracker_IntimidatingHelmetHelpers_AccumulateAndClamp()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordIntimidatingHelmetActivationForTest(agg, 6);
        RunTracker.RecordIntimidatingHelmetBlockGainedForTest(agg, 30m);
        RunTracker.RecordIntimidatingHelmetTurnForTest(agg, 8);
        RunTracker.RecordIntimidatingHelmetCombatForTest(agg, 3);
        RunTracker.RecordIntimidatingHelmetActivationForTest(agg, -1);
        RunTracker.RecordIntimidatingHelmetBlockGainedForTest(agg, -4m);
        RunTracker.RecordIntimidatingHelmetTurnForTest(agg, -2);
        RunTracker.RecordIntimidatingHelmetCombatForTest(agg, -3);

        AssertPopulated(agg);
    }

    [Fact]
    public void MergeRelicAggregateInto_IntimidatingHelmetFields_Accumulates()
    {
        var target = new RelicAggregate
        {
            Activations = 2,
            AdditionalBlockGained = 8,
            IntimidatingHelmetTurns = 3,
            IntimidatingHelmetCombats = 1,
        };
        var source = new RelicAggregate
        {
            Activations = 4,
            AdditionalBlockGained = 22,
            IntimidatingHelmetTurns = 5,
            IntimidatingHelmetCombats = 2,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        AssertPopulated(target);
    }

    [Fact]
    public void RelicTooltip_IntimidatingHelmet_ShowsTotalsAndAverages()
    {
        var body = BuildBody(BuildPopulatedAggregate());

        Assert.Contains("Cards played costing 2+", body);
        Assert.Contains("[hint=\"Block:", body);
        Assert.Contains("[hint=\"Average:", body);
        Assert.Contains("[hint=\"Turn:", body);
        Assert.Contains("[hint=\"Combat:", body);
        Assert.Contains("block gained", body);
        Assert.Contains("avg block per turn", body);
        Assert.Contains("avg block per combat", body);
        Assert.Contains("[b]6[/b]", body);
        Assert.Contains("[b]30[/b]", body);
        Assert.Contains("[b]3.75[/b]", body);
        Assert.Contains("[b]10[/b]", body);
    }

    [Fact]
    public void RelicTooltip_IntimidatingHelmet_ShowsZeroRows()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Cards played costing 2+", body);
        Assert.Contains("block gained", body);
        Assert.Contains("avg block per turn", body);
        Assert.Contains("avg block per combat", body);
        Assert.Equal(4, CountOccurrences(body, "[b]0[/b]"));
    }

    [Fact]
    public void RelicTooltip_IntimidatingHelmet_DispatchesForModel()
    {
        var relic = (IntimidatingHelmet)RuntimeHelpers.GetUninitializedObject(typeof(IntimidatingHelmet));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            BuildPopulatedAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Intimidating Helmet", title);
        Assert.Contains("Cards played costing 2+", body);
    }

    [Fact]
    public void RunData_OlderShapeWithoutIntimidatingHelmetDenominators_DefaultsToZero()
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
                "RELIC.INTIMIDATING_HELMET": {
                  "activations": 2,
                  "additional_block_gained": 8
                }
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        var agg = run!.RelicAggregates[IntimidatingHelmetRelicId];
        Assert.Equal(2, agg.Activations);
        Assert.Equal(8, agg.AdditionalBlockGained);
        Assert.Equal(0, agg.IntimidatingHelmetTurns);
        Assert.Equal(0, agg.IntimidatingHelmetCombats);
    }

    private static RelicAggregate BuildPopulatedAggregate()
        => new()
        {
            Activations = 6,
            AdditionalBlockGained = 30,
            IntimidatingHelmetTurns = 8,
            IntimidatingHelmetCombats = 3,
        };

    private static void AssertPopulated(RelicAggregate agg)
    {
        Assert.Equal(6, agg.Activations);
        Assert.Equal(30, agg.AdditionalBlockGained);
        Assert.Equal(8, agg.IntimidatingHelmetTurns);
        Assert.Equal(3, agg.IntimidatingHelmetCombats);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildIntimidatingHelmetBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildIntimidatingHelmetBodyBBCode returned null."));

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
