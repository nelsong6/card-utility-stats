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

public class SilverCrucibleStatsTests
{
    private const string SilverCrucibleRelicId = "RELIC.SILVER_CRUCIBLE";

    private static readonly MethodInfo BuildSilverCrucibleBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildSilverCrucibleBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildSilverCrucibleBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_SilverCrucibleScreens_DefaultToEmpty()
    {
        var agg = new RelicAggregate();

        Assert.NotNull(agg.CardRewardScreens);
        Assert.Empty(agg.CardRewardScreens);
    }

    [Fact]
    public void RelicAggregate_SilverCrucibleScreens_JsonRoundtripPreservesOrderAndOutcomes()
    {
        var run = new RunData();
        run.RelicAggregates[SilverCrucibleRelicId] = BuildPopulatedAggregate();

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("card_reward_screens", json);
        Assert.Contains("screen_number", json);
        Assert.Contains("floor", json);
        Assert.Contains("resolved", json);
        Assert.Contains("upgrade_level", json);
        Assert.Contains("\"taken\"", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        AssertPopulated(restored!.RelicAggregates[SilverCrucibleRelicId]);
    }

    [Fact]
    public void RecordSilverCrucibleRewardForTest_OrdersAndUpsertsByScreenNumber()
    {
        var agg = new RelicAggregate();
        var third = Screen(3, Card("CARD.HEADBUTT", "Headbutt+", taken: false));
        var first = Screen(1, Card("CARD.BASH", "Bash+", taken: true));
        var replacement = Screen(1, Card("CARD.SHRUG_IT_OFF", "Shrug It Off+", taken: false));

        RunTracker.RecordSilverCrucibleRewardForTest(agg, third);
        RunTracker.RecordSilverCrucibleRewardForTest(agg, first);
        RunTracker.RecordSilverCrucibleRewardForTest(agg, replacement);

        Assert.Equal(new[] { 1, 3 }, agg.CardRewardScreens.Select(screen => screen.ScreenNumber));
        Assert.Single(agg.CardRewardScreens[0].Cards);
        Assert.Equal("CARD.SHRUG_IT_OFF", agg.CardRewardScreens[0].Cards[0].CardId);
        Assert.False(agg.CardRewardScreens[0].Cards[0].Taken);

        replacement.Cards[0].DisplayName = "mutated source";
        Assert.Equal("Shrug It Off+", agg.CardRewardScreens[0].Cards[0].DisplayName);
    }

    [Fact]
    public void MergeRelicAggregateInto_SilverCrucibleScreens_DeepCopiesAndUpserts()
    {
        var target = new RelicAggregate
        {
            CardRewardScreens = new()
            {
                Screen(1, Card("CARD.BASH", "Bash+", taken: true)),
            },
        };
        var source = new RelicAggregate
        {
            CardRewardScreens = new()
            {
                Screen(1, Card("CARD.INFLAME", "Inflame+", taken: false)),
                Screen(2, Card("CARD.TRUE_GRIT", "True Grit+", taken: true)),
            },
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(new[] { 1, 2 }, target.CardRewardScreens.Select(screen => screen.ScreenNumber));
        Assert.Equal("CARD.INFLAME", target.CardRewardScreens[0].Cards[0].CardId);
        Assert.False(target.CardRewardScreens[0].Cards[0].Taken);
        Assert.Equal("CARD.TRUE_GRIT", target.CardRewardScreens[1].Cards[0].CardId);
        Assert.True(target.CardRewardScreens[1].Cards[0].Taken);

        source.CardRewardScreens[1].Cards[0].DisplayName = "mutated source";
        Assert.Equal("True Grit+", target.CardRewardScreens[1].Cards[0].DisplayName);
    }

    [Fact]
    public void RelicTooltip_SilverCrucible_ShowsEveryCardAndTakenOutcomeInScreenOrder()
    {
        var agg = new RelicAggregate
        {
            CardRewardScreens = new()
            {
                Screen(
                    1,
                    Card("CARD.BASH", "Bash+", taken: true),
                    Card("CARD.SHRUG_IT_OFF", "Shrug It Off+", taken: false)),
                Screen(3, Card("CARD.HEADBUTT", "Headbutt+", taken: false)),
            },
        };

        var body = BuildBody(agg);

        Assert.Contains("Card reward 1", body);
        Assert.Contains("Bash+", body);
        Assert.Contains("Shrug It Off+", body);
        Assert.Contains("taken", body);
        Assert.Contains("not taken", body);
        Assert.Contains("Card reward 2", body);
        Assert.Contains("not seen yet", body);
        Assert.Contains("Card reward 3", body);
        Assert.Contains("Headbutt+", body);
        Assert.True(body.IndexOf("Bash+", StringComparison.Ordinal) < body.IndexOf("Shrug It Off+", StringComparison.Ordinal));
        Assert.True(body.IndexOf("Shrug It Off+", StringComparison.Ordinal) < body.IndexOf("not seen yet", StringComparison.Ordinal));
        Assert.True(body.IndexOf("not seen yet", StringComparison.Ordinal) < body.IndexOf("Headbutt+", StringComparison.Ordinal));
    }

    [Fact]
    public void RelicTooltip_SilverCrucible_DispatchesForSilverCrucibleModel()
    {
        var relic = (SilverCrucible)RuntimeHelpers.GetUninitializedObject(typeof(SilverCrucible));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            BuildPopulatedAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Silver Crucible", title);
        Assert.Contains("Card reward 1", body);
        Assert.Contains("Bash+", body);
    }

    [Fact]
    public void RelicTooltip_SilverCrucible_MarksGeneratedUnresolvedScreenPending()
    {
        var pending = Screen(1, Card("CARD.BASH", "Bash+", taken: false));
        pending.Resolved = false;
        var agg = new RelicAggregate { CardRewardScreens = new() { pending } };

        var body = BuildBody(agg);

        Assert.Contains("Bash+", body);
        Assert.Contains("pending", body);
    }

    [Fact]
    public void RunData_OlderShapeWithoutSilverCrucibleScreens_DeserializesWithEmptyDefault()
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
                "RELIC.SILVER_CRUCIBLE": {}
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        Assert.Empty(run!.RelicAggregates[SilverCrucibleRelicId].CardRewardScreens);
    }

    private static RelicAggregate BuildPopulatedAggregate()
        => new()
        {
            CardRewardScreens = new()
            {
                Screen(
                    1,
                    Card("CARD.BASH", "Bash+", taken: true),
                    Card("CARD.SHRUG_IT_OFF", "Shrug It Off+", taken: false)),
                Screen(2, Card("CARD.TRUE_GRIT", "True Grit+", taken: true)),
                Screen(3, Card("CARD.HEADBUTT", "Headbutt+", taken: false)),
            },
        };

    private static RelicCardRewardScreenAggregate Screen(
        int number,
        params RelicCardRewardOptionAggregate[] cards)
        => new()
        {
            ScreenNumber = number,
            Floor = 12,
            Resolved = true,
            Cards = cards.ToList(),
        };

    private static RelicCardRewardOptionAggregate Card(
        string cardId,
        string displayName,
        bool taken)
        => new()
        {
            CardId = cardId,
            DisplayName = displayName,
            UpgradeLevel = 1,
            Taken = taken,
        };

    private static void AssertPopulated(RelicAggregate agg)
    {
        Assert.Equal(new[] { 1, 2, 3 }, agg.CardRewardScreens.Select(screen => screen.ScreenNumber));
        Assert.All(agg.CardRewardScreens, screen => Assert.Equal(12, screen.Floor));
        Assert.All(agg.CardRewardScreens, screen => Assert.True(screen.Resolved));
        Assert.Equal(new[] { "CARD.BASH", "CARD.SHRUG_IT_OFF" }, agg.CardRewardScreens[0].Cards.Select(card => card.CardId));
        Assert.True(agg.CardRewardScreens[0].Cards[0].Taken);
        Assert.False(agg.CardRewardScreens[0].Cards[1].Taken);
        Assert.Equal(1, agg.CardRewardScreens[0].Cards[0].UpgradeLevel);
        Assert.True(agg.CardRewardScreens[1].Cards[0].Taken);
        Assert.False(agg.CardRewardScreens[2].Cards[0].Taken);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildSilverCrucibleBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildSilverCrucibleBodyBBCode returned null."));
}
