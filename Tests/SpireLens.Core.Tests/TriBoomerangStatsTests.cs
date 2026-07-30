using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class TriBoomerangStatsTests
{
    private const string TriBoomerangRelicId = "RELIC.TRI_BOOMERANG";

    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildTriBoomerangBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "BuildTriBoomerangBodyBBCode not found.");

    [Fact]
    public void Patch_TargetsTriBoomerangAfterObtained()
    {
        var target = typeof(TriBoomerang).GetMethod(
            nameof(TriBoomerang.AfterObtained),
            Type.EmptyTypes);

        Assert.NotNull(target);
    }

    [Fact]
    public void RelicAggregate_TriBoomerangFields_DefaultSafely()
    {
        var agg = new RelicAggregate();

        Assert.Empty(agg.TriBoomerangInstinctCards);
        Assert.Equal(0, agg.TriBoomerangInstinctCardPlays);
        Assert.Equal(0, agg.TriBoomerangCombats);
    }

    [Fact]
    public void RelicAggregate_TriBoomerangFields_JsonRoundtripPreservesValues()
    {
        var run = new RunData();
        run.RelicAggregates[TriBoomerangRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(
            json,
            RunStorage.Options);

        Assert.Contains("\"tri_boomerang_instinct_cards\"", json);
        Assert.Contains("\"card_instance_id\"", json);
        Assert.Contains("\"tri_boomerang_instinct_card_plays\"", json);
        Assert.Contains("\"tri_boomerang_combats\"", json);
        Assert.NotNull(restored);
        AssertRepresentativeAggregate(
            restored!.RelicAggregates[TriBoomerangRelicId]);
    }

    [Fact]
    public void RunTracker_TriBoomerangHelpers_DeduplicateByStableInstanceId()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordTriBoomerangInstinctCardsForTest(
            agg,
            new[]
            {
                Card("CARD.REAP#1", "Reap"),
                Card("CARD.GRAVE_WARDEN#1", "Grave Warden"),
                Card("CARD.REAP#1", "Renamed Reap"),
                Card("", "Missing identity"),
            });
        RunTracker.RecordTriBoomerangInstinctCardsForTest(
            agg,
            new[]
            {
                Card("CARD.SEVERANCE#2", "Severance+"),
            });
        RunTracker.RecordTriBoomerangInstinctCardPlayForTest(agg, 7);
        RunTracker.RecordTriBoomerangInstinctCardPlayForTest(agg, -2);
        RunTracker.RecordTriBoomerangCombatForTest(agg, 3);
        RunTracker.RecordTriBoomerangCombatForTest(agg, -1);

        AssertRepresentativeAggregate(agg);
    }

    [Fact]
    public void MergeRelicAggregateInto_TriBoomerangFields_UnionAndAccumulate()
    {
        var target = new RelicAggregate
        {
            TriBoomerangInstinctCards =
            {
                Card("CARD.REAP#1", "Reap"),
                Card("CARD.GRAVE_WARDEN#1", "Grave Warden"),
            },
            TriBoomerangInstinctCardPlays = 3,
            TriBoomerangCombats = 1,
        };
        var source = new RelicAggregate
        {
            TriBoomerangInstinctCards =
            {
                Card("CARD.REAP#1", "Renamed Reap"),
                Card("CARD.SEVERANCE#2", "Severance+"),
            },
            TriBoomerangInstinctCardPlays = 4,
            TriBoomerangCombats = 2,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        AssertRepresentativeAggregate(target);
    }

    [Fact]
    public void RelicTooltip_TriBoomerang_ShowsCardsPlaysAndCombatAverage()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("Cards enchanted with Instinct", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("Instinct-enchanted card", body);
        Assert.Contains("Reap", body);
        Assert.Contains("Grave Warden", body);
        Assert.Contains("Severance+", body);
        Assert.Contains("Times Instinct cards were played", body);
        Assert.Contains("[b]7[/b]", body);
        Assert.Contains("Avg Instinct-card plays per combat", body);
        Assert.Contains("[b]2.33[/b]", body);
    }

    [Fact]
    public void RelicTooltip_TriBoomerang_DispatchesForModel()
    {
        var relic = (TriBoomerang)RuntimeHelpers.GetUninitializedObject(
            typeof(TriBoomerang));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            PopulatedAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Tri-Boomerang", title);
        Assert.Contains("Cards enchanted with Instinct", body);
    }

    [Fact]
    public void RelicAggregate_OlderShapeWithoutTriBoomerangFields_DefaultsSafely()
    {
        var agg = JsonSerializer.Deserialize<RelicAggregate>(
            "{}",
            RunStorage.Options);

        Assert.NotNull(agg);
        Assert.Empty(agg!.TriBoomerangInstinctCards);
        Assert.Equal(0, agg.TriBoomerangInstinctCardPlays);
        Assert.Equal(0, agg.TriBoomerangCombats);
    }

    private static RelicAggregate PopulatedAggregate() =>
        new()
        {
            TriBoomerangInstinctCards =
            {
                Card("CARD.REAP#1", "Reap"),
                Card("CARD.GRAVE_WARDEN#1", "Grave Warden"),
                Card("CARD.SEVERANCE#2", "Severance+"),
            },
            TriBoomerangInstinctCardPlays = 7,
            TriBoomerangCombats = 3,
        };

    private static RelicEnchantedCardAggregate Card(
        string instanceId,
        string displayName) =>
        new()
        {
            CardInstanceId = instanceId,
            DisplayName = displayName,
        };

    private static void AssertRepresentativeAggregate(RelicAggregate agg)
    {
        Assert.Collection(
            agg.TriBoomerangInstinctCards,
            card =>
            {
                Assert.Equal("CARD.REAP#1", card.CardInstanceId);
                Assert.Equal("Reap", card.DisplayName);
            },
            card =>
            {
                Assert.Equal("CARD.GRAVE_WARDEN#1", card.CardInstanceId);
                Assert.Equal("Grave Warden", card.DisplayName);
            },
            card =>
            {
                Assert.Equal("CARD.SEVERANCE#2", card.CardInstanceId);
                Assert.Equal("Severance+", card.DisplayName);
            });
        Assert.Equal(7, agg.TriBoomerangInstinctCardPlays);
        Assert.Equal(3, agg.TriBoomerangCombats);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException(
                "BuildTriBoomerangBodyBBCode returned null."));
}
