using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Entities.Relics;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class ShovelStatsTests
{
    private const string ShovelRelicId = "RELIC.SHOVEL";

    private static readonly MethodInfo BuildShovelBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildShovelBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildShovelBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_ShovelFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.RelicsAcquired);
        Assert.Equal(0, agg.CommonRelicsAcquired);
        Assert.Equal(0, agg.UncommonRelicsAcquired);
        Assert.Equal(0, agg.RareRelicsAcquired);
        Assert.Equal(0, agg.CampfiresNotDug);
    }

    [Fact]
    public void RelicAggregate_ShovelFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[ShovelRelicId] = new RelicAggregate
        {
            Activations = 4,
            RelicsAcquired = 4,
            CommonRelicsAcquired = 1,
            UncommonRelicsAcquired = 2,
            RareRelicsAcquired = 1,
            CampfiresNotDug = 3,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("relics_acquired", json);
        Assert.Contains("common_relics_acquired", json);
        Assert.Contains("uncommon_relics_acquired", json);
        Assert.Contains("rare_relics_acquired", json);
        Assert.Contains("campfires_not_dug", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[ShovelRelicId];
        Assert.Equal(4, restoredAgg.Activations);
        Assert.Equal(4, restoredAgg.RelicsAcquired);
        Assert.Equal(1, restoredAgg.CommonRelicsAcquired);
        Assert.Equal(2, restoredAgg.UncommonRelicsAcquired);
        Assert.Equal(1, restoredAgg.RareRelicsAcquired);
        Assert.Equal(3, restoredAgg.CampfiresNotDug);
    }

    [Fact]
    public void RunTracker_RecordShovelRelicAcquiredForTest_SplitsByRarity()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordShovelRelicAcquiredForTest(agg, RelicRarity.Common);
        RunTracker.RecordShovelRelicAcquiredForTest(agg, RelicRarity.Uncommon);
        RunTracker.RecordShovelRelicAcquiredForTest(agg, RelicRarity.Uncommon);
        RunTracker.RecordShovelRelicAcquiredForTest(agg, RelicRarity.Rare);
        RunTracker.RecordShovelRelicAcquiredForTest(agg, RelicRarity.Event);

        Assert.Equal(5, agg.Activations);
        Assert.Equal(5, agg.RelicsAcquired);
        Assert.Equal(1, agg.CommonRelicsAcquired);
        Assert.Equal(2, agg.UncommonRelicsAcquired);
        Assert.Equal(1, agg.RareRelicsAcquired);
    }

    [Fact]
    public void RunTracker_RecordShovelCampfireNotDugForTest_AccumulatesAndClamps()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordShovelCampfireNotDugForTest(agg);
        RunTracker.RecordShovelCampfireNotDugForTest(agg, count: 3);
        RunTracker.RecordShovelCampfireNotDugForTest(agg, count: -2);

        Assert.Equal(4, agg.CampfiresNotDug);
    }

    [Fact]
    public void RelicTooltip_Shovel_ShowsRelicRarityRows()
    {
        var agg = new RelicAggregate
        {
            RelicsAcquired = 4,
            CommonRelicsAcquired = 1,
            UncommonRelicsAcquired = 2,
            RareRelicsAcquired = 1,
            CampfiresNotDug = 3,
        };

        var body = (string)(BuildShovelBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildShovelBodyBBCode returned null."));

        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("relic_gained"), body);
        Assert.Contains("common relics", body);
        Assert.Contains("uncommon relics", body);
        Assert.Contains("rare relics", body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("campfire"), body);
        Assert.Contains("not dug", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[b]2[/b]", body);
    }

    [Fact]
    public void RelicTooltip_Shovel_ShowsZeroRowsForEmptyAggregate()
    {
        var body = (string)(BuildShovelBodyMethod.Invoke(null, new object?[] { new RelicAggregate() })
            ?? throw new InvalidOperationException("BuildShovelBodyBBCode returned null."));

        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("relic_gained"), body);
        Assert.Contains("common relics", body);
        Assert.Contains("uncommon relics", body);
        Assert.Contains("rare relics", body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("campfire"), body);
        Assert.Contains("not dug", body);
        Assert.Equal(5, CountOccurrences(body, "[b]0[/b]"));
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
