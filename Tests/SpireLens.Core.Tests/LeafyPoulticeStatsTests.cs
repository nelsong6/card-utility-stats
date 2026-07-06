using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class LeafyPoulticeStatsTests
{
    private const string LeafyPoulticeRelicId = "RELIC.LEAFY_POULTICE";

    private static readonly MethodInfo BuildLeafyPoulticeBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildLeafyPoulticeBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildLeafyPoulticeBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_LeafyPoulticeFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Null(agg.OriginalMaxHp);
        Assert.Null(agg.NewMaxHp);
    }

    [Fact]
    public void RelicAggregate_LeafyPoulticeFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[LeafyPoulticeRelicId] = new RelicAggregate
        {
            Activations = 1,
            OriginalMaxHp = 70m,
            NewMaxHp = 58m,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("original_max_hp", json);
        Assert.Contains("new_max_hp", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[LeafyPoulticeRelicId];
        Assert.Equal(1, agg.Activations);
        Assert.Equal(70m, agg.OriginalMaxHp);
        Assert.Equal(58m, agg.NewMaxHp);
    }

    [Fact]
    public void RunTracker_RecordLeafyPoulticeMaxHpChangedForTest_RecordsSnapshot()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordLeafyPoulticeMaxHpChangedForTest(agg, 70m, 58m);

        Assert.Equal(1, agg.Activations);
        Assert.Equal(70m, agg.OriginalMaxHp);
        Assert.Equal(58m, agg.NewMaxHp);
    }

    [Fact]
    public void RelicTooltip_LeafyPoultice_ShowsMaxHpLossRows()
    {
        var body = BuildBody(new RelicAggregate
        {
            Activations = 1,
            OriginalMaxHp = 70m,
            NewMaxHp = 58m,
        });

        Assert.Contains("Activations", body);
        Assert.Contains("Original max HP", body);
        Assert.Contains("New max HP", body);
        Assert.Contains("Max HP lost", body);
        Assert.Contains("[b]1[/b]", body);
        Assert.Contains("[b]70[/b]", body);
        Assert.Contains("[b]58[/b]", body);
        Assert.Contains("[b]12[/b]", body);
    }

    [Fact]
    public void RelicTooltip_LeafyPoultice_ShowsZeroRowsWithoutStats()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Activations", body);
        Assert.Contains("Original max HP", body);
        Assert.Contains("New max HP", body);
        Assert.Contains("Max HP lost", body);
        Assert.Equal(4, CountOccurrences(body, "[b]0[/b]"));
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildLeafyPoulticeBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildLeafyPoulticeBodyBBCode returned null."));

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
