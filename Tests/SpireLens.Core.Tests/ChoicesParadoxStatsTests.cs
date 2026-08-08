using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

/// <summary>
/// Pins Choices Paradox's screen counting, offered/taken rarity split, and
/// persisted presentation. The live turn-start selection timing remains
/// user-owned gameplay verification.
/// </summary>
public class ChoicesParadoxStatsTests
{
    private const string ChoicesParadoxRelicId = "RELIC.CHOICES_PARADOX";

    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildChoicesParadoxBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "BuildChoicesParadoxBodyBBCode not found.");

    [Fact]
    public void TrackingMath_CountsOneScreenAndSplitsOfferedRarities()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordChoicesParadoxScreenForTest(agg);
        RunTracker.RecordChoicesParadoxOffersForTest(
            agg,
            [
                CardRarity.Common,
                CardRarity.Common,
                CardRarity.Common,
                CardRarity.Uncommon,
                CardRarity.Rare,
            ]);
        RunTracker.RecordChoicesParadoxTakenForTest(agg, CardRarity.Rare);

        Assert.Equal(1, agg.Activations);
        Assert.Equal(3, agg.CommonCardsOffered);
        Assert.Equal(1, agg.UncommonCardsOffered);
        Assert.Equal(1, agg.RareCardsOffered);
        Assert.Equal(0, agg.CommonCardsTaken);
        Assert.Equal(0, agg.UncommonCardsTaken);
        Assert.Equal(1, agg.RareCardsTaken);
    }

    [Fact]
    public void TrackingMath_IgnoresRaritiesOutsideTheOfferableBuckets()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordChoicesParadoxOffersForTest(
            agg,
            [CardRarity.Basic, CardRarity.Ancient, CardRarity.Event, CardRarity.None]);
        RunTracker.RecordChoicesParadoxTakenForTest(agg, CardRarity.Basic);

        Assert.Equal(0, agg.CommonCardsOffered);
        Assert.Equal(0, agg.UncommonCardsOffered);
        Assert.Equal(0, agg.RareCardsOffered);
        Assert.Equal(0, agg.CommonCardsTaken);
        Assert.Equal(0, agg.UncommonCardsTaken);
        Assert.Equal(0, agg.RareCardsTaken);
    }

    [Fact]
    public void RelicAggregate_ChoicesParadoxFields_JsonRoundtripPreservesValues()
    {
        var run = new RunData();
        run.RelicAggregates[ChoicesParadoxRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(
            json,
            RunStorage.Options);

        Assert.Contains("\"common_cards_offered\"", json);
        Assert.Contains("\"uncommon_cards_offered\"", json);
        Assert.Contains("\"rare_cards_offered\"", json);
        Assert.Contains("\"common_cards_taken\"", json);
        Assert.Contains("\"uncommon_cards_taken\"", json);
        Assert.Contains("\"rare_cards_taken\"", json);
        Assert.NotNull(restored);
        AssertPopulatedAggregate(restored!.RelicAggregates[ChoicesParadoxRelicId]);
    }

    [Fact]
    public void MergeRelicAggregateInto_AccumulatesChoicesParadoxFields()
    {
        var target = PopulatedAggregate();

        RunTracker.MergeRelicAggregateInto(target, PopulatedAggregate());

        Assert.Equal(6, target.Activations);
        Assert.Equal(38, target.CommonCardsOffered);
        Assert.Equal(16, target.UncommonCardsOffered);
        Assert.Equal(6, target.RareCardsOffered);
        Assert.Equal(4, target.CommonCardsTaken);
        Assert.Equal(4, target.UncommonCardsTaken);
        Assert.Equal(2, target.RareCardsTaken);
    }

    [Fact]
    public void Tooltip_ShowsScreensThenOfferedThenTakenRarityRows()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("Activations", body);
        Assert.Contains("Commons offered", body);
        Assert.Contains("Uncommons offered", body);
        Assert.Contains("Rares offered", body);
        Assert.Contains("Commons taken", body);
        Assert.Contains("Uncommons taken", body);
        Assert.Contains("Rares taken", body);
        Assert.Contains(
            StatConceptGlossary.RenderHintedGlyph("offered"),
            body);
        Assert.Contains(
            StatConceptGlossary.RenderHintedGlyph("taken"),
            body);

        Assert.True(
            body.IndexOf("Commons offered", StringComparison.Ordinal)
            < body.IndexOf("Commons taken", StringComparison.Ordinal),
            "Offered rows should precede taken rows.");
    }

    [Fact]
    public void Tooltip_TintsUncommonAndRareCardIcons()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("color=#87CEEB", body);
        Assert.Contains("color=#EFC850", body);
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void TooltipDispatch_RecognizesChoicesParadox()
    {
        var relic = (ChoicesParadox)
            RuntimeHelpers.GetUninitializedObject(typeof(ChoicesParadox));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            PopulatedAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Choices Paradox", title);
        Assert.Contains("Commons offered", body);
        Assert.Contains("Rares taken", body);
    }

    private static RelicAggregate PopulatedAggregate()
        => new()
        {
            Activations = 3,
            CommonCardsOffered = 19,
            UncommonCardsOffered = 8,
            RareCardsOffered = 3,
            CommonCardsTaken = 2,
            UncommonCardsTaken = 2,
            RareCardsTaken = 1,
        };

    private static void AssertPopulatedAggregate(RelicAggregate agg)
    {
        Assert.Equal(3, agg.Activations);
        Assert.Equal(19, agg.CommonCardsOffered);
        Assert.Equal(8, agg.UncommonCardsOffered);
        Assert.Equal(3, agg.RareCardsOffered);
        Assert.Equal(2, agg.CommonCardsTaken);
        Assert.Equal(2, agg.UncommonCardsTaken);
        Assert.Equal(1, agg.RareCardsTaken);
    }

    private static string BuildBody(RelicAggregate aggregate)
        => (string)(BuildBodyMethod.Invoke(null, [aggregate])
            ?? throw new InvalidOperationException(
                "BuildChoicesParadoxBodyBBCode returned null."));
}
