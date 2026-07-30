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

public class OrreryStatsTests
{
    private const string OrreryRelicId = "RELIC.ORRERY";

    private static readonly MethodInfo BuildOrreryBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildOrreryBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildOrreryBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_OrreryRewards_DefaultToEmpty()
    {
        var agg = new RelicAggregate();

        Assert.NotNull(agg.OrreryRewards);
        Assert.Empty(agg.OrreryRewards);
    }

    [Fact]
    public void RelicAggregate_OrreryRewards_JsonRoundtripPreservesOrderAndOutcomes()
    {
        var run = new RunData();
        run.RelicAggregates[OrreryRelicId] = BuildPopulatedAggregate();

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("orrery_rewards", json);
        Assert.Contains("reward_number", json);
        Assert.Contains("alternative_id", json);
        Assert.Contains("offered_card_ids", json);
        Assert.Contains("cards_obtained", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        AssertPopulated(restored!.RelicAggregates[OrreryRelicId]);
    }

    [Fact]
    public void RecordOrreryRewardForTest_OrdersAndUpsertsWithoutRegressingResolvedOutcome()
    {
        var agg = new RelicAggregate();
        var third = Reward(3, "alternative", alternativeId: "SACRIFICE");
        var first = Reward(1, "skipped");
        var replacement = Reward(
            1,
            "obtained",
            cards: new[] { Card("CARD.POMMEL_STRIKE", "Pommel Strike", 0) });

        RunTracker.RecordOrreryRewardForTest(agg, third);
        RunTracker.RecordOrreryRewardForTest(agg, first);
        RunTracker.RecordOrreryRewardForTest(agg, replacement);
        RunTracker.RecordOrreryRewardForTest(agg, Reward(1, "pending"));

        Assert.Equal(new[] { 1, 3 }, agg.OrreryRewards.Select(reward => reward.RewardNumber));
        Assert.Equal("obtained", agg.OrreryRewards[0].Outcome);
        Assert.Equal("Pommel Strike", agg.OrreryRewards[0].CardsObtained[0].DisplayName);
        Assert.Equal("SACRIFICE", agg.OrreryRewards[1].AlternativeId);

        replacement.CardsObtained[0].DisplayName = "mutated source";
        Assert.Equal("Pommel Strike", agg.OrreryRewards[0].CardsObtained[0].DisplayName);
    }

    [Fact]
    public void MergeRelicAggregateInto_OrreryRewards_DeepCopiesAndUpserts()
    {
        var target = new RelicAggregate
        {
            OrreryRewards = new()
            {
                Reward(1, "skipped"),
            },
        };
        var source = new RelicAggregate
        {
            OrreryRewards = new()
            {
                Reward(
                    1,
                    "obtained",
                    cards: new[] { Card("CARD.POMMEL_STRIKE", "Pommel Strike", 0) }),
                Reward(2, "alternative", alternativeId: "SACRIFICE"),
            },
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(new[] { 1, 2 }, target.OrreryRewards.Select(reward => reward.RewardNumber));
        Assert.Equal("obtained", target.OrreryRewards[0].Outcome);
        Assert.Equal("Pommel Strike", target.OrreryRewards[0].CardsObtained[0].DisplayName);
        Assert.Equal("alternative", target.OrreryRewards[1].Outcome);
        Assert.Equal("SACRIFICE", target.OrreryRewards[1].AlternativeId);

        source.OrreryRewards[0].CardsObtained[0].DisplayName = "mutated source";
        Assert.Equal("Pommel Strike", target.OrreryRewards[0].CardsObtained[0].DisplayName);
    }

    [Fact]
    public void RelicTooltip_Orrery_ShowsEveryRewardAndFinalHandlingInOrder()
    {
        var body = BuildBody(BuildPopulatedAggregate());

        Assert.Contains("Reward 1", body);
        Assert.Contains("skipped", body);
        Assert.Contains("Reward 2", body);
        Assert.Contains("obtained Pommel Strike", body);
        Assert.Contains("Reward 3", body);
        Assert.Contains(
            "res://images/atlases/relic_atlas.sprites/paels_wing.tres",
            body);
        Assert.DoesNotContain("sacrificed to Pael", body);
        Assert.Contains("Reward 4", body);
        Assert.Contains("pending", body);
        Assert.Contains("Reward 5", body);
        Assert.Contains("not seen yet", body);
        Assert.True(body.IndexOf("Reward 1", StringComparison.Ordinal) < body.IndexOf("Reward 2", StringComparison.Ordinal));
        Assert.True(body.IndexOf("Reward 2", StringComparison.Ordinal) < body.IndexOf("Reward 3", StringComparison.Ordinal));
        Assert.True(body.IndexOf("Reward 3", StringComparison.Ordinal) < body.IndexOf("Reward 4", StringComparison.Ordinal));
        Assert.True(body.IndexOf("Reward 4", StringComparison.Ordinal) < body.IndexOf("Reward 5", StringComparison.Ordinal));
    }

    [Fact]
    public void RelicTooltip_Orrery_DispatchesForOrreryModel()
    {
        var relic = (Orrery)RuntimeHelpers.GetUninitializedObject(typeof(Orrery));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            BuildPopulatedAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Orrery", title);
        Assert.Contains("Reward 1", body);
        Assert.Contains("Reward 5", body);
    }

    [Fact]
    public void RunData_OlderShapeWithoutOrreryRewards_DeserializesWithEmptyDefault()
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
                "RELIC.ORRERY": {}
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        Assert.Empty(run!.RelicAggregates[OrreryRelicId].OrreryRewards);
    }

    private static RelicAggregate BuildPopulatedAggregate()
        => new()
        {
            OrreryRewards = new()
            {
                Reward(1, "skipped"),
                Reward(
                    2,
                    "obtained",
                    cards: new[] { Card("CARD.POMMEL_STRIKE", "Pommel Strike", 0) }),
                Reward(3, "alternative", alternativeId: "SACRIFICE"),
                Reward(4, "pending"),
            },
        };

    private static OrreryRewardAggregate Reward(
        int rewardNumber,
        string outcome,
        string alternativeId = "",
        IReadOnlyList<OrreryObtainedCardAggregate>? cards = null)
        => new()
        {
            RewardNumber = rewardNumber,
            Floor = 12,
            Outcome = outcome,
            AlternativeId = alternativeId,
            OfferedCardIds = new()
            {
                $"CARD.REWARD_{rewardNumber}_A",
                $"CARD.REWARD_{rewardNumber}_B",
                $"CARD.REWARD_{rewardNumber}_C",
            },
            CardsObtained = cards?.ToList() ?? new List<OrreryObtainedCardAggregate>(),
        };

    private static OrreryObtainedCardAggregate Card(
        string cardId,
        string displayName,
        int upgradeLevel)
        => new()
        {
            CardId = cardId,
            DisplayName = displayName,
            UpgradeLevel = upgradeLevel,
        };

    private static void AssertPopulated(RelicAggregate agg)
    {
        Assert.Equal(new[] { 1, 2, 3, 4 }, agg.OrreryRewards.Select(reward => reward.RewardNumber));
        Assert.All(agg.OrreryRewards, reward => Assert.Equal(12, reward.Floor));
        Assert.Equal("skipped", agg.OrreryRewards[0].Outcome);
        Assert.Equal("obtained", agg.OrreryRewards[1].Outcome);
        Assert.Equal("CARD.POMMEL_STRIKE", agg.OrreryRewards[1].CardsObtained[0].CardId);
        Assert.Equal("Pommel Strike", agg.OrreryRewards[1].CardsObtained[0].DisplayName);
        Assert.Equal(0, agg.OrreryRewards[1].CardsObtained[0].UpgradeLevel);
        Assert.Equal("alternative", agg.OrreryRewards[2].Outcome);
        Assert.Equal("SACRIFICE", agg.OrreryRewards[2].AlternativeId);
        Assert.Equal("pending", agg.OrreryRewards[3].Outcome);
        Assert.Equal(
            new[] { "CARD.REWARD_4_A", "CARD.REWARD_4_B", "CARD.REWARD_4_C" },
            agg.OrreryRewards[3].OfferedCardIds);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildOrreryBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildOrreryBodyBBCode returned null."));
}
