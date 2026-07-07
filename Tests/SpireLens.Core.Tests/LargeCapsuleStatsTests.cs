using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class LargeCapsuleStatsTests
{
    private const string LargeCapsuleRelicId = "RELIC.LARGE_CAPSULE";

    private static readonly MethodInfo BuildLargeCapsuleBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildLargeCapsuleBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildLargeCapsuleBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_LargeCapsuleFields_DefaultToEmpty()
    {
        var agg = new RelicAggregate();

        Assert.Empty(agg.RelicsGranted);
    }

    [Fact]
    public void RelicAggregate_LargeCapsuleFields_JsonRoundtrip_PreservesGrantedRelics()
    {
        var run = new RunData();
        run.RelicAggregates[LargeCapsuleRelicId] = new RelicAggregate
        {
            RelicsGranted =
            {
                ["RELIC.DATA_DISK"] = new RelicGrantedAggregate
                {
                    RelicId = "RELIC.DATA_DISK",
                    DisplayName = "Data Disk",
                    Count = 1,
                },
            },
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("relics_granted", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[LargeCapsuleRelicId];
        Assert.Equal(1, agg.RelicsGranted["RELIC.DATA_DISK"].Count);
        Assert.Equal("Data Disk", agg.RelicsGranted["RELIC.DATA_DISK"].DisplayName);
    }

    [Fact]
    public void RunTracker_LargeCapsuleHelper_RecordsGrantedRelics()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordLargeCapsuleRelicObtainedForTest(agg, "RELIC.DATA_DISK", "Data Disk");
        RunTracker.RecordLargeCapsuleRelicObtainedForTest(agg, "RELIC.DATA_DISK", "Data Disk");
        RunTracker.RecordLargeCapsuleRelicObtainedForTest(agg, null, null);

        Assert.Single(agg.RelicsGranted);
        Assert.Equal(2, agg.RelicsGranted["RELIC.DATA_DISK"].Count);
        Assert.Equal("Data Disk", agg.RelicsGranted["RELIC.DATA_DISK"].DisplayName);
    }

    [Fact]
    public void MergeRelicAggregateInto_LargeCapsuleFields_MergesGrantedRelics()
    {
        var target = new RelicAggregate();
        var source = new RelicAggregate();
        source.RelicsGranted["RELIC.DATA_DISK"] = new RelicGrantedAggregate
        {
            RelicId = "RELIC.DATA_DISK",
            DisplayName = "Data Disk",
            Count = 1,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(1, target.RelicsGranted["RELIC.DATA_DISK"].Count);
        Assert.Equal("Data Disk", target.RelicsGranted["RELIC.DATA_DISK"].DisplayName);
    }

    [Fact]
    public void RelicTooltip_LargeCapsule_ShowsRelicsObtained()
    {
        var agg = new RelicAggregate
        {
            RelicsGranted =
            {
                ["RELIC.DATA_DISK"] = new RelicGrantedAggregate
                {
                    RelicId = "RELIC.DATA_DISK",
                    DisplayName = "Data Disk",
                    Count = 1,
                },
                ["RELIC.BAG_OF_PREPARATION"] = new RelicGrantedAggregate
                {
                    RelicId = "RELIC.BAG_OF_PREPARATION",
                    DisplayName = "Bag of Preparation",
                    Count = 2,
                },
            },
        };

        var body = BuildBody(agg);

        Assert.Contains("Relics obtained", body);
        Assert.Contains("Obtained", body);
        Assert.Contains("Data Disk", body);
        Assert.Contains("Bag of Preparation x2", body);
        Assert.Contains("[b]3[/b]", body);
    }

    [Fact]
    public void RelicTooltip_LargeCapsule_ShowsZeroValue()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Relics obtained", body);
        Assert.Contains("[b]0[/b]", body);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildLargeCapsuleBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildLargeCapsuleBodyBBCode returned null."));
}
