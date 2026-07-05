using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Entities.Cards;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class BookmarkStatsTests
{
    private const string BookmarkRelicId = "RELIC.BOOKMARK";

    private static readonly MethodInfo BuildBookmarkBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildBookmarkBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildBookmarkBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_BookmarkFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.BookmarkCombats);
        Assert.Equal(0, agg.BookmarkCommonActivations);
        Assert.Equal(0, agg.BookmarkUncommonActivations);
        Assert.Equal(0, agg.BookmarkRareActivations);
    }

    [Fact]
    public void RelicAggregate_BookmarkFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[BookmarkRelicId] = new RelicAggregate
        {
            Activations = 7,
            BookmarkCombats = 4,
            BookmarkCommonActivations = 2,
            BookmarkUncommonActivations = 3,
            BookmarkRareActivations = 2,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("bookmark_combats", json);
        Assert.Contains("bookmark_common_activations", json);
        Assert.Contains("bookmark_uncommon_activations", json);
        Assert.Contains("bookmark_rare_activations", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[BookmarkRelicId];
        Assert.Equal(7, restoredAgg.Activations);
        Assert.Equal(4, restoredAgg.BookmarkCombats);
        Assert.Equal(2, restoredAgg.BookmarkCommonActivations);
        Assert.Equal(3, restoredAgg.BookmarkUncommonActivations);
        Assert.Equal(2, restoredAgg.BookmarkRareActivations);
    }

    [Fact]
    public void RunTracker_BookmarkHelpers_AccumulateCombatsAndRarityActivations()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordBookmarkCombatForTest(agg, 4);
        RunTracker.RecordBookmarkCombatForTest(agg, -1);
        RunTracker.RecordBookmarkActivationForTest(agg, CardRarity.Common);
        RunTracker.RecordBookmarkActivationForTest(agg, CardRarity.Uncommon);
        RunTracker.RecordBookmarkActivationForTest(agg, CardRarity.Uncommon);
        RunTracker.RecordBookmarkActivationForTest(agg, CardRarity.Rare);

        Assert.Equal(4, agg.BookmarkCombats);
        Assert.Equal(4, agg.Activations);
        Assert.Equal(1, agg.BookmarkCommonActivations);
        Assert.Equal(2, agg.BookmarkUncommonActivations);
        Assert.Equal(1, agg.BookmarkRareActivations);
    }

    [Fact]
    public void RelicTooltip_Bookmark_ShowsCountsAndAverage()
    {
        var agg = new RelicAggregate
        {
            Activations = 7,
            BookmarkCombats = 4,
            BookmarkCommonActivations = 2,
            BookmarkUncommonActivations = 3,
            BookmarkRareActivations = 2,
        };

        var body = (string)(BuildBookmarkBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildBookmarkBodyBBCode returned null."));

        Assert.Contains("Activations", body);
        Assert.Contains("common activations", body);
        Assert.Contains("uncommon activations", body);
        Assert.Contains("rare activations", body);
        Assert.Contains("Combats held", body);
        Assert.Contains("Avg activations per combat", body);
        Assert.Contains("[b]7[/b]", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.Contains("[b]1.75[/b]", body);
    }

    [Fact]
    public void RelicTooltip_Bookmark_ShowsZeroRows()
    {
        var body = (string)(BuildBookmarkBodyMethod.Invoke(null, new object?[] { new RelicAggregate() })
            ?? throw new InvalidOperationException("BuildBookmarkBodyBBCode returned null."));

        Assert.Contains("Activations", body);
        Assert.Contains("common activations", body);
        Assert.Contains("uncommon activations", body);
        Assert.Contains("rare activations", body);
        Assert.Contains("Avg activations per combat", body);
        Assert.Contains("[b]0[/b]", body);
    }
}
