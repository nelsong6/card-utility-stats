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

public class OpenBranchRelicStatsTests
{
    private static readonly MethodInfo IsAnchorStatsRelicModelMethod =
        typeof(RelicHoverShowPatch).GetMethod("IsAnchorStatsRelicModel", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("IsAnchorStatsRelicModel not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_OpenBranchFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.VigorGained);
        Assert.Equal(0, agg.TotalDamageAttempted);
        Assert.Equal(0, agg.TotalDamageDealt);
        Assert.Equal(0, agg.TotalDamageBlocked);
        Assert.Equal(0, agg.TotalDamageOverkill);
        Assert.Equal(0, agg.Kills);
        Assert.Equal(0, agg.TotalTargets);
        Assert.Equal(0, agg.UncommonCardsOffered);
        Assert.Equal(0, agg.RareCardsOffered);
        Assert.Equal(0, agg.UncommonCardsTaken);
        Assert.Equal(0, agg.RareCardsTaken);
        Assert.Equal(0, agg.PendulumCombats);
        Assert.Equal(0, agg.PenNibAttacksPlayed);
        Assert.Equal(0, agg.PenNibTurnsEndedOn8Charges);
        Assert.Equal(0, agg.PenNibTurnsEndedOn9Charges);
        Assert.Equal(0, agg.PenNibTurnEndChargeTotal);
        Assert.Equal(0, agg.PenNibTurnEndChargeCount);
    }

    [Fact]
    public void RelicAggregate_OpenBranchFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates["RELIC.AKABEKO"] = new RelicAggregate { VigorGained = 16 };
        run.RelicAggregates["RELIC.LETTER_OPENER"] = new RelicAggregate
        {
            Activations = 3,
            TotalDamageAttempted = 45,
            TotalTargets = 9,
        };
        run.RelicAggregates["RELIC.TOOLBOX"] = new RelicAggregate
        {
            Activations = 2,
            UncommonCardsOffered = 4,
            RareCardsOffered = 1,
            UncommonCardsTaken = 2,
            RareCardsTaken = 1,
        };
        run.RelicAggregates["RELIC.PENDULUM"] = new RelicAggregate
        {
            Activations = 3,
            AdditionalCardsDrawn = 6,
            PendulumCombats = 2,
        };
        run.RelicAggregates["RELIC.PARRYING_SHIELD"] = new RelicAggregate
        {
            Activations = 2,
            TotalDamageAttempted = 17,
            TotalDamageDealt = 11,
            TotalDamageBlocked = 4,
            TotalDamageOverkill = 2,
            Kills = 1,
            TotalTargets = 2,
        };
        run.RelicAggregates["RELIC.PEN_NIB"] = new RelicAggregate
        {
            Activations = 1,
            TotalDamageAttempted = 27,
            PenNibAttacksPlayed = 9,
            PenNibTurnsEndedOn8Charges = 2,
            PenNibTurnsEndedOn9Charges = 1,
            PenNibTurnEndChargeTotal = 34,
            PenNibTurnEndChargeCount = 5,
        };
        run.RelicAggregates["RELIC.HORN_CLEAT"] = new RelicAggregate
        {
            Activations = 2,
            AdditionalBlockGained = 24,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("vigor_gained", json);
        Assert.Contains("total_damage_attempted", json);
        Assert.Contains("total_targets", json);
        Assert.Contains("uncommon_cards_offered", json);
        Assert.Contains("rare_cards_offered", json);
        Assert.Contains("uncommon_cards_taken", json);
        Assert.Contains("rare_cards_taken", json);
        Assert.Contains("total_damage_dealt", json);
        Assert.Contains("total_damage_blocked", json);
        Assert.Contains("total_damage_overkill", json);
        Assert.Contains("kills", json);
        Assert.Contains("pendulum_combats", json);
        Assert.Contains("pen_nib_attacks_played", json);
        Assert.Contains("pen_nib_turns_ended_on8_charges", json);
        Assert.Contains("pen_nib_turns_ended_on9_charges", json);
        Assert.Contains("pen_nib_turn_end_charge_total", json);
        Assert.Contains("pen_nib_turn_end_charge_count", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        Assert.Equal(16, restored!.RelicAggregates["RELIC.AKABEKO"].VigorGained);
        Assert.Equal(3, restored.RelicAggregates["RELIC.LETTER_OPENER"].Activations);
        Assert.Equal(45, restored.RelicAggregates["RELIC.LETTER_OPENER"].TotalDamageAttempted);
        Assert.Equal(9, restored.RelicAggregates["RELIC.LETTER_OPENER"].TotalTargets);
        Assert.Equal(2, restored.RelicAggregates["RELIC.TOOLBOX"].Activations);
        Assert.Equal(4, restored.RelicAggregates["RELIC.TOOLBOX"].UncommonCardsOffered);
        Assert.Equal(1, restored.RelicAggregates["RELIC.TOOLBOX"].RareCardsOffered);
        Assert.Equal(2, restored.RelicAggregates["RELIC.TOOLBOX"].UncommonCardsTaken);
        Assert.Equal(1, restored.RelicAggregates["RELIC.TOOLBOX"].RareCardsTaken);
        Assert.Equal(3, restored.RelicAggregates["RELIC.PENDULUM"].Activations);
        Assert.Equal(6, restored.RelicAggregates["RELIC.PENDULUM"].AdditionalCardsDrawn);
        Assert.Equal(2, restored.RelicAggregates["RELIC.PENDULUM"].PendulumCombats);
        Assert.Equal(2, restored.RelicAggregates["RELIC.PARRYING_SHIELD"].Activations);
        Assert.Equal(17, restored.RelicAggregates["RELIC.PARRYING_SHIELD"].TotalDamageAttempted);
        Assert.Equal(11, restored.RelicAggregates["RELIC.PARRYING_SHIELD"].TotalDamageDealt);
        Assert.Equal(4, restored.RelicAggregates["RELIC.PARRYING_SHIELD"].TotalDamageBlocked);
        Assert.Equal(2, restored.RelicAggregates["RELIC.PARRYING_SHIELD"].TotalDamageOverkill);
        Assert.Equal(1, restored.RelicAggregates["RELIC.PARRYING_SHIELD"].Kills);
        Assert.Equal(2, restored.RelicAggregates["RELIC.PARRYING_SHIELD"].TotalTargets);
        Assert.Equal(1, restored.RelicAggregates["RELIC.PEN_NIB"].Activations);
        Assert.Equal(27, restored.RelicAggregates["RELIC.PEN_NIB"].TotalDamageAttempted);
        Assert.Equal(9, restored.RelicAggregates["RELIC.PEN_NIB"].PenNibAttacksPlayed);
        Assert.Equal(2, restored.RelicAggregates["RELIC.PEN_NIB"].PenNibTurnsEndedOn8Charges);
        Assert.Equal(1, restored.RelicAggregates["RELIC.PEN_NIB"].PenNibTurnsEndedOn9Charges);
        Assert.Equal(34, restored.RelicAggregates["RELIC.PEN_NIB"].PenNibTurnEndChargeTotal);
        Assert.Equal(5, restored.RelicAggregates["RELIC.PEN_NIB"].PenNibTurnEndChargeCount);
        Assert.Equal(2, restored.RelicAggregates["RELIC.HORN_CLEAT"].Activations);
        Assert.Equal(24, restored.RelicAggregates["RELIC.HORN_CLEAT"].AdditionalBlockGained);
    }

    [Fact]
    public void RunTracker_RecordPendulumCombatForTest_AccumulatesAndClamps()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordPendulumCombatForTest(agg, 2);
        RunTracker.RecordPendulumCombatForTest(agg, -1);

        Assert.Equal(2, agg.PendulumCombats);
    }

    [Fact]
    public void MergeRelicAggregateInto_PendulumCombats_Accumulates()
    {
        var target = new RelicAggregate { PendulumCombats = 2 };
        var source = new RelicAggregate { PendulumCombats = 3 };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(5, target.PendulumCombats);
    }

    [Fact]
    public void RunTracker_RecordPendulumActivationTurnForTest_SamplesEachActivation()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordPendulumActivationTurnForTest(agg, 3);
        RunTracker.RecordPendulumActivationTurnForTest(agg, 6);
        RunTracker.RecordPendulumActivationTurnForTest(agg, 3);
        RunTracker.RecordPendulumActivationTurnForTest(agg, 0);
        RunTracker.RecordPendulumActivationTurnForTest(agg, -2);

        Assert.Equal(12, agg.PendulumActivationTurnTotal);
        Assert.Equal(3, agg.PendulumActivationTurnSamples);
    }

    [Fact]
    public void MergeRelicAggregateInto_PendulumActivationTurn_Accumulates()
    {
        var target = new RelicAggregate
        {
            PendulumActivationTurnTotal = 9,
            PendulumActivationTurnSamples = 2,
        };
        var source = new RelicAggregate
        {
            PendulumActivationTurnTotal = 3,
            PendulumActivationTurnSamples = 1,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(12, target.PendulumActivationTurnTotal);
        Assert.Equal(3, target.PendulumActivationTurnSamples);
    }

    [Fact]
    public void RunTracker_RecordPenNibBaseDamageAdded_AccumulatesTruncatedBaseDamage()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordPenNibBaseDamageAddedForTest(agg, 9m);
        RunTracker.RecordPenNibBaseDamageAddedForTest(agg, 6.9m);
        RunTracker.RecordPenNibBaseDamageAddedForTest(agg, 0.9m);
        RunTracker.RecordPenNibBaseDamageAddedForTest(agg, -5m);

        Assert.Equal(15, agg.TotalDamageAttempted);
    }

    [Fact]
    public void RunTracker_RecordPenNibChargeStats_TracksAttackAndTurnEndBuckets()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordPenNibAttackPlayedForTest(agg, 9);
        RunTracker.RecordPenNibAttackPlayedForTest(
            agg,
            willActivate: true);
        RunTracker.RecordPenNibAttackPlayedForTest(agg, 10);
        RunTracker.RecordPenNibAttackPlayedForTest(agg, -2);
        RunTracker.RecordPenNibTurnEndChargeForTest(agg, 8);
        RunTracker.RecordPenNibTurnEndChargeForTest(agg, 9);
        RunTracker.RecordPenNibTurnEndChargeForTest(agg, 12);
        RunTracker.RecordPenNibTurnEndChargeForTest(agg, -1);

        Assert.Equal(20, agg.PenNibAttacksPlayed);
        Assert.Equal(2, agg.Activations);
        Assert.Equal(1, agg.PenNibTurnsEndedOn8Charges);
        Assert.Equal(1, agg.PenNibTurnsEndedOn9Charges);
        Assert.Equal(19, agg.PenNibTurnEndChargeTotal);
        Assert.Equal(3, agg.PenNibTurnEndChargeCount);
    }

    [Fact]
    public void PenNib_ResolveBaseDamageAmount_PrefersRawDamageFrame()
    {
        try
        {
            PenNibModifyDamageMultiplicativePatch.PushDamageFrame(null, null, 9m);

            Assert.Equal(
                9m,
                PenNibModifyDamageMultiplicativePatch.ResolveBaseDamageAmount(
                    null,
                    null,
                    13.5m));
        }
        finally
        {
            PenNibModifyDamageMultiplicativePatch.PopDamageFrame();
        }
    }

    [Fact]
    public void PenNib_ResolveBaseDamageAmount_FallsBackWithoutRawDamageFrame()
    {
        Assert.Equal(
            13.5m,
            PenNibModifyDamageMultiplicativePatch.ResolveBaseDamageAmount(
                null,
                null,
                13.5m));
    }

    [Fact]
    public void RelicTooltip_AnchorModelRecognition_IncludesFakeAnchor()
    {
        var real = (bool)(IsAnchorStatsRelicModelMethod.Invoke(null, new object[] { Uninitialized<Anchor>() })
            ?? throw new InvalidOperationException("IsAnchorStatsRelicModel returned null."));
        var fake = (bool)(IsAnchorStatsRelicModelMethod.Invoke(null, new object[] { Uninitialized<FakeAnchor>() })
            ?? throw new InvalidOperationException("IsAnchorStatsRelicModel returned null."));

        Assert.True(real);
        Assert.True(fake);
    }

    [Fact]
    public void RelicTooltip_OpenBranchRelics_ShowExpectedRows()
    {
        var anchorBody = InvokeTooltipBuilder(
            "BuildAnchorBodyBBCode",
            new RelicAggregate { Activations = 2, AdditionalBlockGained = 20 });
        Assert.Contains("Activations", anchorBody);
        Assert.Contains("block gained", anchorBody);
        Assert.Contains("[b]20[/b]", anchorBody);

        var letterOpenerBody = InvokeTooltipBuilder(
            "BuildLetterOpenerBodyBBCode",
            new RelicAggregate { Activations = 3, TotalDamageAttempted = 45, TotalTargets = 9 });
        Assert.Contains("Damage attempted", letterOpenerBody);
        Assert.Contains(
            StatConceptGlossary.RenderHintedGlyph("targets_hit"),
            letterOpenerBody);
        Assert.Contains("[b]45[/b]", letterOpenerBody);

        var akabekoBody = InvokeTooltipBuilder(
            "BuildAkabekoBodyBBCode",
            new RelicAggregate { VigorGained = 16 });
        Assert.Contains("vigor gained", akabekoBody);
        Assert.Contains("[b]16[/b]", akabekoBody);

        var boomingConchBody = InvokeTooltipBuilder(
            "BuildBoomingConchBodyBBCode",
            new RelicAggregate { EnergyGenerated = 2, AdditionalCardsDrawn = 4 });
        Assert.Contains("Energy generated", boomingConchBody);
        Assert.Contains("Cards drawn", boomingConchBody);
        Assert.Contains("[b]4[/b]", boomingConchBody);

        var bloodVialBody = InvokeTooltipBuilder(
            "BuildBloodVialBodyBBCode",
            new RelicAggregate { Activations = 2, TotalHealingRestored = 3, TotalHealingLost = 1 });
        Assert.Contains("HP healed", bloodVialBody);
        Assert.Contains("healing lost", bloodVialBody);

        var toolboxBody = InvokeTooltipBuilder(
            "BuildToolboxBodyBBCode",
            new RelicAggregate
            {
                Activations = 2,
                UncommonCardsOffered = 4,
                RareCardsOffered = 1,
                UncommonCardsTaken = 2,
                RareCardsTaken = 1,
            });
        Assert.Contains("Activations", toolboxBody);
        Assert.Contains("Uncommon cards offered", toolboxBody);
        Assert.Contains("Rare cards offered", toolboxBody);
        Assert.Contains("Uncommon cards taken", toolboxBody);
        Assert.Contains("Rare cards taken", toolboxBody);
        Assert.Contains("[b]4[/b]", toolboxBody);
        Assert.Contains("[b]2[/b]", toolboxBody);
        Assert.Contains("[b]1[/b]", toolboxBody);

        var pendulumBody = InvokeTooltipBuilder(
            "BuildPendulumBodyBBCode",
            new RelicAggregate { Activations = 4, AdditionalCardsDrawn = 6, PendulumCombats = 2 });
        Assert.Contains("Activations", pendulumBody);
        Assert.Contains("Cards drawn", pendulumBody);
        Assert.Contains("Avg cards drawn per combat", pendulumBody);
        Assert.Contains("[b]4[/b]", pendulumBody);
        Assert.Contains("[b]3[/b]", pendulumBody);
        Assert.Contains("[b]6[/b]", pendulumBody);

        var parryingShieldBody = InvokeTooltipBuilder(
            "BuildParryingShieldBodyBBCode",
            new RelicAggregate
            {
                Activations = 2,
                TotalDamageAttempted = 17,
                TotalDamageDealt = 11,
                TotalDamageBlocked = 4,
                TotalDamageOverkill = 2,
                Kills = 1,
            });
        Assert.Contains("Activations", parryingShieldBody);
        Assert.Contains("Damage attempted", parryingShieldBody);
        Assert.Contains("Damage dealt", parryingShieldBody);
        Assert.Contains("Damage blocked", parryingShieldBody);
        Assert.Contains("Overkill", parryingShieldBody);
        Assert.Contains("Kills", parryingShieldBody);
        Assert.Contains(
            StatConceptGlossary.RenderHintedGlyph("targets_hit"),
            parryingShieldBody);
        Assert.Contains("Damage per activation", parryingShieldBody);
        Assert.Contains("[b]17[/b]", parryingShieldBody);
        Assert.Contains("[b]11[/b]", parryingShieldBody);

        var penNibBody = InvokeTooltipBuilder(
            "BuildPenNibBodyBBCode",
            new RelicAggregate
            {
                Activations = 1,
                TotalDamageAttempted = 27,
                PenNibAttacksPlayed = 9,
                PenNibTurnsEndedOn8Charges = 2,
                PenNibTurnsEndedOn9Charges = 1,
                PenNibTurnEndChargeTotal = 34,
                PenNibTurnEndChargeCount = 5,
            });
        Assert.Contains(
            StatConceptGlossary.RenderHintedGlyph("activation"),
            penNibBody);
        Assert.Contains("Base damage added", penNibBody);
        Assert.Contains("Avg base damage added per attack", penNibBody);
        Assert.Contains("The number of Attack cards played while this relic was held.", penNibBody);
        Assert.Contains("Turns ended on 8 charges", penNibBody);
        Assert.Contains("Turns ended on 9 charges", penNibBody);
        Assert.Contains("Avg charge at turn end", penNibBody);
        Assert.Contains("[b]27[/b]", penNibBody);
        Assert.Contains("[b]9[/b]", penNibBody);
        Assert.Contains("[b]6.8[/b]", penNibBody);

        var hornCleatBody = InvokeTooltipBuilder(
            "BuildHornCleatBodyBBCode",
            new RelicAggregate { Activations = 2, AdditionalBlockGained = 24 });
        Assert.Contains("Activations", hornCleatBody);
        Assert.Contains("block gained", hornCleatBody);
        Assert.Contains("[b]2[/b]", hornCleatBody);
        Assert.Contains("[b]24[/b]", hornCleatBody);

        var captainsWheelBody = InvokeTooltipBuilder(
            "BuildCaptainsWheelBodyBBCode",
            new RelicAggregate { Activations = 3, AdditionalBlockGained = 54 });
        Assert.Contains("[b]3[/b]", captainsWheelBody);
        Assert.Contains("[b]54[/b]", captainsWheelBody);
    }

    private static string InvokeTooltipBuilder(string methodName, RelicAggregate agg)
    {
        var method = typeof(RelicHoverShowPatch).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (string)(method!.Invoke(null, new object[] { agg })
            ?? throw new InvalidOperationException($"{methodName} returned null."));
    }

    private static T Uninitialized<T>() where T : class
    {
        return (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    }
}
