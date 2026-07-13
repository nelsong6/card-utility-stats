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

public class UnlimitedAttackChargeRelicStatsTests
{
    private const string KusarigamaRelicId = "RELIC.KUSARIGAMA";
    private const string OrnamentalFanRelicId = "RELIC.ORNAMENTAL_FAN";
    private const string ShurikenRelicId = "RELIC.SHURIKEN";

    private static readonly MethodInfo BuildKusarigamaBodyMethod = GetBuilder("BuildKusarigamaBodyBBCode");
    private static readonly MethodInfo BuildOrnamentalFanBodyMethod = GetBuilder("BuildOrnamentalFanBodyBBCode");
    private static readonly MethodInfo BuildShurikenBodyMethod = GetBuilder("BuildShurikenBodyBBCode");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_UnlimitedAttackChargeFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.KusarigamaAttacksPlayed);
        Assert.Equal(0, agg.KusarigamaTurnsEndedAt1Charge);
        Assert.Equal(0, agg.KusarigamaTurnsEndedAt2Charges);
        Assert.Equal(0, agg.KusarigamaTurnEndChargeTotal);
        Assert.Equal(0, agg.KusarigamaTurnEndChargeCount);

        Assert.Equal(0, agg.OrnamentalFanAttacksPlayed);
        Assert.Equal(0, agg.OrnamentalFanTurnsEndedAt1Charge);
        Assert.Equal(0, agg.OrnamentalFanTurnsEndedAt2Charges);
        Assert.Equal(0, agg.OrnamentalFanTurnEndChargeTotal);
        Assert.Equal(0, agg.OrnamentalFanTurnEndChargeCount);

        Assert.Equal(0, agg.ShurikenAttacksPlayed);
        Assert.Equal(0, agg.ShurikenTurnsEndedAt1Charge);
        Assert.Equal(0, agg.ShurikenTurnsEndedAt2Charges);
        Assert.Equal(0, agg.ShurikenTurnEndChargeTotal);
        Assert.Equal(0, agg.ShurikenTurnEndChargeCount);

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0, agg.TotalDamageAttempted);
        Assert.Equal(0, agg.TotalDamageDealt);
        Assert.Equal(0, agg.TotalDamageBlocked);
        Assert.Equal(0, agg.TotalDamageOverkill);
        Assert.Equal(0, agg.Kills);
        Assert.Equal(0, agg.TotalTargets);
        Assert.Equal(0, agg.AdditionalBlockGained);
        Assert.Equal(0m, agg.StrengthAdded);
    }

    [Fact]
    public void RelicAggregate_UnlimitedAttackChargeFields_JsonRoundtrip_PreservesFields()
    {
        var run = BuildRepresentativeRun();

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("kusarigama_attacks_played", json);
        Assert.Contains("kusarigama_turns_ended_at1_charge", json);
        Assert.Contains("kusarigama_turns_ended_at2_charges", json);
        Assert.Contains("kusarigama_turn_end_charge_total", json);
        Assert.Contains("kusarigama_turn_end_charge_count", json);
        Assert.Contains("ornamental_fan_attacks_played", json);
        Assert.Contains("ornamental_fan_turns_ended_at1_charge", json);
        Assert.Contains("ornamental_fan_turns_ended_at2_charges", json);
        Assert.Contains("ornamental_fan_turn_end_charge_total", json);
        Assert.Contains("ornamental_fan_turn_end_charge_count", json);
        Assert.Contains("shuriken_attacks_played", json);
        Assert.Contains("shuriken_turns_ended_at1_charge", json);
        Assert.Contains("shuriken_turns_ended_at2_charges", json);
        Assert.Contains("shuriken_turn_end_charge_total", json);
        Assert.Contains("shuriken_turn_end_charge_count", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        AssertRepresentativeAggregates(restored!);
    }

    [Fact]
    public void RunTracker_UnlimitedAttackChargeHelpers_AccumulateAndClamp()
    {
        var kusarigama = new RelicAggregate();
        RunTracker.RecordKusarigamaAttackPlayedForTest(kusarigama, 14);
        RunTracker.RecordKusarigamaAttackPlayedForTest(kusarigama, -2);
        RunTracker.RecordKusarigamaActivationForTest(kusarigama);
        RunTracker.RecordKusarigamaActivationForTest(kusarigama);
        RunTracker.RecordKusarigamaDamageForTest(
            kusarigama,
            new[]
            {
                (BlockedDamage: 2, UnblockedDamage: 3, OverkillDamage: 1, WasTargetKilled: true),
                (BlockedDamage: 0, UnblockedDamage: 6, OverkillDamage: 0, WasTargetKilled: false),
            });
        RunTracker.RecordKusarigamaTurnEndChargeForTest(kusarigama, 1);
        RunTracker.RecordKusarigamaTurnEndChargeForTest(kusarigama, 2);
        RunTracker.RecordKusarigamaTurnEndChargeForTest(kusarigama, 5);
        RunTracker.RecordKusarigamaTurnEndChargeForTest(kusarigama, -1);

        Assert.Equal(14, kusarigama.KusarigamaAttacksPlayed);
        Assert.Equal(2, kusarigama.Activations);
        Assert.Equal(12, kusarigama.TotalDamageAttempted);
        Assert.Equal(9, kusarigama.TotalDamageDealt);
        Assert.Equal(2, kusarigama.TotalDamageBlocked);
        Assert.Equal(1, kusarigama.TotalDamageOverkill);
        Assert.Equal(1, kusarigama.Kills);
        Assert.Equal(2, kusarigama.TotalTargets);
        Assert.Equal(1, kusarigama.KusarigamaTurnsEndedAt1Charge);
        Assert.Equal(2, kusarigama.KusarigamaTurnsEndedAt2Charges);
        Assert.Equal(5, kusarigama.KusarigamaTurnEndChargeTotal);
        Assert.Equal(3, kusarigama.KusarigamaTurnEndChargeCount);

        var ornamentalFan = new RelicAggregate();
        RunTracker.RecordOrnamentalFanAttackPlayedForTest(ornamentalFan, 8);
        RunTracker.RecordOrnamentalFanAttackPlayedForTest(ornamentalFan, -2);
        RunTracker.RecordOrnamentalFanActivationForTest(ornamentalFan);
        RunTracker.RecordOrnamentalFanActivationForTest(ornamentalFan);
        RunTracker.RecordOrnamentalFanBlockGainedForTest(ornamentalFan, 4);
        RunTracker.RecordOrnamentalFanBlockGainedForTest(ornamentalFan, 5);
        RunTracker.RecordOrnamentalFanBlockGainedForTest(ornamentalFan, -2);
        RunTracker.RecordOrnamentalFanTurnEndChargeForTest(ornamentalFan, 1);
        RunTracker.RecordOrnamentalFanTurnEndChargeForTest(ornamentalFan, 2);
        RunTracker.RecordOrnamentalFanTurnEndChargeForTest(ornamentalFan, 4);
        RunTracker.RecordOrnamentalFanTurnEndChargeForTest(ornamentalFan, -1);

        Assert.Equal(8, ornamentalFan.OrnamentalFanAttacksPlayed);
        Assert.Equal(2, ornamentalFan.Activations);
        Assert.Equal(9, ornamentalFan.AdditionalBlockGained);
        Assert.Equal(2, ornamentalFan.OrnamentalFanTurnsEndedAt1Charge);
        Assert.Equal(1, ornamentalFan.OrnamentalFanTurnsEndedAt2Charges);
        Assert.Equal(4, ornamentalFan.OrnamentalFanTurnEndChargeTotal);
        Assert.Equal(3, ornamentalFan.OrnamentalFanTurnEndChargeCount);

        var shuriken = new RelicAggregate();
        RunTracker.RecordShurikenAttackPlayedForTest(shuriken, 11);
        RunTracker.RecordShurikenAttackPlayedForTest(shuriken, -2);
        RunTracker.RecordShurikenActivationForTest(shuriken, 1m);
        RunTracker.RecordShurikenActivationForTest(shuriken, 2m);
        RunTracker.RecordShurikenActivationForTest(shuriken, -1m);
        RunTracker.RecordShurikenTurnEndChargeForTest(shuriken, 0);
        RunTracker.RecordShurikenTurnEndChargeForTest(shuriken, 1);
        RunTracker.RecordShurikenTurnEndChargeForTest(shuriken, 2);
        RunTracker.RecordShurikenTurnEndChargeForTest(shuriken, 8);
        RunTracker.RecordShurikenTurnEndChargeForTest(shuriken, -1);

        Assert.Equal(11, shuriken.ShurikenAttacksPlayed);
        Assert.Equal(3, shuriken.Activations);
        Assert.Equal(3m, shuriken.StrengthAdded);
        Assert.Equal(1, shuriken.ShurikenTurnsEndedAt1Charge);
        Assert.Equal(2, shuriken.ShurikenTurnsEndedAt2Charges);
        Assert.Equal(5, shuriken.ShurikenTurnEndChargeTotal);
        Assert.Equal(4, shuriken.ShurikenTurnEndChargeCount);
    }

    [Fact]
    public void MergeRelicAggregateInto_UnlimitedAttackChargeFields_Accumulates()
    {
        var kusarigama = new RelicAggregate
        {
            KusarigamaAttacksPlayed = 5,
            Activations = 1,
            TotalDamageAttempted = 6,
            TotalDamageDealt = 4,
            TotalDamageBlocked = 1,
            TotalDamageOverkill = 1,
            TotalTargets = 1,
            KusarigamaTurnsEndedAt1Charge = 1,
            KusarigamaTurnEndChargeTotal = 1,
            KusarigamaTurnEndChargeCount = 1,
        };
        RunTracker.MergeRelicAggregateInto(kusarigama, new RelicAggregate
        {
            KusarigamaAttacksPlayed = 9,
            Activations = 3,
            TotalDamageAttempted = 18,
            TotalDamageDealt = 13,
            TotalDamageBlocked = 2,
            TotalDamageOverkill = 3,
            Kills = 2,
            TotalTargets = 3,
            KusarigamaTurnsEndedAt1Charge = 1,
            KusarigamaTurnsEndedAt2Charges = 3,
            KusarigamaTurnEndChargeTotal = 7,
            KusarigamaTurnEndChargeCount = 6,
        });

        Assert.Equal(14, kusarigama.KusarigamaAttacksPlayed);
        Assert.Equal(4, kusarigama.Activations);
        Assert.Equal(24, kusarigama.TotalDamageAttempted);
        Assert.Equal(17, kusarigama.TotalDamageDealt);
        Assert.Equal(3, kusarigama.TotalDamageBlocked);
        Assert.Equal(4, kusarigama.TotalDamageOverkill);
        Assert.Equal(2, kusarigama.Kills);
        Assert.Equal(4, kusarigama.TotalTargets);
        Assert.Equal(2, kusarigama.KusarigamaTurnsEndedAt1Charge);
        Assert.Equal(3, kusarigama.KusarigamaTurnsEndedAt2Charges);
        Assert.Equal(8, kusarigama.KusarigamaTurnEndChargeTotal);
        Assert.Equal(7, kusarigama.KusarigamaTurnEndChargeCount);

        var ornamentalFan = new RelicAggregate
        {
            OrnamentalFanAttacksPlayed = 5,
            Activations = 1,
            AdditionalBlockGained = 4,
            OrnamentalFanTurnsEndedAt1Charge = 1,
            OrnamentalFanTurnEndChargeTotal = 1,
            OrnamentalFanTurnEndChargeCount = 1,
        };
        RunTracker.MergeRelicAggregateInto(ornamentalFan, new RelicAggregate
        {
            OrnamentalFanAttacksPlayed = 6,
            Activations = 2,
            AdditionalBlockGained = 9,
            OrnamentalFanTurnsEndedAt2Charges = 3,
            OrnamentalFanTurnEndChargeTotal = 6,
            OrnamentalFanTurnEndChargeCount = 4,
        });

        Assert.Equal(11, ornamentalFan.OrnamentalFanAttacksPlayed);
        Assert.Equal(3, ornamentalFan.Activations);
        Assert.Equal(13, ornamentalFan.AdditionalBlockGained);
        Assert.Equal(1, ornamentalFan.OrnamentalFanTurnsEndedAt1Charge);
        Assert.Equal(3, ornamentalFan.OrnamentalFanTurnsEndedAt2Charges);
        Assert.Equal(7, ornamentalFan.OrnamentalFanTurnEndChargeTotal);
        Assert.Equal(5, ornamentalFan.OrnamentalFanTurnEndChargeCount);

        var shuriken = new RelicAggregate
        {
            ShurikenAttacksPlayed = 8,
            Activations = 2,
            StrengthAdded = 2m,
            ShurikenTurnsEndedAt1Charge = 1,
            ShurikenTurnEndChargeTotal = 1,
            ShurikenTurnEndChargeCount = 2,
        };
        RunTracker.MergeRelicAggregateInto(shuriken, new RelicAggregate
        {
            ShurikenAttacksPlayed = 9,
            Activations = 3,
            StrengthAdded = 3m,
            ShurikenTurnsEndedAt1Charge = 1,
            ShurikenTurnsEndedAt2Charges = 2,
            ShurikenTurnEndChargeTotal = 5,
            ShurikenTurnEndChargeCount = 4,
        });

        Assert.Equal(17, shuriken.ShurikenAttacksPlayed);
        Assert.Equal(5, shuriken.Activations);
        Assert.Equal(5m, shuriken.StrengthAdded);
        Assert.Equal(2, shuriken.ShurikenTurnsEndedAt1Charge);
        Assert.Equal(2, shuriken.ShurikenTurnsEndedAt2Charges);
        Assert.Equal(6, shuriken.ShurikenTurnEndChargeTotal);
        Assert.Equal(6, shuriken.ShurikenTurnEndChargeCount);
    }

    [Fact]
    public void RelicTooltips_UnlimitedAttackChargeRelics_ShowEffectAndChargeRows()
    {
        var kusarigamaBody = BuildBody(BuildKusarigamaBodyMethod, new RelicAggregate
        {
            KusarigamaAttacksPlayed = 14,
            Activations = 4,
            TotalDamageAttempted = 24,
            TotalDamageDealt = 17,
            TotalDamageBlocked = 3,
            TotalDamageOverkill = 4,
            Kills = 2,
            TotalTargets = 4,
            KusarigamaTurnsEndedAt1Charge = 2,
            KusarigamaTurnsEndedAt2Charges = 3,
            KusarigamaTurnEndChargeTotal = 8,
            KusarigamaTurnEndChargeCount = 7,
        });
        Assert.Contains("Attacks played", kusarigamaBody);
        Assert.Contains("Activations", kusarigamaBody);
        Assert.Contains("Damage attempted", kusarigamaBody);
        Assert.Contains("Damage dealt", kusarigamaBody);
        Assert.Contains("Damage blocked", kusarigamaBody);
        Assert.Contains("Overkill", kusarigamaBody);
        Assert.Contains("Kills", kusarigamaBody);
        Assert.Contains("Targets hit", kusarigamaBody);
        Assert.Contains("Damage per activation", kusarigamaBody);
        Assert.Contains("Turns ended at 1 charge", kusarigamaBody);
        Assert.Contains("Turns ended at 2 charges", kusarigamaBody);
        Assert.Contains("Avg charge at turn end", kusarigamaBody);
        Assert.Contains("[b]4.25[/b]", kusarigamaBody);
        Assert.Contains("[b]1.14[/b]", kusarigamaBody);

        var ornamentalFanBody = BuildBody(BuildOrnamentalFanBodyMethod, new RelicAggregate
        {
            OrnamentalFanAttacksPlayed = 11,
            Activations = 3,
            AdditionalBlockGained = 13,
            OrnamentalFanTurnsEndedAt1Charge = 1,
            OrnamentalFanTurnsEndedAt2Charges = 3,
            OrnamentalFanTurnEndChargeTotal = 7,
            OrnamentalFanTurnEndChargeCount = 5,
        });
        Assert.Contains("Attacks played", ornamentalFanBody);
        Assert.Contains("Activations", ornamentalFanBody);
        Assert.Contains("[img=16x16]res://images/ui/combat/block.png[/img] block gained", ornamentalFanBody);
        Assert.Contains("block gained per activation", ornamentalFanBody);
        Assert.Contains("Turns ended at 1 charge", ornamentalFanBody);
        Assert.Contains("Turns ended at 2 charges", ornamentalFanBody);
        Assert.Contains("Avg charge at turn end", ornamentalFanBody);
        Assert.Contains("[b]4.33[/b]", ornamentalFanBody);
        Assert.Contains("[b]1.4[/b]", ornamentalFanBody);

        var shurikenBody = BuildBody(BuildShurikenBodyMethod, new RelicAggregate
        {
            ShurikenAttacksPlayed = 14,
            Activations = 4,
            StrengthAdded = 5m,
            ShurikenTurnsEndedAt1Charge = 1,
            ShurikenTurnsEndedAt2Charges = 2,
            ShurikenTurnEndChargeTotal = 5,
            ShurikenTurnEndChargeCount = 4,
        });
        Assert.Contains("Attacks played", shurikenBody);
        Assert.Contains("Activations", shurikenBody);
        Assert.Contains("Strength gained", shurikenBody);
        Assert.Contains("Strength gained per activation", shurikenBody);
        Assert.Contains("Turns ended at 1 charge", shurikenBody);
        Assert.Contains("Turns ended at 2 charges", shurikenBody);
        Assert.Contains("Avg charge at turn end", shurikenBody);
        Assert.Contains("[b]1.25[/b]", shurikenBody);
    }

    [Fact]
    public void RelicTooltips_UnlimitedAttackChargeRelics_DispatchForAllModels()
    {
        var kusarigama = (Kusarigama)RuntimeHelpers.GetUninitializedObject(typeof(Kusarigama));
        var ornamentalFan = (OrnamentalFan)RuntimeHelpers.GetUninitializedObject(typeof(OrnamentalFan));
        var shuriken = (Shuriken)RuntimeHelpers.GetUninitializedObject(typeof(Shuriken));

        Assert.True(RelicHoverShowPatch.TryBuildBodyBBCode(
            kusarigama,
            new RelicAggregate(),
            floorCount: null,
            out var kusarigamaTitle,
            out var kusarigamaBody));
        Assert.Equal("Kusarigama", kusarigamaTitle);
        Assert.Contains("Attacks played", kusarigamaBody);

        Assert.True(RelicHoverShowPatch.TryBuildBodyBBCode(
            ornamentalFan,
            new RelicAggregate(),
            floorCount: null,
            out var ornamentalFanTitle,
            out var ornamentalFanBody));
        Assert.Equal("Ornamental Fan", ornamentalFanTitle);
        Assert.Contains("Attacks played", ornamentalFanBody);

        Assert.True(RelicHoverShowPatch.TryBuildBodyBBCode(
            shuriken,
            new RelicAggregate(),
            floorCount: null,
            out var shurikenTitle,
            out var shurikenBody));
        Assert.Equal("Shuriken", shurikenTitle);
        Assert.Contains("Attacks played", shurikenBody);
    }

    [Fact]
    public void RelicTooltips_UnlimitedAttackChargeRelics_ShowZeroRowsForEmptyAggregates()
    {
        var kusarigamaBody = BuildBody(BuildKusarigamaBodyMethod, new RelicAggregate());
        var ornamentalFanBody = BuildBody(BuildOrnamentalFanBodyMethod, new RelicAggregate());
        var shurikenBody = BuildBody(BuildShurikenBodyMethod, new RelicAggregate());

        Assert.Contains("Damage per activation", kusarigamaBody);
        Assert.Contains("Avg charge at turn end", kusarigamaBody);
        Assert.Contains("[b]0[/b]", kusarigamaBody);
        Assert.Contains("block gained per activation", ornamentalFanBody);
        Assert.Contains("Avg charge at turn end", ornamentalFanBody);
        Assert.Contains("[b]0[/b]", ornamentalFanBody);
        Assert.Contains("Strength gained per activation", shurikenBody);
        Assert.Contains("Avg charge at turn end", shurikenBody);
        Assert.Contains("[b]0[/b]", shurikenBody);
    }

    [Fact]
    public void RunData_OlderShapeWithoutUnlimitedAttackChargeFields_DeserializesWithZeroDefaults()
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
                "RELIC.KUSARIGAMA": {},
                "RELIC.ORNAMENTAL_FAN": {},
                "RELIC.SHURIKEN": {}
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        var kusarigama = run!.RelicAggregates[KusarigamaRelicId];
        Assert.Equal(0, kusarigama.KusarigamaAttacksPlayed);
        Assert.Equal(0, kusarigama.KusarigamaTurnsEndedAt1Charge);
        Assert.Equal(0, kusarigama.KusarigamaTurnsEndedAt2Charges);
        Assert.Equal(0, kusarigama.KusarigamaTurnEndChargeTotal);
        Assert.Equal(0, kusarigama.KusarigamaTurnEndChargeCount);
        Assert.Equal(0, kusarigama.TotalDamageAttempted);
        Assert.Equal(0, kusarigama.TotalDamageDealt);

        var ornamentalFan = run.RelicAggregates[OrnamentalFanRelicId];
        Assert.Equal(0, ornamentalFan.OrnamentalFanAttacksPlayed);
        Assert.Equal(0, ornamentalFan.OrnamentalFanTurnsEndedAt1Charge);
        Assert.Equal(0, ornamentalFan.OrnamentalFanTurnsEndedAt2Charges);
        Assert.Equal(0, ornamentalFan.OrnamentalFanTurnEndChargeTotal);
        Assert.Equal(0, ornamentalFan.OrnamentalFanTurnEndChargeCount);
        Assert.Equal(0, ornamentalFan.AdditionalBlockGained);

        var shuriken = run.RelicAggregates[ShurikenRelicId];
        Assert.Equal(0, shuriken.ShurikenAttacksPlayed);
        Assert.Equal(0, shuriken.ShurikenTurnsEndedAt1Charge);
        Assert.Equal(0, shuriken.ShurikenTurnsEndedAt2Charges);
        Assert.Equal(0, shuriken.ShurikenTurnEndChargeTotal);
        Assert.Equal(0, shuriken.ShurikenTurnEndChargeCount);
        Assert.Equal(0m, shuriken.StrengthAdded);
    }

    private static RunData BuildRepresentativeRun()
    {
        var run = new RunData();
        run.RelicAggregates[KusarigamaRelicId] = new RelicAggregate
        {
            KusarigamaAttacksPlayed = 14,
            Activations = 4,
            TotalDamageAttempted = 24,
            TotalDamageDealt = 17,
            TotalDamageBlocked = 3,
            TotalDamageOverkill = 4,
            Kills = 2,
            TotalTargets = 4,
            KusarigamaTurnsEndedAt1Charge = 2,
            KusarigamaTurnsEndedAt2Charges = 3,
            KusarigamaTurnEndChargeTotal = 8,
            KusarigamaTurnEndChargeCount = 7,
        };
        run.RelicAggregates[OrnamentalFanRelicId] = new RelicAggregate
        {
            OrnamentalFanAttacksPlayed = 11,
            Activations = 3,
            AdditionalBlockGained = 13,
            OrnamentalFanTurnsEndedAt1Charge = 1,
            OrnamentalFanTurnsEndedAt2Charges = 3,
            OrnamentalFanTurnEndChargeTotal = 7,
            OrnamentalFanTurnEndChargeCount = 5,
        };
        run.RelicAggregates[ShurikenRelicId] = new RelicAggregate
        {
            ShurikenAttacksPlayed = 17,
            Activations = 5,
            StrengthAdded = 5m,
            ShurikenTurnsEndedAt1Charge = 2,
            ShurikenTurnsEndedAt2Charges = 2,
            ShurikenTurnEndChargeTotal = 6,
            ShurikenTurnEndChargeCount = 6,
        };
        return run;
    }

    private static void AssertRepresentativeAggregates(RunData run)
    {
        var kusarigama = run.RelicAggregates[KusarigamaRelicId];
        Assert.Equal(14, kusarigama.KusarigamaAttacksPlayed);
        Assert.Equal(4, kusarigama.Activations);
        Assert.Equal(24, kusarigama.TotalDamageAttempted);
        Assert.Equal(17, kusarigama.TotalDamageDealt);
        Assert.Equal(3, kusarigama.TotalDamageBlocked);
        Assert.Equal(4, kusarigama.TotalDamageOverkill);
        Assert.Equal(2, kusarigama.Kills);
        Assert.Equal(4, kusarigama.TotalTargets);
        Assert.Equal(2, kusarigama.KusarigamaTurnsEndedAt1Charge);
        Assert.Equal(3, kusarigama.KusarigamaTurnsEndedAt2Charges);
        Assert.Equal(8, kusarigama.KusarigamaTurnEndChargeTotal);
        Assert.Equal(7, kusarigama.KusarigamaTurnEndChargeCount);

        var ornamentalFan = run.RelicAggregates[OrnamentalFanRelicId];
        Assert.Equal(11, ornamentalFan.OrnamentalFanAttacksPlayed);
        Assert.Equal(3, ornamentalFan.Activations);
        Assert.Equal(13, ornamentalFan.AdditionalBlockGained);
        Assert.Equal(1, ornamentalFan.OrnamentalFanTurnsEndedAt1Charge);
        Assert.Equal(3, ornamentalFan.OrnamentalFanTurnsEndedAt2Charges);
        Assert.Equal(7, ornamentalFan.OrnamentalFanTurnEndChargeTotal);
        Assert.Equal(5, ornamentalFan.OrnamentalFanTurnEndChargeCount);

        var shuriken = run.RelicAggregates[ShurikenRelicId];
        Assert.Equal(17, shuriken.ShurikenAttacksPlayed);
        Assert.Equal(5, shuriken.Activations);
        Assert.Equal(5m, shuriken.StrengthAdded);
        Assert.Equal(2, shuriken.ShurikenTurnsEndedAt1Charge);
        Assert.Equal(2, shuriken.ShurikenTurnsEndedAt2Charges);
        Assert.Equal(6, shuriken.ShurikenTurnEndChargeTotal);
        Assert.Equal(6, shuriken.ShurikenTurnEndChargeCount);
    }

    private static MethodInfo GetBuilder(string name)
        => typeof(RelicHoverShowPatch).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{name} not found.");

    private static string BuildBody(MethodInfo builder, RelicAggregate agg)
        => (string)(builder.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException($"{builder.Name} returned null."));
}
