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

public class FresnelLensStatsTests
{
    private const string FresnelLensRelicId = "RELIC.FRESNEL_LENS";

    private static readonly MethodInfo BuildFresnelLensBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildFresnelLensBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildFresnelLensBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_FresnelLensFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Null(agg.OriginalMaxHp);
        Assert.Null(agg.NewMaxHp);
        Assert.Equal(0, agg.NimbleCardsTaken);
        Assert.Equal(0, agg.RewardScreensWithNimbleCards);
        Assert.Equal(0, agg.RewardScreensWithTwoNimbleCards);
        Assert.Equal(0, agg.RewardScreensWithThreeOrMoreNimbleCards);
        Assert.Equal(0, agg.RewardScreensWithoutNimbleCards);
        Assert.Equal(0, agg.RewardScreensWithNimbleCardsButNoneTaken);
    }

    [Fact]
    public void RelicAggregate_FresnelLensFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[FresnelLensRelicId] = BuildPopulatedAggregate();

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("original_max_hp", json);
        Assert.Contains("new_max_hp", json);
        Assert.Contains("nimble_cards_taken", json);
        Assert.Contains("reward_screens_with_nimble_cards", json);
        Assert.Contains("reward_screens_with_two_nimble_cards", json);
        Assert.Contains("reward_screens_with_three_or_more_nimble_cards", json);
        Assert.Contains("reward_screens_without_nimble_cards", json);
        Assert.Contains("reward_screens_with_nimble_cards_but_none_taken", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        AssertPopulated(restored!.RelicAggregates[FresnelLensRelicId]);
    }

    [Fact]
    public void RunTracker_RecordFresnelLensRewardForTest_RecordsOfferBreakdownAndTakenCards()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordFresnelLensRewardForTest(agg, nimbleCardsOffered: 0, nimbleCardsTaken: 0);
        RunTracker.RecordFresnelLensRewardForTest(agg, nimbleCardsOffered: 1, nimbleCardsTaken: 1);
        RunTracker.RecordFresnelLensRewardForTest(agg, nimbleCardsOffered: 2, nimbleCardsTaken: 0);
        RunTracker.RecordFresnelLensRewardForTest(agg, nimbleCardsOffered: 4, nimbleCardsTaken: 2);

        Assert.Equal(3, agg.NimbleCardsTaken);
        Assert.Equal(3, agg.RewardScreensWithNimbleCards);
        Assert.Equal(1, agg.RewardScreensWithTwoNimbleCards);
        Assert.Equal(1, agg.RewardScreensWithThreeOrMoreNimbleCards);
        Assert.Equal(1, agg.RewardScreensWithoutNimbleCards);
        Assert.Equal(1, agg.RewardScreensWithNimbleCardsButNoneTaken);
    }

    [Fact]
    public void RunTracker_RecordFresnelLensMaxHpChangedForTest_RecordsObservedSnapshot()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordFresnelLensMaxHpChangedForTest(agg, originalMaxHp: 70m, newMaxHp: 57m);

        Assert.Equal(70m, agg.OriginalMaxHp);
        Assert.Equal(57m, agg.NewMaxHp);
    }

    [Fact]
    public void MergeRelicAggregateInto_FresnelLensFields_Accumulates()
    {
        var target = new RelicAggregate
        {
            NimbleCardsTaken = 2,
            RewardScreensWithNimbleCards = 1,
            RewardScreensWithTwoNimbleCards = 2,
            RewardScreensWithThreeOrMoreNimbleCards = 3,
            RewardScreensWithoutNimbleCards = 4,
            RewardScreensWithNimbleCardsButNoneTaken = 5,
        };
        var source = new RelicAggregate
        {
            NimbleCardsTaken = 3,
            RewardScreensWithNimbleCards = 6,
            RewardScreensWithTwoNimbleCards = 7,
            RewardScreensWithThreeOrMoreNimbleCards = 8,
            RewardScreensWithoutNimbleCards = 9,
            RewardScreensWithNimbleCardsButNoneTaken = 10,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(5, target.NimbleCardsTaken);
        Assert.Equal(7, target.RewardScreensWithNimbleCards);
        Assert.Equal(9, target.RewardScreensWithTwoNimbleCards);
        Assert.Equal(11, target.RewardScreensWithThreeOrMoreNimbleCards);
        Assert.Equal(13, target.RewardScreensWithoutNimbleCards);
        Assert.Equal(15, target.RewardScreensWithNimbleCardsButNoneTaken);
    }

    [Fact]
    public void RelicTooltip_FresnelLens_ShowsMaxHpLossAndNimbleRewardStats()
    {
        var body = BuildBody(BuildPopulatedAggregate());

        Assert.Contains("Original max HP", body);
        Assert.Contains("New max HP", body);
        Assert.Contains("Max HP lost to Drowning Beacon", body);
        Assert.Contains("Nimble cards taken", body);
        Assert.Contains("Reward screens with Nimble cards", body);
        Assert.Contains("Reward screens with 2 Nimble cards", body);
        Assert.Contains("Reward screens with 3+ Nimble cards", body);
        Assert.Contains("Reward screens with no Nimble cards", body);
        Assert.Contains("Nimble offered, none taken", body);
        Assert.Contains("[b]70[/b]", body);
        Assert.Contains("[b]57[/b]", body);
        Assert.Contains("[b]13[/b]", body);
        Assert.Contains("[b]9[/b]", body);
        Assert.Contains("[b]8[/b]", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]5[/b]", body);
        Assert.Contains("[b]4[/b]", body);
    }

    [Fact]
    public void RelicTooltip_FresnelLens_ShowsZeroRowsWithoutStats()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Equal(9, CountOccurrences(body, "[b]0[/b]"));
    }

    [Fact]
    public void RelicTooltip_FresnelLens_DispatchesForFresnelLensModel()
    {
        var relic = (FresnelLens)RuntimeHelpers.GetUninitializedObject(typeof(FresnelLens));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            BuildPopulatedAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Fresnel Lens", title);
        Assert.Contains("Nimble cards taken", body);
    }

    [Fact]
    public void RunData_OlderShapeWithoutFresnelLensFields_DeserializesWithZeroDefaults()
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
                "RELIC.FRESNEL_LENS": {
                  "original_max_hp": 70,
                  "new_max_hp": 57
                }
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        var agg = run!.RelicAggregates[FresnelLensRelicId];
        Assert.Equal(70m, agg.OriginalMaxHp);
        Assert.Equal(57m, agg.NewMaxHp);
        Assert.Equal(0, agg.NimbleCardsTaken);
        Assert.Equal(0, agg.RewardScreensWithNimbleCards);
        Assert.Equal(0, agg.RewardScreensWithTwoNimbleCards);
        Assert.Equal(0, agg.RewardScreensWithThreeOrMoreNimbleCards);
        Assert.Equal(0, agg.RewardScreensWithoutNimbleCards);
        Assert.Equal(0, agg.RewardScreensWithNimbleCardsButNoneTaken);
    }

    private static RelicAggregate BuildPopulatedAggregate()
        => new()
        {
            OriginalMaxHp = 70m,
            NewMaxHp = 57m,
            NimbleCardsTaken = 9,
            RewardScreensWithNimbleCards = 8,
            RewardScreensWithTwoNimbleCards = 3,
            RewardScreensWithThreeOrMoreNimbleCards = 2,
            RewardScreensWithoutNimbleCards = 5,
            RewardScreensWithNimbleCardsButNoneTaken = 4,
        };

    private static void AssertPopulated(RelicAggregate agg)
    {
        Assert.Equal(70m, agg.OriginalMaxHp);
        Assert.Equal(57m, agg.NewMaxHp);
        Assert.Equal(9, agg.NimbleCardsTaken);
        Assert.Equal(8, agg.RewardScreensWithNimbleCards);
        Assert.Equal(3, agg.RewardScreensWithTwoNimbleCards);
        Assert.Equal(2, agg.RewardScreensWithThreeOrMoreNimbleCards);
        Assert.Equal(5, agg.RewardScreensWithoutNimbleCards);
        Assert.Equal(4, agg.RewardScreensWithNimbleCardsButNoneTaken);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildFresnelLensBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildFresnelLensBodyBBCode returned null."));

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
