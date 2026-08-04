using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class PaelsEyeStatsTests
{
    private const string PaelsEyeRelicId = "RELIC.PAELS_EYE";

    private static readonly MethodInfo BuildPaelsEyeBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildPaelsEyeBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildPaelsEyeBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_PaelsEyeFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0, agg.PaelsEyeCardsExhausted);
        Assert.Equal(0, agg.PaelsEyeStrikesAndDefendsExhausted);
        Assert.Equal(0, agg.PaelsEyeActivationTurnTotal);
        Assert.Equal(0, agg.PaelsEyeActivationTurnSamples);
        Assert.Equal(0, agg.PaelsEyeCombats);
        Assert.Equal(0, agg.StatusCardsExhausted);
        Assert.Equal(0, agg.CurseCardsExhausted);
        Assert.Equal(0, agg.CombatsWithoutActivation);
    }

    [Fact]
    public void RelicAggregate_PaelsEyeFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[PaelsEyeRelicId] = new RelicAggregate
        {
            Activations = 3,
            PaelsEyeCardsExhausted = 11,
            PaelsEyeStrikesAndDefendsExhausted = 3,
            PaelsEyeActivationTurnTotal = 7,
            PaelsEyeActivationTurnSamples = 3,
            PaelsEyeCombats = 8,
            StatusCardsExhausted = 4,
            CurseCardsExhausted = 2,
            CombatsWithoutActivation = 5,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("paels_eye_cards_exhausted", json);
        Assert.Contains("paels_eye_strikes_and_defends_exhausted", json);
        Assert.Contains("paels_eye_activation_turn_total", json);
        Assert.Contains("paels_eye_activation_turn_samples", json);
        Assert.Contains("paels_eye_combats", json);
        Assert.Contains("status_cards_exhausted", json);
        Assert.Contains("curse_cards_exhausted", json);
        Assert.Contains("combats_without_activation", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[PaelsEyeRelicId];
        Assert.Equal(3, restoredAgg.Activations);
        Assert.Equal(11, restoredAgg.PaelsEyeCardsExhausted);
        Assert.Equal(3, restoredAgg.PaelsEyeStrikesAndDefendsExhausted);
        Assert.Equal(7, restoredAgg.PaelsEyeActivationTurnTotal);
        Assert.Equal(3, restoredAgg.PaelsEyeActivationTurnSamples);
        Assert.Equal(8, restoredAgg.PaelsEyeCombats);
        Assert.Equal(4, restoredAgg.StatusCardsExhausted);
        Assert.Equal(2, restoredAgg.CurseCardsExhausted);
        Assert.Equal(5, restoredAgg.CombatsWithoutActivation);
    }

    [Fact]
    public void MergeRelicAggregateInto_PaelsEyeFields_Accumulates()
    {
        var target = new RelicAggregate
        {
            Activations = 1,
            PaelsEyeCardsExhausted = 5,
            PaelsEyeStrikesAndDefendsExhausted = 2,
            PaelsEyeActivationTurnTotal = 2,
            PaelsEyeActivationTurnSamples = 1,
            PaelsEyeCombats = 4,
            StatusCardsExhausted = 2,
            CurseCardsExhausted = 1,
            CombatsWithoutActivation = 3,
        };
        var source = new RelicAggregate
        {
            Activations = 2,
            PaelsEyeCardsExhausted = 6,
            PaelsEyeStrikesAndDefendsExhausted = 1,
            PaelsEyeActivationTurnTotal = 5,
            PaelsEyeActivationTurnSamples = 2,
            PaelsEyeCombats = 4,
            StatusCardsExhausted = 3,
            CurseCardsExhausted = 4,
            CombatsWithoutActivation = 5,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(3, target.Activations);
        Assert.Equal(11, target.PaelsEyeCardsExhausted);
        Assert.Equal(3, target.PaelsEyeStrikesAndDefendsExhausted);
        Assert.Equal(7, target.PaelsEyeActivationTurnTotal);
        Assert.Equal(3, target.PaelsEyeActivationTurnSamples);
        Assert.Equal(8, target.PaelsEyeCombats);
        Assert.Equal(5, target.StatusCardsExhausted);
        Assert.Equal(5, target.CurseCardsExhausted);
        Assert.Equal(8, target.CombatsWithoutActivation);
    }

    [Fact]
    public void RunTracker_RecordPaelsEyeActivationForTest_AccumulatesAndClamps()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordPaelsEyeActivationForTest(
            agg,
            cardsExhausted: 5,
            strikesAndDefendsExhausted: 2,
            statusesExhausted: 2,
            cursesExhausted: 1,
            activationTurn: 2);
        RunTracker.RecordPaelsEyeActivationForTest(
            agg,
            cardsExhausted: 7,
            strikesAndDefendsExhausted: 1,
            statusesExhausted: 2,
            cursesExhausted: 3,
            activationTurn: 4);
        RunTracker.RecordPaelsEyeActivationForTest(
            agg,
            cardsExhausted: -9,
            strikesAndDefendsExhausted: -3,
            statusesExhausted: -5,
            cursesExhausted: -7,
            activationTurn: -1);

        Assert.Equal(3, agg.Activations);
        Assert.Equal(12, agg.PaelsEyeCardsExhausted);
        Assert.Equal(3, agg.PaelsEyeStrikesAndDefendsExhausted);
        Assert.Equal(6, agg.PaelsEyeActivationTurnTotal);
        Assert.Equal(2, agg.PaelsEyeActivationTurnSamples);
        Assert.Equal(4, agg.StatusCardsExhausted);
        Assert.Equal(4, agg.CurseCardsExhausted);
    }

    [Fact]
    public void RunTracker_RecordPaelsEyeCombatWithoutActivationForTest_AccumulatesAndClamps()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordPaelsEyeCombatWithoutActivationForTest(agg);
        RunTracker.RecordPaelsEyeCombatWithoutActivationForTest(agg, 3);
        RunTracker.RecordPaelsEyeCombatWithoutActivationForTest(agg, -5);

        Assert.Equal(4, agg.CombatsWithoutActivation);
    }

    [Fact]
    public void RunTracker_RecordPaelsEyeCombatForTest_AccumulatesAndClamps()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordPaelsEyeCombatForTest(agg);
        RunTracker.RecordPaelsEyeCombatForTest(agg, 3);
        RunTracker.RecordPaelsEyeCombatForTest(agg, -5);

        Assert.Equal(4, agg.PaelsEyeCombats);
    }

    [Fact]
    public void RelicTooltip_PaelsEye_ShowsExhaustRowsAndZeroValues()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Activations", body);
        Assert.Contains("Activated this combat", body);
        Assert.Contains("[b]false[/b]", body);
        Assert.Contains("Combats without activation", body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("combat"), body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("activation"), body);
        Assert.Contains("= 0", body);
        Assert.Contains("Average cards exhausted per combat", body);
        Assert.Contains("Average activation turn", body);
        Assert.Contains("Cards exhausted total", body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("all"), body);
        Assert.Contains("Strikes and Defends exhausted", body);
        Assert.Contains("Statuses exhausted", body);
        Assert.Contains("Curses exhausted", body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("status"), body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("curse"), body);
        Assert.Equal(8, CountOccurrences(body, "[b]0[/b]"));
        Assert.Equal(1, CountOccurrences(body, "[table=4]"));
        Assert.Contains("[left][b]false[/b][/left]", body);
        Assert.DoesNotContain("[right]", body);
    }

    [Fact]
    public void RelicTooltip_PaelsEye_ShowsTrackedCounts()
    {
        var body = BuildBody(new RelicAggregate
        {
            Activations = 3,
            PaelsEyeCardsExhausted = 11,
            PaelsEyeStrikesAndDefendsExhausted = 3,
            PaelsEyeActivationTurnTotal = 7,
            PaelsEyeActivationTurnSamples = 3,
            PaelsEyeCombats = 8,
            StatusCardsExhausted = 4,
            CurseCardsExhausted = 2,
            CombatsWithoutActivation = 5,
        });

        Assert.Contains("Activations", body);
        Assert.Contains("Activated this combat", body);
        Assert.Contains("[b]false[/b]", body);
        Assert.Contains("Combats without activation", body);
        Assert.Contains("Average cards exhausted per combat", body);
        Assert.Contains("[b]1.38[/b]", body);
        Assert.Contains("Average activation turn", body);
        Assert.Contains("[b]2.33[/b]", body);
        Assert.Contains("Cards exhausted total", body);
        Assert.Contains("[b]11[/b]", body);
        Assert.Contains("Strikes and Defends exhausted", body);
        Assert.Contains("Statuses exhausted", body);
        Assert.Contains("Curses exhausted", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]5[/b]", body);
    }

    [Fact]
    public void RelicTooltip_PaelsEye_CanShowActivatedThisCombatTrue()
    {
        var body = BuildBody(new RelicAggregate(), activatedThisCombat: true);

        Assert.Contains("Activated this combat", body);
        Assert.Contains("[b]true[/b]", body);
    }

    private static string BuildBody(RelicAggregate agg, bool activatedThisCombat = false)
    {
        return (string)(BuildPaelsEyeBodyMethod.Invoke(null, new object?[] { agg, activatedThisCombat })
            ?? throw new InvalidOperationException("BuildPaelsEyeBodyBBCode returned null."));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var start = 0;
        while (true)
        {
            var index = haystack.IndexOf(needle, start, StringComparison.Ordinal);
            if (index < 0) return count;
            count += 1;
            start = index + needle.Length;
        }
    }
}
