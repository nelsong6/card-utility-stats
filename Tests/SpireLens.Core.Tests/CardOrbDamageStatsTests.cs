using System.Text.Json;
using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

public class CardOrbDamageStatsTests
{
    [Fact]
    public void CardOrbAggregate_DamageFields_DefaultToZero()
    {
        var outcome = new CardOrbAggregate();

        Assert.Equal(0, outcome.DamageAttempted);
        Assert.Equal(0, outcome.DamageDealt);
        Assert.Equal(0, outcome.DamageBlocked);
        Assert.Equal(0, outcome.DamageOverkill);
        Assert.Equal(0, outcome.Kills);
        Assert.Equal(0, outcome.TargetsHit);
    }

    [Fact]
    public void RecordCardSourcedOrbDamage_StoresObservedSplitOutsideDirectCardDamage()
    {
        var aggregate = new CardAggregate();

        var outcome = RunTracker.RecordCardSourcedOrbDamageForTest(
            aggregate,
            "ORB.LIGHTNING",
            [
                (
                    BlockedDamage: 2,
                    UnblockedDamage: 3,
                    OverkillDamage: 0,
                    WasTargetKilled: false),
                (
                    BlockedDamage: 0,
                    UnblockedDamage: 4,
                    OverkillDamage: 1,
                    WasTargetKilled: true),
            ]);

        Assert.Equal("ORB.LIGHTNING", outcome.OrbId);
        Assert.Equal(10, outcome.DamageAttempted);
        Assert.Equal(7, outcome.DamageDealt);
        Assert.Equal(2, outcome.DamageBlocked);
        Assert.Equal(1, outcome.DamageOverkill);
        Assert.Equal(1, outcome.Kills);
        Assert.Equal(2, outcome.TargetsHit);
        Assert.Equal(0, aggregate.TotalIntended);
        Assert.Equal(0, aggregate.TotalEffective);
        Assert.Equal(0, aggregate.TotalBlocked);
        Assert.Equal(0, aggregate.TotalOverkill);
        Assert.Equal(0, aggregate.Kills);
    }

    [Fact]
    public void CardOrbAggregate_DamageFields_RoundTrip()
    {
        var aggregate = new CardAggregate();
        aggregate.OrbOutcomes["ORB.LIGHTNING"] = new CardOrbAggregate
        {
            OrbId = "ORB.LIGHTNING",
            Created = 2,
            DamageAttempted = 18,
            DamageDealt = 12,
            DamageBlocked = 4,
            DamageOverkill = 2,
            Kills = 1,
            TargetsHit = 4,
        };

        var json = JsonSerializer.Serialize(aggregate, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<CardAggregate>(
            json,
            RunStorage.Options);

        Assert.Contains("\"damage_attempted\"", json);
        Assert.Contains("\"damage_dealt\"", json);
        Assert.Contains("\"damage_blocked\"", json);
        Assert.Contains("\"damage_overkill\"", json);
        Assert.Contains("\"kills\"", json);
        Assert.Contains("\"targets_hit\"", json);
        Assert.NotNull(restored);
        var outcome = restored!.OrbOutcomes["ORB.LIGHTNING"];
        Assert.Equal(18, outcome.DamageAttempted);
        Assert.Equal(12, outcome.DamageDealt);
        Assert.Equal(4, outcome.DamageBlocked);
        Assert.Equal(2, outcome.DamageOverkill);
        Assert.Equal(1, outcome.Kills);
        Assert.Equal(4, outcome.TargetsHit);
    }

    [Fact]
    public void OlderCardOrbShape_DefaultsDamageFieldsToZero()
    {
        var outcome = JsonSerializer.Deserialize<CardOrbAggregate>(
            """
            {
              "orb_id": "ORB.LIGHTNING",
              "created": 1,
              "passive_activations": 2,
              "evokes": 1
            }
            """,
            RunStorage.Options);

        Assert.NotNull(outcome);
        Assert.Equal(0, outcome!.DamageAttempted);
        Assert.Equal(0, outcome.DamageDealt);
        Assert.Equal(0, outcome.DamageBlocked);
        Assert.Equal(0, outcome.DamageOverkill);
        Assert.Equal(0, outcome.Kills);
        Assert.Equal(0, outcome.TargetsHit);
    }
}
