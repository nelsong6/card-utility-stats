using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class LetterOpenerStatsTests
{
    private const string LetterOpenerRelicId = "RELIC.LETTER_OPENER";

    private static readonly MethodInfo BuildLetterOpenerBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildLetterOpenerBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildLetterOpenerBodyBBCode not found.");

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
    public void RelicTooltip_LetterOpener_ShowsRequestedRowsAndAverages()
    {
        var body = BuildBody(new RelicAggregate
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

        Assert.Contains("Activations", body);
        Assert.Contains("Damage attempted", body);
        Assert.Contains("Targets hit", body);
        Assert.Contains("Avg damage per combat", body);
        Assert.Contains("Avg damage per turn", body);
        Assert.Contains("Turns ended at 1 charge", body);
        Assert.Contains("Turns ended at 2 charges", body);
        Assert.Contains("Avg damage per skill played", body);
        Assert.Contains("[b]45[/b]", body);
        Assert.Contains("[b]15[/b]", body);
        Assert.Contains("[b]7.5[/b]", body);
        Assert.Contains("[b]5[/b]", body);
    }

    [Fact]
    public void RelicTooltip_LetterOpener_ShowsZeroRowsForEmptyAggregate()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Avg damage per combat", body);
        Assert.Contains("Avg damage per turn", body);
        Assert.Contains("Turns ended at 1 charge", body);
        Assert.Contains("Turns ended at 2 charges", body);
        Assert.Contains("Avg damage per skill played", body);
        Assert.Equal(8, CountOccurrences(body, "[b]0[/b]"));
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildLetterOpenerBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildLetterOpenerBodyBBCode returned null."));

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
