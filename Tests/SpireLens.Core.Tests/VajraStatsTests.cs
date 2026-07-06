using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class VajraStatsTests
{
    private const string VajraRelicId = "RELIC.VAJRA";

    private static readonly MethodInfo BuildVajraBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildVajraBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildVajraBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_VajraFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.VajraAttacksPlayed);
        Assert.Equal(0, agg.VajraAttackHits);
    }

    [Fact]
    public void RelicAggregate_VajraFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[VajraRelicId] = new RelicAggregate
        {
            VajraAttacksPlayed = 6,
            VajraAttackHits = 11,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("vajra_attacks_played", json);
        Assert.Contains("vajra_attack_hits", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[VajraRelicId];
        Assert.Equal(6, restoredAgg.VajraAttacksPlayed);
        Assert.Equal(11, restoredAgg.VajraAttackHits);
    }

    [Fact]
    public void RunTracker_VajraHelpers_AccumulateAndClamp()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordVajraAttackPlayedForTest(agg, 6);
        RunTracker.RecordVajraAttackHitForTest(agg, 11);
        RunTracker.RecordVajraAttackPlayedForTest(agg, -2);
        RunTracker.RecordVajraAttackHitForTest(agg, -3);

        Assert.Equal(6, agg.VajraAttacksPlayed);
        Assert.Equal(11, agg.VajraAttackHits);
    }

    [Fact]
    public void RelicTooltip_Vajra_ShowsAttackRows()
    {
        var body = BuildBody(new RelicAggregate
        {
            VajraAttacksPlayed = 6,
            VajraAttackHits = 11,
        });

        Assert.Contains("Attacks played", body);
        Assert.Contains("Attack hits", body);
        Assert.Contains("[b]6[/b]", body);
        Assert.Contains("[b]11[/b]", body);
    }

    [Fact]
    public void RelicTooltip_Vajra_ShowsZeroRows()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Attacks played", body);
        Assert.Contains("Attack hits", body);
        Assert.Contains("[b]0[/b]", body);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildVajraBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildVajraBodyBBCode returned null."));
}
