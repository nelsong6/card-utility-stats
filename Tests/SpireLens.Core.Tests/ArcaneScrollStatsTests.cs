using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class ArcaneScrollStatsTests
{
    private const string ArcaneScrollRelicId = "RELIC.ARCANE_SCROLL";

    private static readonly MethodInfo BuildArcaneScrollBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildArcaneScrollBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildArcaneScrollBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_ArcaneScrollFields_DefaultToEmpty()
    {
        var agg = new RelicAggregate();

        Assert.Empty(agg.CardsGranted);
    }

    [Fact]
    public void RelicAggregate_ArcaneScrollFields_JsonRoundtrip_PreservesGrantedRare()
    {
        var run = new RunData();
        run.RelicAggregates[ArcaneScrollRelicId] = new RelicAggregate
        {
            CardsGranted =
            {
                ["CARD.ADRENALINE"] = new RelicCardAggregate
                {
                    CardId = "CARD.ADRENALINE",
                    DisplayName = "Adrenaline",
                    Count = 1,
                },
            },
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("cards_granted", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[ArcaneScrollRelicId];
        Assert.Equal(1, agg.CardsGranted["CARD.ADRENALINE"].Count);
        Assert.Equal("Adrenaline", agg.CardsGranted["CARD.ADRENALINE"].DisplayName);
    }

    [Fact]
    public void RunTracker_ArcaneScrollHelper_RecordsGrantedRare()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordArcaneScrollRareReceivedForTest(agg, "CARD.ADRENALINE", "Adrenaline");
        RunTracker.RecordArcaneScrollRareReceivedForTest(agg, null, null);

        Assert.Single(agg.CardsGranted);
        Assert.Equal(1, agg.CardsGranted["CARD.ADRENALINE"].Count);
        Assert.Equal("Adrenaline", agg.CardsGranted["CARD.ADRENALINE"].DisplayName);
    }

    [Fact]
    public void MergeRelicAggregateInto_ArcaneScrollFields_MergesGrantedRare()
    {
        var target = new RelicAggregate();
        var source = new RelicAggregate();
        source.CardsGranted["CARD.ADRENALINE"] = new RelicCardAggregate
        {
            CardId = "CARD.ADRENALINE",
            DisplayName = "Adrenaline",
            Count = 1,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(1, target.CardsGranted["CARD.ADRENALINE"].Count);
        Assert.Equal("Adrenaline", target.CardsGranted["CARD.ADRENALINE"].DisplayName);
    }

    [Fact]
    public void RelicTooltip_ArcaneScroll_ShowsRareReceived()
    {
        var agg = new RelicAggregate
        {
            CardsGranted =
            {
                ["CARD.ADRENALINE"] = new RelicCardAggregate
                {
                    CardId = "CARD.ADRENALINE",
                    DisplayName = "Adrenaline",
                    Count = 1,
                },
            },
        };

        var body = BuildBody(agg);

        Assert.Contains("Rare received", body);
        Assert.Contains("Adrenaline", body);
    }

    [Fact]
    public void RelicTooltip_ArcaneScroll_ShowsZeroValue()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Rare received", body);
        Assert.Contains("[b]0[/b]", body);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildArcaneScrollBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildArcaneScrollBodyBBCode returned null."));
}
