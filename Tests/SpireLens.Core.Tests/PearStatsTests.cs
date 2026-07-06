using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class PearStatsTests
{
    private const string PearRelicId = "RELIC.PEAR";

    private static readonly MethodInfo BuildPearBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildPearBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildPearBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_PearFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.MaxHpGained);
        Assert.Null(agg.OriginalMaxHp);
        Assert.Null(agg.NewMaxHp);
    }

    [Fact]
    public void RelicAggregate_PearFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[PearRelicId] = new RelicAggregate
        {
            Activations = 1,
            MaxHpGained = 10m,
            OriginalMaxHp = 70m,
            NewMaxHp = 80m,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("max_hp_gained", json);
        Assert.Contains("original_max_hp", json);
        Assert.Contains("new_max_hp", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[PearRelicId];
        Assert.Equal(1, restoredAgg.Activations);
        Assert.Equal(10m, restoredAgg.MaxHpGained);
        Assert.Equal(70m, restoredAgg.OriginalMaxHp);
        Assert.Equal(80m, restoredAgg.NewMaxHp);
    }

    [Fact]
    public void RunTracker_RecordPearMaxHpGainedForTest_AccumulatesObservedMaxHp()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordPearMaxHpGainedForTest(agg, 10m, 70m, 80m);
        RunTracker.RecordPearMaxHpGainedForTest(agg, 0m, 80m, 80m);
        RunTracker.RecordPearMaxHpGainedForTest(agg, -1m);

        Assert.Equal(2, agg.Activations);
        Assert.Equal(10m, agg.MaxHpGained);
        Assert.Equal(70m, agg.OriginalMaxHp);
        Assert.Equal(80m, agg.NewMaxHp);
    }

    [Fact]
    public void RelicTooltip_Pear_ShowsActivationsAndMaxHpGained()
    {
        var body = InvokeTooltipBuilder(new RelicAggregate
        {
            Activations = 1,
            MaxHpGained = 10m,
            OriginalMaxHp = 70m,
            NewMaxHp = 80m,
        });

        Assert.Contains("Activations", body);
        Assert.Contains("Original max HP", body);
        Assert.Contains("New max HP", body);
        Assert.Contains("Max HP gained", body);
        Assert.Contains("[b]1[/b]", body);
        Assert.Contains("[b]70[/b]", body);
        Assert.Contains("[b]80[/b]", body);
        Assert.Contains("[b]10[/b]", body);
        Assert.DoesNotContain("HP healed", body);
    }

    [Fact]
    public void RelicTooltip_Pear_ShowsZeroRowsForEmptyAggregate()
    {
        var body = InvokeTooltipBuilder(new RelicAggregate());

        Assert.Contains("Activations", body);
        Assert.Contains("Original max HP", body);
        Assert.Contains("New max HP", body);
        Assert.Contains("Max HP gained", body);
        Assert.Equal(4, CountOccurrences(body, "[b]0[/b]"));
    }

    private static string InvokeTooltipBuilder(RelicAggregate agg)
    {
        return (string)(BuildPearBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildPearBodyBBCode returned null."));
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
