using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Entities.Cards;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class EnemyStatusStatsTests
{
    private const string HauntedShipEnemyId = "MONSTER.HAUNTED_SHIP";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void EnemyAggregate_StatusFields_DefaultToZero()
    {
        var agg = new EnemyAggregate();

        Assert.Equal(0, agg.DamageInstances);
        Assert.Equal(0, agg.DamageAttempted);
        Assert.Equal(0, agg.DamageBlocked);
        Assert.Equal(0, agg.DamageDealt);
        Assert.Equal(0, agg.StatusCardsAdded);
        Assert.Equal(0, agg.StatusCardsAddedToHand);
        Assert.Equal(0, agg.StatusCardsAddedToDraw);
        Assert.Equal(0, agg.StatusCardsAddedToDiscard);
        Assert.Equal(0, agg.StatusCardsAddedToDeck);
        Assert.Empty(agg.StatusCardsById);
    }

    [Fact]
    public void EnemyAggregate_StatusFields_JsonRoundtrip_PreserveFields()
    {
        var run = new RunData();
        run.EnemyAggregates[HauntedShipEnemyId] = new EnemyAggregate
        {
            DamageInstances = 2,
            DamageAttempted = 12,
            DamageBlocked = 5,
            DamageDealt = 7,
            StatusCardsAdded = 3,
            StatusCardsAddedToHand = 1,
            StatusCardsAddedToDraw = 2,
            StatusCardsById =
            {
                ["CARD.DAZED"] = new EnemyStatusCardAggregate
                {
                    CardId = "CARD.DAZED",
                    DisplayName = "Dazed",
                    Count = 3,
                },
            },
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("enemy_aggregates", json);
        Assert.Contains("damage_instances", json);
        Assert.Contains("damage_attempted", json);
        Assert.Contains("damage_blocked", json);
        Assert.Contains("damage_dealt", json);
        Assert.Contains("status_cards_added", json);
        Assert.Contains("status_cards_added_to_hand", json);
        Assert.Contains("status_cards_by_id", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.EnemyAggregates[HauntedShipEnemyId];
        Assert.Equal(2, agg.DamageInstances);
        Assert.Equal(12, agg.DamageAttempted);
        Assert.Equal(5, agg.DamageBlocked);
        Assert.Equal(7, agg.DamageDealt);
        Assert.Equal(3, agg.StatusCardsAdded);
        Assert.Equal(1, agg.StatusCardsAddedToHand);
        Assert.Equal(2, agg.StatusCardsAddedToDraw);
        Assert.Equal("Dazed", agg.StatusCardsById["CARD.DAZED"].DisplayName);
        Assert.Equal(3, agg.StatusCardsById["CARD.DAZED"].Count);
    }

    [Fact]
    public void EnemyAggregate_RecordDamage_SplitsAttemptedBlockedAndDealt()
    {
        var agg = new EnemyAggregate();

        RunTracker.RecordEnemyDamageToPlayerForTest(agg, blockedDamage: 4, unblockedDamage: 6);
        RunTracker.RecordEnemyDamageToPlayerForTest(agg, blockedDamage: 3, unblockedDamage: 0);

        Assert.Equal(2, agg.DamageInstances);
        Assert.Equal(13, agg.DamageAttempted);
        Assert.Equal(7, agg.DamageBlocked);
        Assert.Equal(6, agg.DamageDealt);
    }

    [Fact]
    public void EnemyTooltip_DamageFields_ShowAttemptedBlockedAndDealt()
    {
        var agg = new EnemyAggregate();
        RunTracker.RecordEnemyDamageToPlayerForTest(agg, blockedDamage: 4, unblockedDamage: 6);
        RunTracker.RecordEnemyDamageToPlayerForTest(agg, blockedDamage: 3, unblockedDamage: 0);

        var body = EnemyHoverShowPatch.BuildEnemyBodyBBCode(agg);

        Assert.Contains("Damage attempted", body);
        Assert.Contains("Damage dealt", body);
        Assert.Contains("Damage blocked", body);
        Assert.Contains("Damage instances", body);
        Assert.Contains("[b]13[/b]", body);
        Assert.Contains("[b]7[/b]", body);
        Assert.Contains("[b]6[/b]", body);
        Assert.Contains("54%", body);
    }

    [Fact]
    public void EnemyAggregate_RecordStatusCards_SplitsByPileAndCard()
    {
        var agg = new EnemyAggregate();

        RunTracker.RecordEnemyStatusCardAddedForTest(agg, "CARD.DAZED", "Dazed", PileType.Draw);
        RunTracker.RecordEnemyStatusCardAddedForTest(agg, "CARD.DAZED", "Dazed", PileType.Hand);
        RunTracker.RecordEnemyStatusCardAddedForTest(agg, "CARD.BURN", "Burn", PileType.Discard);

        Assert.Equal(3, agg.StatusCardsAdded);
        Assert.Equal(1, agg.StatusCardsAddedToDraw);
        Assert.Equal(1, agg.StatusCardsAddedToHand);
        Assert.Equal(1, agg.StatusCardsAddedToDiscard);
        Assert.Equal(2, agg.StatusCardsById["CARD.DAZED"].Count);
        Assert.Equal(1, agg.StatusCardsById["CARD.BURN"].Count);
    }

    [Fact]
    public void EnemyTooltip_StatusFields_ShowPileAndCardBreakdown()
    {
        var agg = new EnemyAggregate();
        RunTracker.RecordEnemyStatusCardAddedForTest(agg, "CARD.DAZED", "Dazed", PileType.Draw);
        RunTracker.RecordEnemyStatusCardAddedForTest(agg, "CARD.DAZED", "Dazed", PileType.Draw);
        RunTracker.RecordEnemyStatusCardAddedForTest(agg, "CARD.BURN", "Burn", PileType.Hand);

        var body = EnemyHoverShowPatch.BuildEnemyBodyBBCode(agg);

        Assert.Contains("Status cards added", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("added to draw pile", body);
        Assert.Contains("added to hand", body);
        Assert.Contains("Dazed", body);
        Assert.Contains("Burn", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]1[/b]", body);
    }

    [Fact]
    public void RunData_OlderShapeWithoutEnemyAggregates_DeserializesWithEmptyDictionary()
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
              "def_counters": {}
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        Assert.Empty(run!.EnemyAggregates);
    }
}
