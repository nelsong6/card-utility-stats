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

public class MiniatureCannonStatsTests
{
    private const string MiniatureCannonRelicId = "RELIC.MINIATURE_CANNON";

    private static readonly MethodInfo BuildMiniatureCannonBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildMiniatureCannonBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildMiniatureCannonBodyBBCode not found.");

    private static readonly MethodInfo IsMiniatureCannonStatsRelicModelMethod =
        typeof(RelicHoverShowPatch).GetMethod("IsMiniatureCannonStatsRelicModel", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("IsMiniatureCannonStatsRelicModel not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_MiniatureCannonFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.MiniatureCannonUpgradedAttacksInDeck);
        Assert.Equal(0, agg.MiniatureCannonUpgradedAttackPlays);
        Assert.Equal(0, agg.MiniatureCannonUpgradedAttackHits);
    }

    [Fact]
    public void RelicAggregate_MiniatureCannonFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[MiniatureCannonRelicId] = new RelicAggregate
        {
            Activations = 4,
            MiniatureCannonUpgradedAttacksInDeck = 3,
            MiniatureCannonUpgradedAttackPlays = 10,
            MiniatureCannonUpgradedAttackHits = 17,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("miniature_cannon_upgraded_attacks_in_deck", json);
        Assert.Contains("miniature_cannon_upgraded_attack_plays", json);
        Assert.Contains("miniature_cannon_upgraded_attack_hits", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[MiniatureCannonRelicId];
        Assert.Equal(4, restoredAgg.Activations);
        Assert.Equal(3, restoredAgg.MiniatureCannonUpgradedAttacksInDeck);
        Assert.Equal(10, restoredAgg.MiniatureCannonUpgradedAttackPlays);
        Assert.Equal(17, restoredAgg.MiniatureCannonUpgradedAttackHits);
    }

    [Fact]
    public void RunTracker_MiniatureCannonHelpers_AccumulateAndClamp()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordMiniatureCannonUpgradedAttackPlayedForTest(agg, 2);
        RunTracker.RecordMiniatureCannonUpgradedAttackHitForTest(agg, 5);
        RunTracker.SetMiniatureCannonDeckCountForTest(agg, 3);
        RunTracker.RecordMiniatureCannonUpgradedAttackPlayedForTest(agg, -1);
        RunTracker.RecordMiniatureCannonUpgradedAttackHitForTest(agg, -2);
        RunTracker.SetMiniatureCannonDeckCountForTest(agg, -3);

        Assert.Equal(2, agg.MiniatureCannonUpgradedAttackPlays);
        Assert.Equal(5, agg.MiniatureCannonUpgradedAttackHits);
        Assert.Equal(0, agg.MiniatureCannonUpgradedAttacksInDeck);
    }

    [Fact]
    public void RunTracker_MiniatureCannonStatsRelic_RecognizesRelic()
    {
        Assert.True(RunTracker.IsMiniatureCannonStatsRelic(Uninitialized<MiniatureCannon>()));
        Assert.False(RunTracker.IsMiniatureCannonStatsRelic(null));
        Assert.False(RunTracker.IsMiniatureCannonStatsRelic(Uninitialized<StrikeDummy>()));
    }

    [Fact]
    public void RelicTooltip_MiniatureCannonModelRecognition_RecognizesRelic()
    {
        var recognized = (bool)(IsMiniatureCannonStatsRelicModelMethod.Invoke(null, new object[] { Uninitialized<MiniatureCannon>() })
            ?? throw new InvalidOperationException("IsMiniatureCannonStatsRelicModel returned null."));

        Assert.True(recognized);
    }

    [Fact]
    public void RelicTooltip_MiniatureCannon_ShowsCountsAndAverages()
    {
        var agg = new RelicAggregate
        {
            Activations = 4,
            MiniatureCannonUpgradedAttacksInDeck = 3,
            MiniatureCannonUpgradedAttackPlays = 10,
            MiniatureCannonUpgradedAttackHits = 17,
        };

        var body = (string)(BuildMiniatureCannonBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildMiniatureCannonBodyBBCode returned null."));

        Assert.Contains("Combats held", body);
        Assert.Contains("Upgraded attacks in deck", body);
        Assert.Contains("Upgraded attack plays", body);
        Assert.Contains("Upgraded attack hits", body);
        Assert.Contains("Avg plays per combat", body);
        Assert.Contains("Avg hits per combat", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[b]10[/b]", body);
        Assert.Contains("[b]17[/b]", body);
        Assert.Contains("[b]2.5[/b]", body);
        Assert.Contains("[b]4.25[/b]", body);
    }

    [Fact]
    public void RelicTooltip_MiniatureCannon_ShowsZeroRows()
    {
        var body = (string)(BuildMiniatureCannonBodyMethod.Invoke(null, new object?[] { new RelicAggregate() })
            ?? throw new InvalidOperationException("BuildMiniatureCannonBodyBBCode returned null."));

        Assert.Contains("Upgraded attacks in deck", body);
        Assert.Contains("Upgraded attack plays", body);
        Assert.Contains("Upgraded attack hits", body);
        Assert.Contains("Avg plays per combat", body);
        Assert.Contains("Avg hits per combat", body);
        Assert.Contains("[b]0[/b]", body);
    }

    private static T Uninitialized<T>() where T : class
    {
        return (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    }
}
