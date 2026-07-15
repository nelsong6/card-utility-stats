using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class NutritiousOysterStatsTests
{
    private const string NutritiousOysterRelicId = "RELIC.NUTRITIOUS_OYSTER";

    private static readonly MethodInfo BuildNutritiousOysterBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildNutritiousOysterBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildNutritiousOysterBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_NutritiousOysterFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.MaxHpGained);
        Assert.Null(agg.OriginalMaxHp);
        Assert.Null(agg.NewMaxHp);
    }

    [Fact]
    public void RelicAggregate_NutritiousOysterFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[NutritiousOysterRelicId] = new RelicAggregate
        {
            Activations = 1,
            MaxHpGained = 11m,
            OriginalMaxHp = 70m,
            NewMaxHp = 81m,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("max_hp_gained", json);
        Assert.Contains("original_max_hp", json);
        Assert.Contains("new_max_hp", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[NutritiousOysterRelicId];
        Assert.Equal(1, restoredAgg.Activations);
        Assert.Equal(11m, restoredAgg.MaxHpGained);
        Assert.Equal(70m, restoredAgg.OriginalMaxHp);
        Assert.Equal(81m, restoredAgg.NewMaxHp);
    }

    [Fact]
    public void RunTracker_RecordNutritiousOysterMaxHpGainedForTest_AccumulatesObservedMaxHp()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordNutritiousOysterMaxHpGainedForTest(agg, 11m, 70m, 81m);
        RunTracker.RecordNutritiousOysterMaxHpGainedForTest(agg, 0m, 81m, 81m);
        RunTracker.RecordNutritiousOysterMaxHpGainedForTest(agg, -1m);

        Assert.Equal(2, agg.Activations);
        Assert.Equal(11m, agg.MaxHpGained);
        Assert.Equal(70m, agg.OriginalMaxHp);
        Assert.Equal(81m, agg.NewMaxHp);
    }

    [Fact]
    public void RelicTooltip_NutritiousOyster_ShowsActivationsAndMaxHpGained()
    {
        var body = InvokeTooltipBuilder(new RelicAggregate
        {
            Activations = 1,
            MaxHpGained = 11m,
            OriginalMaxHp = 70m,
            NewMaxHp = 81m,
        });

        Assert.Contains("Activations", body);
        Assert.Contains("Original max HP", body);
        Assert.Contains("New max HP", body);
        Assert.Contains("Max HP gained", body);
        Assert.Contains("[b]1[/b]", body);
        Assert.Contains("[b]70[/b]", body);
        Assert.Contains("[b]81[/b]", body);
        Assert.Contains("[b]11[/b]", body);
        Assert.DoesNotContain("HP healed", body);
    }

    [Fact]
    public void RelicTooltip_NutritiousOyster_ShowsZeroRowsForEmptyAggregate()
    {
        var body = InvokeTooltipBuilder(new RelicAggregate());

        Assert.Contains("Activations", body);
        Assert.Contains("Original max HP", body);
        Assert.Contains("New max HP", body);
        Assert.Contains("Max HP gained", body);
        Assert.Equal(4, CountOccurrences(body, "[b]0[/b]"));
    }

    [Fact]
    public void RelicTooltip_NutritiousOyster_DispatchesForModel()
    {
        var relic = (NutritiousOyster)RuntimeHelpers.GetUninitializedObject(typeof(NutritiousOyster));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate
            {
                Activations = 1,
                MaxHpGained = 11m,
                OriginalMaxHp = 70m,
                NewMaxHp = 81m,
            },
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Nutritious Oyster", title);
        Assert.Contains("Max HP gained", body);
        Assert.Contains("[b]11[/b]", body);
    }

    private static string InvokeTooltipBuilder(RelicAggregate agg)
    {
        return (string)(BuildNutritiousOysterBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildNutritiousOysterBodyBBCode returned null."));
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
