using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class MangoStatsTests
{
    private const string MangoRelicId = "RELIC.MANGO";

    private static readonly MethodInfo BuildMangoBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildMangoBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildMangoBodyBBCode not found.");

    [Fact]
    public void Patch_TargetsMangoAfterObtained()
    {
        var target = typeof(Mango).GetMethod(nameof(Mango.AfterObtained));

        Assert.NotNull(target);
        Assert.Empty(target!.GetParameters());
    }

    [Fact]
    public void RelicAggregate_MangoFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.MaxHpGained);
        Assert.Null(agg.OriginalMaxHp);
        Assert.Null(agg.NewMaxHp);
    }

    [Fact]
    public void RelicAggregate_MangoFields_JsonRoundtripPreservesValues()
    {
        var run = new RunData();
        run.RelicAggregates[MangoRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"activations\"", json);
        Assert.Contains("\"max_hp_gained\"", json);
        Assert.Contains("\"original_max_hp\"", json);
        Assert.Contains("\"new_max_hp\"", json);
        Assert.NotNull(restored);
        AssertPopulatedAggregate(restored!.RelicAggregates[MangoRelicId]);
    }

    [Fact]
    public void RunTracker_MangoHelper_AccumulatesOnlyObservedNonNegativeGains()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordMangoMaxHpGainedForTest(agg, 14m, 70m, 84m);
        RunTracker.RecordMangoMaxHpGainedForTest(agg, 0m, 84m, 84m);
        RunTracker.RecordMangoMaxHpGainedForTest(agg, -1m, 84m, 83m);

        Assert.Equal(2, agg.Activations);
        Assert.Equal(14m, agg.MaxHpGained);
        Assert.Equal(70m, agg.OriginalMaxHp);
        Assert.Equal(84m, agg.NewMaxHp);
    }

    [Fact]
    public void RelicTooltip_Mango_ShowsUsualMaxHpGainRows()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("Activations", body);
        Assert.Contains("Original max HP", body);
        Assert.Contains("New max HP", body);
        Assert.Contains("Max HP gained", body);
        Assert.Contains("[b]1[/b]", body);
        Assert.Contains("[b]70[/b]", body);
        Assert.Contains("[b]84[/b]", body);
        Assert.Contains("[b]14[/b]", body);
        Assert.DoesNotContain("HP healed", body);
    }

    [Fact]
    public void RelicTooltip_Mango_ShowsZeroRowsForEmptyAggregate()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Activations", body);
        Assert.Contains("Original max HP", body);
        Assert.Contains("New max HP", body);
        Assert.Contains("Max HP gained", body);
        Assert.Equal(4, CountOccurrences(body, "[b]0[/b]"));
    }

    [Fact]
    public void RelicTooltip_Mango_DispatchesForModel()
    {
        var relic = (Mango)RuntimeHelpers.GetUninitializedObject(typeof(Mango));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            PopulatedAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Mango", title);
        Assert.Contains("Max HP gained", body);
    }

    private static RelicAggregate PopulatedAggregate()
        => new()
        {
            Activations = 1,
            MaxHpGained = 14m,
            OriginalMaxHp = 70m,
            NewMaxHp = 84m,
        };

    private static void AssertPopulatedAggregate(RelicAggregate agg)
    {
        Assert.Equal(1, agg.Activations);
        Assert.Equal(14m, agg.MaxHpGained);
        Assert.Equal(70m, agg.OriginalMaxHp);
        Assert.Equal(84m, agg.NewMaxHp);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildMangoBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildMangoBodyBBCode returned null."));

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
