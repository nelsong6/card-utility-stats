using System;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Entities.Cards;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class ReplayStatsTests
{
    private static readonly MethodInfo AppendReplayStatsMethod =
        typeof(CardHoverShowPatch).GetMethod("AppendReplayStats", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("AppendReplayStats not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void CardAggregate_ReplayExtraPlays_DefaultsToZero()
    {
        var agg = new CardAggregate();

        Assert.Equal(0, agg.TimesReplayExtraPlayed);
        Assert.Empty(agg.ReplayExtraPlayReasons);
    }

    [Fact]
    public void CardAggregate_ReplayExtraPlays_JsonRoundtrip_PreservesField()
    {
        var run = new RunData();
        run.Aggregates["CARD.STRIKE_KIN#1"] = new CardAggregate
        {
            Plays = 5,
            TimesReplayExtraPlayed = 2,
        };
        run.Aggregates["CARD.STRIKE_KIN#1"].ReplayExtraPlayReasons["power:POWER.BURST"] =
            new ReplayExtraPlayReasonAggregate
            {
                ReasonId = "power:POWER.BURST",
                DisplayName = "Burst",
                Count = 2,
            };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("times_replay_extra_played", json);
        Assert.Contains("replay_extra_play_reasons", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.Aggregates["CARD.STRIKE_KIN#1"];
        Assert.Equal(5, agg.Plays);
        Assert.Equal(2, agg.TimesReplayExtraPlayed);
        Assert.Equal(2, agg.ReplayExtraPlayReasons["power:POWER.BURST"].Count);
        Assert.Equal("Burst", agg.ReplayExtraPlayReasons["power:POWER.BURST"].DisplayName);
    }

    [Fact]
    public void IsReplayExtraPlay_UsesNonzeroPlayIndex()
    {
        Assert.False(RunTracker.IsReplayExtraPlay(CreateCardPlay(playIndex: 0, playCount: 2)));
        Assert.True(RunTracker.IsReplayExtraPlay(CreateCardPlay(playIndex: 1, playCount: 2)));
    }

    [Fact]
    public void Tooltip_ReplayStats_ShowsExtraPlays()
    {
        var sb = new StringBuilder();
        var agg = new CardAggregate { TimesReplayExtraPlayed = 2 };
        agg.ReplayExtraPlayReasons["replay"] = new ReplayExtraPlayReasonAggregate
        {
            ReasonId = "replay",
            DisplayName = "Replay",
            Count = 1,
        };
        agg.ReplayExtraPlayReasons["power:POWER.BURST"] = new ReplayExtraPlayReasonAggregate
        {
            ReasonId = "power:POWER.BURST",
            DisplayName = "Burst",
            Count = 1,
        };

        _ = AppendReplayStatsMethod.Invoke(null, new object?[] { sb, agg });
        var body = sb.ToString();

        Assert.Contains("Replay extra plays", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("Replay from Replay", body);
        Assert.Contains("Replay from Burst", body);
    }

    [Fact]
    public void Tooltip_ReplayStats_HidesWhenZero()
    {
        var sb = new StringBuilder();

        _ = AppendReplayStatsMethod.Invoke(null, new object?[] { sb, new CardAggregate() });

        Assert.Equal("", sb.ToString());
    }

    private static CardPlay CreateCardPlay(int playIndex, int playCount)
    {
        var cardPlay = (CardPlay)Activator.CreateInstance(typeof(CardPlay), nonPublic: true)!;
        typeof(CardPlay).GetProperty(nameof(CardPlay.PlayIndex))!.SetValue(cardPlay, playIndex);
        typeof(CardPlay).GetProperty(nameof(CardPlay.PlayCount))!.SetValue(cardPlay, playCount);
        return cardPlay;
    }
}
