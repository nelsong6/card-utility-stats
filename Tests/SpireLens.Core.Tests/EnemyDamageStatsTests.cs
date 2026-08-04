using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class EnemyDamageStatsTests
{
    private static readonly MethodInfo BuildEnemyDamageBodyMethod =
        typeof(EnemyHoverShowPatch).GetMethod("BuildEnemyDamageBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildEnemyDamageBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void EnemyAggregate_DefaultsToZero()
    {
        var agg = new EnemyAggregate();

        Assert.Equal(0, agg.DamageAttempted);
        Assert.Equal(0, agg.DamageDealt);
        Assert.Equal(0, agg.DamageBlocked);
    }

    [Fact]
    public void EnemyAggregate_JsonRoundtrip_PreservesDamageFields()
    {
        var run = new RunData();
        run.EnemyAggregates["MONSTER.JAW_WORM"] = new EnemyAggregate
        {
            EnemyId = "MONSTER.JAW_WORM",
            DisplayName = "MONSTER.JAW_WORM",
            DamageAttempted = 20,
            DamageDealt = 12,
            DamageBlocked = 8,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("enemy_aggregates", json);
        Assert.Contains("damage_attempted", json);
        Assert.Contains("damage_dealt", json);
        Assert.Contains("damage_blocked", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.EnemyAggregates["MONSTER.JAW_WORM"];
        Assert.Equal(20, agg.DamageAttempted);
        Assert.Equal(12, agg.DamageDealt);
        Assert.Equal(8, agg.DamageBlocked);
    }

    [Fact]
    public void EnemyTooltip_ShowsAttemptedDealtAndBlockedDamage()
    {
        var body = (string)(BuildEnemyDamageBodyMethod.Invoke(null, new object?[]
        {
            new EnemyAggregate
            {
                DamageAttempted = 20,
                DamageDealt = 12,
                DamageBlocked = 8,
            },
        }) ?? throw new InvalidOperationException("BuildEnemyDamageBodyBBCode returned null."));

        Assert.Contains("Damage attempted", body);
        Assert.Contains("Damage dealt", body);
        Assert.Contains("Damage blocked", body);
        Assert.Contains("[b]20[/b]", body);
        Assert.Contains("[b]12[/b]", body);
        Assert.Contains("[b]8[/b]", body);
        Assert.Equal(1, body.Split("[table=4]", StringSplitOptions.None).Length - 1);
        Assert.Contains("[left][b]20[/b][/left]", body);
        Assert.DoesNotContain("[right]", body);
    }

    [Fact]
    public void EnemyTooltip_ShowsZeroDamageRowsBeforeEnemyHasAttacked()
    {
        var body = (string)(BuildEnemyDamageBodyMethod.Invoke(null, new object?[]
        {
            new EnemyAggregate(),
        }) ?? throw new InvalidOperationException("BuildEnemyDamageBodyBBCode returned null."));

        Assert.Contains("Damage attempted", body);
        Assert.Contains("Damage dealt", body);
        Assert.Contains("Damage blocked", body);
        Assert.Contains("Damage instances", body);
        Assert.Contains("[b]0[/b]", body);
    }

    [Fact]
    public void RunData_OlderShapeWithoutEnemyAggregates_DeserializesWithEmptyDefault()
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
