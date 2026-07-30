using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class LetterOpenerStatsTests
{
    private const string LetterOpenerRelicId = "RELIC.LETTER_OPENER";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_LetterOpenerFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0, agg.TotalDamageAttempted);
        Assert.Equal(0, agg.TotalTargets);
        Assert.Equal(0, agg.LetterOpenerSkillsPlayed);
        Assert.Equal(0, agg.LetterOpenerCombats);
        Assert.Equal(0, agg.LetterOpenerTurns);
        Assert.Equal(0, agg.LetterOpenerTurnsEndedAt1Charge);
        Assert.Equal(0, agg.LetterOpenerTurnsEndedAt2Charges);
    }

    [Fact]
    public void RelicAggregate_LetterOpenerFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[LetterOpenerRelicId] = new RelicAggregate
        {
            Activations = 3,
            TotalDamageAttempted = 45,
            TotalTargets = 9,
            LetterOpenerSkillsPlayed = 9,
            LetterOpenerCombats = 3,
            LetterOpenerTurns = 6,
            LetterOpenerTurnsEndedAt1Charge = 2,
            LetterOpenerTurnsEndedAt2Charges = 3,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("letter_opener_skills_played", json);
        Assert.Contains("letter_opener_combats", json);
        Assert.Contains("letter_opener_turns", json);
        Assert.Contains("letter_opener_turns_ended_at1_charge", json);
        Assert.Contains("letter_opener_turns_ended_at2_charges", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[LetterOpenerRelicId];
        Assert.Equal(3, agg.Activations);
        Assert.Equal(45, agg.TotalDamageAttempted);
        Assert.Equal(9, agg.TotalTargets);
        Assert.Equal(9, agg.LetterOpenerSkillsPlayed);
        Assert.Equal(3, agg.LetterOpenerCombats);
        Assert.Equal(6, agg.LetterOpenerTurns);
        Assert.Equal(2, agg.LetterOpenerTurnsEndedAt1Charge);
        Assert.Equal(3, agg.LetterOpenerTurnsEndedAt2Charges);
    }

    [Fact]
    public void MergeRelicAggregateInto_LetterOpenerFields_Accumulates()
    {
        var target = new RelicAggregate
        {
            Activations = 1,
            TotalDamageAttempted = 15,
            TotalTargets = 3,
            LetterOpenerSkillsPlayed = 3,
            LetterOpenerCombats = 1,
            LetterOpenerTurns = 2,
            LetterOpenerTurnsEndedAt1Charge = 1,
            LetterOpenerTurnsEndedAt2Charges = 0,
        };
        var source = new RelicAggregate
        {
            Activations = 2,
            TotalDamageAttempted = 30,
            TotalTargets = 6,
            LetterOpenerSkillsPlayed = 6,
            LetterOpenerCombats = 2,
            LetterOpenerTurns = 4,
            LetterOpenerTurnsEndedAt1Charge = 1,
            LetterOpenerTurnsEndedAt2Charges = 3,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(3, target.Activations);
        Assert.Equal(45, target.TotalDamageAttempted);
        Assert.Equal(9, target.TotalTargets);
        Assert.Equal(9, target.LetterOpenerSkillsPlayed);
        Assert.Equal(3, target.LetterOpenerCombats);
        Assert.Equal(6, target.LetterOpenerTurns);
        Assert.Equal(2, target.LetterOpenerTurnsEndedAt1Charge);
        Assert.Equal(3, target.LetterOpenerTurnsEndedAt2Charges);
    }

    [Fact]
    public void RunTracker_LetterOpenerHelpers_AccumulateAndClamp()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordLetterOpenerSkillPlayedForTest(agg, 3);
        RunTracker.RecordLetterOpenerSkillPlayedForTest(agg, -2);
        RunTracker.RecordLetterOpenerCombatForTest(agg, 2);
        RunTracker.RecordLetterOpenerCombatForTest(agg, -1);
        RunTracker.RecordLetterOpenerTurnForTest(agg, 4);
        RunTracker.RecordLetterOpenerTurnForTest(agg, -1);
        RunTracker.RecordLetterOpenerTurnEndChargeForTest(agg, 1);
        RunTracker.RecordLetterOpenerTurnEndChargeForTest(agg, 2);
        RunTracker.RecordLetterOpenerTurnEndChargeForTest(agg, 0);
        RunTracker.RecordLetterOpenerTurnEndChargeForTest(agg, 3);
        RunTracker.RecordLetterOpenerTurnEndChargeForTest(agg, -1);

        Assert.Equal(3, agg.LetterOpenerSkillsPlayed);
        Assert.Equal(2, agg.LetterOpenerCombats);
        Assert.Equal(4, agg.LetterOpenerTurns);
        Assert.Equal(1, agg.LetterOpenerTurnsEndedAt1Charge);
        Assert.Equal(1, agg.LetterOpenerTurnsEndedAt2Charges);
    }

    [Fact]
    public void CalculateLetterOpenerRates_ComputesObservedAverages()
    {
        var rates = RelicHoverShowPatch.CalculateLetterOpenerRates(new RelicAggregate
        {
            Activations = 3,
            TotalDamageAttempted = 45,
            TotalTargets = 9,
            LetterOpenerSkillsPlayed = 9,
            LetterOpenerCombats = 3,
            LetterOpenerTurns = 6,
            LetterOpenerTurnsEndedAt1Charge = 2,
            LetterOpenerTurnsEndedAt2Charges = 3,
        });

        Assert.Equal(15m, rates.DamagePerCombat);
        Assert.Equal(7.5m, rates.DamagePerTurn);
        Assert.Equal(3m, rates.TargetsPerActivation);
        Assert.Equal(5m, rates.DamagePerSkillPlayed);
    }

    [Fact]
    public void CalculateLetterOpenerRates_UsesZeroForMissingDenominators()
    {
        var rates = RelicHoverShowPatch.CalculateLetterOpenerRates(new RelicAggregate());

        Assert.Equal(0m, rates.DamagePerCombat);
        Assert.Equal(0m, rates.DamagePerTurn);
        Assert.Equal(0m, rates.TargetsPerActivation);
        Assert.Equal(0m, rates.DamagePerSkillPlayed);
    }
}
