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

public class PaelsToothStatsTests
{
    private const string PaelsToothRelicId = "RELIC.PAELS_TOOTH";

    private static readonly MethodInfo BuildPaelsToothBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildPaelsToothBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildPaelsToothBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_PaelsToothCardsReturned_DefaultsToEmpty()
    {
        var agg = new RelicAggregate();

        Assert.NotNull(agg.CardsReturned);
        Assert.Empty(agg.CardsReturned);
    }

    [Fact]
    public void RelicAggregate_PaelsToothCardsReturned_JsonRoundtripPreservesOrderAndDuplicates()
    {
        var run = new RunData();
        run.RelicAggregates[PaelsToothRelicId] = BuildPopulatedAggregate();

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("cards_returned", json);
        Assert.Contains("upgrade_level", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        AssertPopulated(restored!.RelicAggregates[PaelsToothRelicId]);
    }

    [Fact]
    public void RecordPaelsToothCardReturnedForTest_AppendsObservedCardsWithoutPoolingDuplicates()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordPaelsToothCardReturnedForTest(agg, "CARD.STRIKE_KIN", "Strike+", 1);
        RunTracker.RecordPaelsToothCardReturnedForTest(agg, "CARD.POMMEL_STRIKE", "Pommel Strike++", 2);
        RunTracker.RecordPaelsToothCardReturnedForTest(agg, "CARD.STRIKE_KIN", "Strike++", 2);
        RunTracker.RecordPaelsToothCardReturnedForTest(agg, null, "Ignored", 1);

        AssertPopulated(agg);
    }

    [Fact]
    public void MergeRelicAggregateInto_PaelsToothCardsReturned_AppendsAndDeepCopies()
    {
        var target = new RelicAggregate
        {
            CardsReturned = new()
            {
                ReturnedCard("CARD.BASH", "Bash+", 1),
            },
        };
        var source = new RelicAggregate
        {
            CardsReturned = new()
            {
                ReturnedCard("CARD.STRIKE_KIN", "Strike+", 1),
                ReturnedCard("CARD.STRIKE_KIN", "Strike++", 2),
            },
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(
            new[] { "CARD.BASH", "CARD.STRIKE_KIN", "CARD.STRIKE_KIN" },
            target.CardsReturned.Select(card => card.CardId));
        Assert.Equal(new[] { 1, 1, 2 }, target.CardsReturned.Select(card => card.UpgradeLevel));

        source.CardsReturned[0].DisplayName = "mutated source";
        Assert.Equal("Strike+", target.CardsReturned[1].DisplayName);
    }

    [Fact]
    public void RelicTooltip_PaelsTooth_ShowsReturnedCardsInObservedOrder()
    {
        var agg = new RelicAggregate
        {
            CardsReturned = new()
            {
                ReturnedCard("CARD.STRIKE_KIN", "Strike+", 1),
                ReturnedCard("CARD.POMMEL_STRIKE", "Pommel [Strike]++", 2),
                ReturnedCard("CARD.STRIKE_KIN", "Strike++", 2),
            },
        };

        var body = BuildBody(agg);

        Assert.Contains("Cards returned", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Equal(3, CountOccurrences(body, "Returned card"));
        Assert.Contains("Pommel [lb]Strike]++", body);
        Assert.True(body.IndexOf("Strike+", StringComparison.Ordinal)
                    < body.IndexOf("Pommel [lb]Strike]++", StringComparison.Ordinal));
        Assert.True(body.IndexOf("Pommel [lb]Strike]++", StringComparison.Ordinal)
                    < body.LastIndexOf("Strike++", StringComparison.Ordinal));
    }

    [Fact]
    public void RelicTooltip_PaelsTooth_UsesUpgradeLevelForMissingDisplayName()
    {
        var agg = new RelicAggregate
        {
            CardsReturned = new()
            {
                ReturnedCard("CARD.POMMEL_STRIKE", "", 2),
            },
        };

        var body = BuildBody(agg);

        Assert.Contains("Pommel Strike++", body);
    }

    [Fact]
    public void RelicTooltip_PaelsTooth_ZeroStateShowsOnlyTotal()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Cards returned", body);
        Assert.Contains("[b]0[/b]", body);
        Assert.DoesNotContain("Returned card", body);
    }

    [Fact]
    public void RelicTooltip_PaelsTooth_DispatchesForPaelsToothModel()
    {
        var relic = (PaelsTooth)RuntimeHelpers.GetUninitializedObject(typeof(PaelsTooth));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            BuildPopulatedAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Pael's Tooth", title);
        Assert.Contains("Cards returned", body);
        Assert.Contains("Strike+", body);
    }

    [Fact]
    public void RunData_OlderShapeWithoutPaelsToothCardsReturned_DeserializesWithEmptyDefault()
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
              "def_counters": {},
              "relic_aggregates": {
                "RELIC.PAELS_TOOTH": {}
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        Assert.Empty(run!.RelicAggregates[PaelsToothRelicId].CardsReturned);
    }

    private static RelicAggregate BuildPopulatedAggregate()
        => new()
        {
            CardsReturned = new()
            {
                ReturnedCard("CARD.STRIKE_KIN", "Strike+", 1),
                ReturnedCard("CARD.POMMEL_STRIKE", "Pommel Strike++", 2),
                ReturnedCard("CARD.STRIKE_KIN", "Strike++", 2),
            },
        };

    private static RelicCardReturnAggregate ReturnedCard(
        string cardId,
        string displayName,
        int upgradeLevel)
        => new()
        {
            CardId = cardId,
            DisplayName = displayName,
            UpgradeLevel = upgradeLevel,
        };

    private static void AssertPopulated(RelicAggregate agg)
    {
        Assert.Collection(
            agg.CardsReturned,
            card => AssertReturnedCard(card, "CARD.STRIKE_KIN", "Strike+", 1),
            card => AssertReturnedCard(card, "CARD.POMMEL_STRIKE", "Pommel Strike++", 2),
            card => AssertReturnedCard(card, "CARD.STRIKE_KIN", "Strike++", 2));
    }

    private static void AssertReturnedCard(
        RelicCardReturnAggregate card,
        string cardId,
        string displayName,
        int upgradeLevel)
    {
        Assert.Equal(cardId, card.CardId);
        Assert.Equal(displayName, card.DisplayName);
        Assert.Equal(upgradeLevel, card.UpgradeLevel);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildPaelsToothBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildPaelsToothBodyBBCode returned null."));

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
