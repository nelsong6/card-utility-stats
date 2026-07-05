using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class HeftyTabletStatsTests
{
    private const string HeftyTabletRelicId = "RELIC.HEFTY_TABLET";

    private static readonly MethodInfo BuildHeftyTabletBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildHeftyTabletBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildHeftyTabletBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_HeftyTabletFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Empty(agg.CardsGranted);
        Assert.Equal(0, agg.CardChoicesSkipped);
    }

    [Fact]
    public void RelicAggregate_HeftyTabletFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[HeftyTabletRelicId] = new RelicAggregate
        {
            CardChoicesSkipped = 1,
            CardsGranted =
            {
                ["CARD.ADRENALINE"] = new RelicCardAggregate
                {
                    CardId = "CARD.ADRENALINE",
                    DisplayName = "Adrenaline",
                    Count = 2,
                },
            },
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("cards_granted", json);
        Assert.Contains("card_choices_skipped", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[HeftyTabletRelicId];
        Assert.Equal(1, agg.CardChoicesSkipped);
        Assert.Equal(2, agg.CardsGranted["CARD.ADRENALINE"].Count);
        Assert.Equal("Adrenaline", agg.CardsGranted["CARD.ADRENALINE"].DisplayName);
    }

    [Fact]
    public void RunTracker_HeftyTabletHelper_RecordsGrantedCardsAndSkips()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordHeftyTabletChoiceForTest(agg, "CARD.ADRENALINE", "Adrenaline");
        RunTracker.RecordHeftyTabletChoiceForTest(agg, "CARD.ADRENALINE", "Adrenaline");
        RunTracker.RecordHeftyTabletChoiceForTest(agg, null, null);

        Assert.Equal(2, agg.CardsGranted["CARD.ADRENALINE"].Count);
        Assert.Equal("Adrenaline", agg.CardsGranted["CARD.ADRENALINE"].DisplayName);
        Assert.Equal(1, agg.CardChoicesSkipped);
    }

    [Fact]
    public void MergeRelicAggregateInto_HeftyTabletFields_MergesGrantedCardsAndSkips()
    {
        var target = new RelicAggregate();
        var source = new RelicAggregate { CardChoicesSkipped = 1 };
        source.CardsGranted["CARD.ADRENALINE"] = new RelicCardAggregate
        {
            CardId = "CARD.ADRENALINE",
            DisplayName = "Adrenaline",
            Count = 2,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(1, target.CardChoicesSkipped);
        Assert.Equal(2, target.CardsGranted["CARD.ADRENALINE"].Count);
        Assert.Equal("Adrenaline", target.CardsGranted["CARD.ADRENALINE"].DisplayName);
    }

    [Fact]
    public void RelicTooltip_HeftyTablet_ShowsGrantedCardAndSkippedRows()
    {
        var agg = new RelicAggregate
        {
            CardChoicesSkipped = 1,
            CardsGranted =
            {
                ["CARD.ADRENALINE"] = new RelicCardAggregate
                {
                    CardId = "CARD.ADRENALINE",
                    DisplayName = "Adrenaline",
                    Count = 2,
                },
            },
        };

        var body = BuildBody(agg);

        Assert.Contains("Cards granted", body);
        Assert.Contains("Skipped", body);
        Assert.Contains("Granted", body);
        Assert.Contains("Adrenaline x2", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]1[/b]", body);
    }

    [Fact]
    public void RelicTooltip_HeftyTablet_ShowsZeroValues()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Cards granted", body);
        Assert.Contains("Skipped", body);
        Assert.Contains("[b]0[/b]", body);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildHeftyTabletBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildHeftyTabletBodyBBCode returned null."));
}
