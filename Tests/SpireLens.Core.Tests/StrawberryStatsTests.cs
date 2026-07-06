using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class StrawberryStatsTests
{
    private const string StrawberryRelicId = "RELIC.STRAWBERRY";

    private static readonly MethodInfo BuildStrawberryBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildStrawberryBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildStrawberryBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_StrawberryFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.MaxHpGained);
        Assert.Null(agg.OriginalMaxHp);
        Assert.Null(agg.NewMaxHp);
    }

    [Fact]
    public void RelicAggregate_StrawberryFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[StrawberryRelicId] = new RelicAggregate
        {
            Activations = 1,
            MaxHpGained = 7m,
            OriginalMaxHp = 70m,
            NewMaxHp = 77m,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("max_hp_gained", json);
        Assert.Contains("original_max_hp", json);
        Assert.Contains("new_max_hp", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[StrawberryRelicId];
        Assert.Equal(1, restoredAgg.Activations);
        Assert.Equal(7m, restoredAgg.MaxHpGained);
        Assert.Equal(70m, restoredAgg.OriginalMaxHp);
        Assert.Equal(77m, restoredAgg.NewMaxHp);
    }

    [Fact]
    public void RunTracker_RecordStrawberryMaxHpGainedForTest_AccumulatesObservedMaxHp()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordStrawberryMaxHpGainedForTest(agg, 7m, 70m, 77m);
        RunTracker.RecordStrawberryMaxHpGainedForTest(agg, 0m, 77m, 77m);
        RunTracker.RecordStrawberryMaxHpGainedForTest(agg, -1m);

        Assert.Equal(2, agg.Activations);
        Assert.Equal(7m, agg.MaxHpGained);
        Assert.Equal(70m, agg.OriginalMaxHp);
        Assert.Equal(77m, agg.NewMaxHp);
    }

    [Fact]
    public void RelicTooltip_Strawberry_ShowsActivationsAndMaxHpGained()
    {
        var body = InvokeTooltipBuilder(new RelicAggregate
        {
            Activations = 1,
            MaxHpGained = 7m,
            OriginalMaxHp = 70m,
            NewMaxHp = 77m,
        });

        Assert.Contains("Activations", body);
        Assert.Contains("Original max HP", body);
        Assert.Contains("New max HP", body);
        Assert.Contains("Max HP gained", body);
        Assert.Contains("[b]1[/b]", body);
        Assert.Contains("[b]70[/b]", body);
        Assert.Contains("[b]77[/b]", body);
        Assert.Contains("[b]7[/b]", body);
        Assert.DoesNotContain("HP healed", body);
    }

    [Fact]
    public void RelicTooltip_Strawberry_ShowsZeroRowsForEmptyAggregate()
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
        return (string)(BuildStrawberryBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildStrawberryBodyBBCode returned null."));
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
