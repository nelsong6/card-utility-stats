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
            TotalDamageAttempted = 27,
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
        Assert.Equal(2, restored.RelicAggregates["RELIC.PARRYING_SHIELD"].Activations);
        Assert.Equal(17, restored.RelicAggregates["RELIC.PARRYING_SHIELD"].TotalDamageAttempted);
        Assert.Equal(11, restored.RelicAggregates["RELIC.PARRYING_SHIELD"].TotalDamageDealt);
        Assert.Equal(4, restored.RelicAggregates["RELIC.PARRYING_SHIELD"].TotalDamageBlocked);
        Assert.Equal(2, restored.RelicAggregates["RELIC.PARRYING_SHIELD"].TotalDamageOverkill);
        Assert.Equal(1, restored.RelicAggregates["RELIC.PARRYING_SHIELD"].Kills);
        Assert.Equal(2, restored.RelicAggregates["RELIC.PARRYING_SHIELD"].TotalTargets);
        Assert.Equal(27, restored.RelicAggregates["RELIC.PEN_NIB"].TotalDamageAttempted);
        Assert.Equal(2, restored.RelicAggregates["RELIC.HORN_CLEAT"].Activations);
        Assert.Equal(24, restored.RelicAggregates["RELIC.HORN_CLEAT"].AdditionalBlockGained);
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
        Assert.Contains("Targets hit", letterOpenerBody);
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
            new RelicAggregate { Activations = 3, AdditionalCardsDrawn = 6 });
        Assert.Contains("Activations", pendulumBody);
        Assert.Contains("Cards drawn", pendulumBody);
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
        Assert.Contains("[b]17[/b]", parryingShieldBody);
        Assert.Contains("[b]11[/b]", parryingShieldBody);

        var penNibBody = InvokeTooltipBuilder(
            "BuildPenNibBodyBBCode",
            new RelicAggregate { TotalDamageAttempted = 27 });
        Assert.Contains("Base damage added", penNibBody);
        Assert.Contains("[b]27[/b]", penNibBody);

        var hornCleatBody = InvokeTooltipBuilder(
            "BuildHornCleatBodyBBCode",
            new RelicAggregate { Activations = 2, AdditionalBlockGained = 24 });
        Assert.Contains("Activations", hornCleatBody);
        Assert.Contains("block gained", hornCleatBody);
        Assert.Contains("[b]2[/b]", hornCleatBody);
        Assert.Contains("[b]24[/b]", hornCleatBody);
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
