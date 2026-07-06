using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class CursedPearlStatsTests
{
    private const string CursedPearlRelicId = "RELIC.CURSED_PEARL";
    private const string GreedCardId = "CARD.GREED#1";

    private static readonly MethodInfo BuildCursedPearlBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildCursedPearlBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildCursedPearlBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_CursedPearlFields_DefaultToUnsetAndZero()
    {
        var relicAgg = new RelicAggregate();
        var curseAgg = new CardAggregate();

        Assert.Null(relicAgg.FloorsAscendedBeforeFirstShop);
        Assert.Equal(0, curseAgg.CombatsInDeck);
        Assert.Equal(0, curseAgg.TimesDrawn);
        Assert.Equal(0, curseAgg.TimesDiscarded);
        Assert.Equal(0, curseAgg.Plays);
        Assert.Equal(0, curseAgg.TimesExhausted);
    }

    [Fact]
    public void RelicAggregate_CursedPearlAndGreedStats_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[CursedPearlRelicId] = new RelicAggregate
        {
            FloorsAscendedBeforeFirstShop = 6,
        };
        run.Aggregates[GreedCardId] = new CardAggregate
        {
            CombatsInDeck = 4,
            TimesDrawn = 8,
            TimesDiscarded = 3,
            Plays = 1,
            TimesExhausted = 2,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("floors_ascended_before_first_shop", json);
        Assert.Contains(GreedCardId, json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var relicAgg = restored!.RelicAggregates[CursedPearlRelicId];
        var curseAgg = restored.Aggregates[GreedCardId];
        Assert.Equal(6, relicAgg.FloorsAscendedBeforeFirstShop);
        Assert.Equal(4, curseAgg.CombatsInDeck);
        Assert.Equal(8, curseAgg.TimesDrawn);
        Assert.Equal(3, curseAgg.TimesDiscarded);
        Assert.Equal(1, curseAgg.Plays);
        Assert.Equal(2, curseAgg.TimesExhausted);
    }

    [Fact]
    public void RunTracker_CursedPearlFirstShopHelper_RecordsFirstValueOnlyAndClamps()
    {
        var agg = new RelicAggregate();

        Assert.True(RunTracker.RecordCursedPearlFloorsBeforeFirstShopForTest(agg, -2));
        Assert.Equal(0, agg.FloorsAscendedBeforeFirstShop);
        Assert.False(RunTracker.RecordCursedPearlFloorsBeforeFirstShopForTest(agg, 7));
        Assert.Equal(0, agg.FloorsAscendedBeforeFirstShop);

        var laterShopAgg = new RelicAggregate();
        Assert.True(RunTracker.RecordCursedPearlFloorsBeforeFirstShopForTest(laterShopAgg, 5));
        Assert.False(RunTracker.RecordCursedPearlFloorsBeforeFirstShopForTest(laterShopAgg, 9));
        Assert.Equal(5, laterShopAgg.FloorsAscendedBeforeFirstShop);
    }

    [Fact]
    public void RelicTooltip_CursedPearl_ShowsZeroRowsWithoutStats()
    {
        var body = BuildBody(new RelicAggregate(), new CardAggregate());

        Assert.Contains("Floors ascended before first shop", body);
        Assert.Contains("Greed combats", body);
        Assert.Contains("Greed drawn", body);
        Assert.Contains("Greed discarded", body);
        Assert.Contains("Greed played", body);
        Assert.Contains("Greed exhausted", body);
        Assert.Contains("[b]0[/b]", body);
    }

    [Fact]
    public void RelicTooltip_CursedPearl_ShowsFirstShopAndGreedStats()
    {
        var body = BuildBody(
            new RelicAggregate
            {
                FloorsAscendedBeforeFirstShop = 6,
            },
            new CardAggregate
            {
                CombatsInDeck = 4,
                TimesDrawn = 8,
                TimesDiscarded = 3,
                Plays = 1,
                TimesExhausted = 2,
            });

        Assert.Contains("Floors ascended before first shop", body);
        Assert.Contains("Greed combats", body);
        Assert.Contains("[b]6[/b]", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.Contains("[b]8[/b]", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[b]1[/b]", body);
        Assert.Contains("[b]2[/b]", body);
    }

    private static string BuildBody(RelicAggregate relicAgg, CardAggregate curseAgg)
        => (string)(BuildCursedPearlBodyMethod.Invoke(null, new object?[] { relicAgg, curseAgg })
            ?? throw new InvalidOperationException("BuildCursedPearlBodyBBCode returned null."));
}
