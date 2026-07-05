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

public class StrikeDummyStatsTests
{
    private const string StrikeDummyRelicId = "RELIC.STRIKE_DUMMY";

    private static readonly MethodInfo BuildStrikeDummyBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildStrikeDummyBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildStrikeDummyBodyBBCode not found.");

    private static readonly MethodInfo IsStrikeDummyStatsRelicModelMethod =
        typeof(RelicHoverShowPatch).GetMethod("IsStrikeDummyStatsRelicModel", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("IsStrikeDummyStatsRelicModel not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_StrikeDummyFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.StrikeDummyStrikesPlayed);
        Assert.Equal(0, agg.StrikeDummyBaseStrikesInDeck);
        Assert.Equal(0, agg.StrikeDummyNonBaseStrikeCardsInDeck);
    }

    [Fact]
    public void RelicAggregate_StrikeDummyFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[StrikeDummyRelicId] = new RelicAggregate
        {
            StrikeDummyStrikesPlayed = 8,
            StrikeDummyBaseStrikesInDeck = 4,
            StrikeDummyNonBaseStrikeCardsInDeck = 3,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("strike_dummy_strikes_played", json);
        Assert.Contains("strike_dummy_base_strikes_in_deck", json);
        Assert.Contains("strike_dummy_non_base_strike_cards_in_deck", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[StrikeDummyRelicId];
        Assert.Equal(8, restoredAgg.StrikeDummyStrikesPlayed);
        Assert.Equal(4, restoredAgg.StrikeDummyBaseStrikesInDeck);
        Assert.Equal(3, restoredAgg.StrikeDummyNonBaseStrikeCardsInDeck);
    }

    [Fact]
    public void RunTracker_StrikeDummyHelpers_AccumulatePlaysAndClampDeckCounts()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordStrikeDummyStrikePlayedForTest(agg);
        RunTracker.RecordStrikeDummyStrikePlayedForTest(agg);
        RunTracker.SetStrikeDummyDeckCountsForTest(agg, 4, 3);
        RunTracker.SetStrikeDummyDeckCountsForTest(agg, -1, -2);

        Assert.Equal(2, agg.StrikeDummyStrikesPlayed);
        Assert.Equal(0, agg.StrikeDummyBaseStrikesInDeck);
        Assert.Equal(0, agg.StrikeDummyNonBaseStrikeCardsInDeck);
    }

    [Fact]
    public void RunTracker_StrikeDummyStatsRelic_IncludesFakeStrikeDummy()
    {
        Assert.True(RunTracker.IsStrikeDummyStatsRelic(Uninitialized<StrikeDummy>()));
        Assert.True(RunTracker.IsStrikeDummyStatsRelic(Uninitialized<FakeStrikeDummy>()));
        Assert.False(RunTracker.IsStrikeDummyStatsRelic(null));
    }

    [Fact]
    public void RelicTooltip_StrikeDummyModelRecognition_IncludesFakeStrikeDummy()
    {
        var real = (bool)(IsStrikeDummyStatsRelicModelMethod.Invoke(null, new object[] { Uninitialized<StrikeDummy>() })
            ?? throw new InvalidOperationException("IsStrikeDummyStatsRelicModel returned null."));
        var fake = (bool)(IsStrikeDummyStatsRelicModelMethod.Invoke(null, new object[] { Uninitialized<FakeStrikeDummy>() })
            ?? throw new InvalidOperationException("IsStrikeDummyStatsRelicModel returned null."));

        Assert.True(real);
        Assert.True(fake);
    }

    [Fact]
    public void RelicTooltip_StrikeDummy_ShowsPlayAndDeckRows()
    {
        var agg = new RelicAggregate
        {
            StrikeDummyStrikesPlayed = 8,
            StrikeDummyBaseStrikesInDeck = 4,
            StrikeDummyNonBaseStrikeCardsInDeck = 3,
        };

        var body = (string)(BuildStrikeDummyBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildStrikeDummyBodyBBCode returned null."));

        Assert.Contains("Strikes played", body);
        Assert.Contains("Base Strikes in deck", body);
        Assert.Contains("Non-base Strike cards in deck", body);
        Assert.Contains("[b]8[/b]", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.Contains("[b]3[/b]", body);
    }

    private static T Uninitialized<T>() where T : class
    {
        return (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    }
}
