using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class PaelsWingStatsTests
{
    private const string PaelsWingRelicId = "RELIC.PAELS_WING";

    private static readonly MethodInfo BuildPaelsWingBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildPaelsWingBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildPaelsWingBodyBBCode not found.");

    private static readonly MethodInfo BuildPaelsWingBodyForFloorMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildPaelsWingBodyBBCodeForFloor", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildPaelsWingBodyBBCodeForFloor not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_PaelsWingFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.CommonCardsConsumed);
        Assert.Equal(0, agg.UncommonCardsConsumed);
        Assert.Equal(0, agg.RareCardsConsumed);
        Assert.Equal(0, agg.SacrificesMade);
        Assert.Equal(0, agg.SacrificesSkipped);
        Assert.Empty(agg.RelicsGranted);
    }

    [Fact]
    public void RelicAggregate_PaelsWingFields_JsonRoundtrip_PreserveFields()
    {
        var agg = new RelicAggregate
        {
            CommonCardsConsumed = 5,
            UncommonCardsConsumed = 3,
            RareCardsConsumed = 1,
            SacrificesMade = 3,
            SacrificesSkipped = 2,
            RelicsGranted =
            {
                ["RELIC.KUNAI"] = new RelicGrantedAggregate
                {
                    RelicId = "RELIC.KUNAI",
                    DisplayName = "Kunai",
                    Count = 1,
                },
            },
        };
        var run = new RunData();
        run.RelicAggregates[PaelsWingRelicId] = agg;

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("common_cards_consumed", json);
        Assert.Contains("uncommon_cards_consumed", json);
        Assert.Contains("rare_cards_consumed", json);
        Assert.Contains("sacrifices_made", json);
        Assert.Contains("sacrifices_skipped", json);
        Assert.Contains("relics_granted", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        Assert.True(restored!.RelicAggregates.ContainsKey(PaelsWingRelicId));
        var restoredAgg = restored.RelicAggregates[PaelsWingRelicId];
        Assert.Equal(5, restoredAgg.CommonCardsConsumed);
        Assert.Equal(3, restoredAgg.UncommonCardsConsumed);
        Assert.Equal(1, restoredAgg.RareCardsConsumed);
        Assert.Equal(3, restoredAgg.SacrificesMade);
        Assert.Equal(2, restoredAgg.SacrificesSkipped);
        Assert.Equal(1, restoredAgg.RelicsGranted["RELIC.KUNAI"].Count);
        Assert.Equal("Kunai", restoredAgg.RelicsGranted["RELIC.KUNAI"].DisplayName);
    }

    [Fact]
    public void RelicTooltip_PaelsWingFields_ShowSacrificeRows()
    {
        var agg = new RelicAggregate
        {
            CommonCardsConsumed = 5,
            UncommonCardsConsumed = 3,
            RareCardsConsumed = 1,
            SacrificesMade = 3,
            SacrificesSkipped = 2,
            RelicsGranted =
            {
                ["RELIC.KUNAI"] = new RelicGrantedAggregate
                {
                    RelicId = "RELIC.KUNAI",
                    DisplayName = "Kunai",
                    Count = 2,
                },
                ["RELIC.BAG_OF_PREPARATION"] = new RelicGrantedAggregate
                {
                    RelicId = "RELIC.BAG_OF_PREPARATION",
                    DisplayName = "Bag of Preparation",
                    Count = 1,
                },
            },
        };

        var body = (string)(BuildPaelsWingBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildPaelsWingBodyBBCode returned null."));

        Assert.Contains("common cards consumed", body);
        Assert.Contains("uncommon cards consumed", body);
        Assert.Contains("rare cards consumed", body);
        Assert.Contains("Sacrifices made", body);
        Assert.Contains("Sacrifices skipped", body);
        Assert.Contains("Artifacts gained", body);
        Assert.Contains("Artifact gained", body);
        Assert.Contains("Kunai x2", body);
        Assert.Contains("Bag of Preparation", body);
        Assert.DoesNotContain("Sacrifice rate", body);
        Assert.DoesNotContain("/floor", body);
        Assert.Contains("[b]5[/b]", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]1[/b]", body);
    }

    [Fact]
    public void RunTracker_PaelsWingHelper_RecordsEachArtifactGained()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordPaelsWingArtifactGainedForTest(agg, "RELIC.KUNAI", "Kunai");
        RunTracker.RecordPaelsWingArtifactGainedForTest(agg, "RELIC.KUNAI", "Kunai");
        RunTracker.RecordPaelsWingArtifactGainedForTest(
            agg,
            "RELIC.BAG_OF_PREPARATION",
            "Bag of Preparation");
        RunTracker.RecordPaelsWingArtifactGainedForTest(agg, null, null);

        Assert.Equal(2, agg.RelicsGranted.Count);
        Assert.Equal(2, agg.RelicsGranted["RELIC.KUNAI"].Count);
        Assert.Equal("Kunai", agg.RelicsGranted["RELIC.KUNAI"].DisplayName);
        Assert.Equal(1, agg.RelicsGranted["RELIC.BAG_OF_PREPARATION"].Count);
        Assert.Equal("Bag of Preparation", agg.RelicsGranted["RELIC.BAG_OF_PREPARATION"].DisplayName);
    }

    [Fact]
    public void RelicTooltip_PaelsWingRate_UsesFloorsSinceRelicWasObtained()
    {
        var agg = new RelicAggregate
        {
            SacrificesMade = 3,
        };

        var body = (string)(BuildPaelsWingBodyForFloorMethod.Invoke(null, new object?[] { agg, 8, 5 })
            ?? throw new InvalidOperationException("BuildPaelsWingBodyBBCodeForFloor returned null."));

        Assert.Contains("Sacrifice rate", body);
        Assert.Contains("[b]0.75[/b]", body);
        Assert.DoesNotContain("[b]0.38[/b]", body);
    }

    [Fact]
    public void RelicTooltip_PaelsWingRate_HidesRateWithoutObtainedFloor()
    {
        var agg = new RelicAggregate
        {
            SacrificesMade = 3,
        };

        var body = (string)(BuildPaelsWingBodyForFloorMethod.Invoke(null, new object?[] { agg, 8, null })
            ?? throw new InvalidOperationException("BuildPaelsWingBodyBBCodeForFloor returned null."));

        Assert.Contains("Sacrifices made", body);
        Assert.DoesNotContain("Sacrifice rate", body);
        Assert.DoesNotContain("/floor", body);
        Assert.DoesNotContain("[b]0.38[/b]", body);
    }

    [Fact]
    public void RunData_OlderShapeWithoutPaelsWingFields_DeserializesWithZeroDefault()
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
                "RELIC.PAELS_WING": {
                  "activations": 0
                }
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        Assert.True(run!.RelicAggregates.ContainsKey(PaelsWingRelicId));
        var agg = run.RelicAggregates[PaelsWingRelicId];
        Assert.Equal(0, agg.CommonCardsConsumed);
        Assert.Equal(0, agg.UncommonCardsConsumed);
        Assert.Equal(0, agg.RareCardsConsumed);
        Assert.Equal(0, agg.SacrificesMade);
        Assert.Equal(0, agg.SacrificesSkipped);
        Assert.Empty(agg.RelicsGranted);
    }
}
