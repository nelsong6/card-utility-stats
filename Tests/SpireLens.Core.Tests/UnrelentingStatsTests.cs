using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class UnrelentingStatsTests
{
    private const string FreeAttackPowerId = "POWER.FREE_ATTACK_POWER";

    private static readonly MethodInfo AppendUnrelentingFreeAttackStatsMethod =
        typeof(CardHoverShowPatch).GetMethod(
            "AppendUnrelentingFreeAttackStats",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("AppendUnrelentingFreeAttackStats not found.");

    [Fact]
    public void Patches_TargetFreeAttackPowerMethodsWithExpectedParameters()
    {
        var costTarget = typeof(FreeAttackPower).GetMethod(
            nameof(FreeAttackPower.TryModifyEnergyCostInCombatLate));
        var useTarget = typeof(FreeAttackPower).GetMethod(
            nameof(FreeAttackPower.BeforeCardPlayed));

        Assert.NotNull(costTarget);
        Assert.Equal(
            new[] { typeof(CardModel), typeof(decimal), typeof(decimal).MakeByRefType() },
            costTarget!.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.NotNull(useTarget);
        Assert.Equal(
            new[] { typeof(CardPlay) },
            useTarget!.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void PowerAggregate_FreeAttackFields_DefaultToZero()
    {
        var agg = new PowerAggregate();

        Assert.Equal(0, agg.FreeAttackChargesGranted);
        Assert.Equal(0, agg.FreeAttackChargesUsed);
        Assert.Equal(0, agg.FreeAttackZeroEnergySavingsUses);
        Assert.Equal(0m, agg.FreeAttackEnergySaved);
        Assert.Equal(0, agg.FreeAttackBasicAttacksDiscounted);
        Assert.Equal(0, agg.FreeAttackCommonAttacksDiscounted);
        Assert.Equal(0, agg.FreeAttackUncommonAttacksDiscounted);
        Assert.Equal(0, agg.FreeAttackRareAttacksDiscounted);
    }

    [Fact]
    public void PowerAggregate_FreeAttackFields_JsonRoundtripPreservesValues()
    {
        var run = new RunData();
        run.MetaStats.PowerAggregates[FreeAttackPowerId] = CreateRepresentativeAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"free_attack_charges_granted\"", json);
        Assert.Contains("\"free_attack_charges_used\"", json);
        Assert.Contains("\"free_attack_zero_energy_savings_uses\"", json);
        Assert.Contains("\"free_attack_energy_saved\"", json);
        Assert.Contains("\"free_attack_basic_attacks_discounted\"", json);
        Assert.Contains("\"free_attack_common_attacks_discounted\"", json);
        Assert.Contains("\"free_attack_uncommon_attacks_discounted\"", json);
        Assert.Contains("\"free_attack_rare_attacks_discounted\"", json);
        Assert.NotNull(restored);
        AssertRepresentativeAggregate(
            restored!.MetaStats.PowerAggregates[FreeAttackPowerId]);
    }

    [Fact]
    public void RunTracker_FreeAttackHelpers_AccumulateUsageSavingsAndRarities()
    {
        var agg = new PowerAggregate
        {
            PowerId = FreeAttackPowerId,
            DisplayName = "Free Attack",
        };

        RunTracker.RecordFreeAttackGrantForTest(agg, 10);
        RunTracker.RecordFreeAttackGrantForTest(agg, -1);
        RunTracker.RecordFreeAttackUseForTest(agg, 2m, CardRarity.Basic);
        RunTracker.RecordFreeAttackUseForTest(agg, 1m, CardRarity.Basic);
        RunTracker.RecordFreeAttackUseForTest(agg, 0m, CardRarity.Common);
        RunTracker.RecordFreeAttackUseForTest(agg, -2m, CardRarity.Common);
        RunTracker.RecordFreeAttackUseForTest(agg, 2m, CardRarity.Uncommon);
        RunTracker.RecordFreeAttackUseForTest(agg, 3m, CardRarity.Uncommon);
        RunTracker.RecordFreeAttackUseForTest(agg, 2m, CardRarity.Uncommon);
        RunTracker.RecordFreeAttackUseForTest(agg, 3m, CardRarity.Rare);

        AssertRepresentativeAggregate(agg);
    }

    [Fact]
    public void RunTracker_Promotion_MergesFreeAttackPowerAggregates()
    {
        var run = new RunData();
        run.MetaStats.PowerAggregates[FreeAttackPowerId] = new PowerAggregate
        {
            PowerId = FreeAttackPowerId,
            DisplayName = "Free Attack",
            FreeAttackChargesGranted = 4,
            FreeAttackChargesUsed = 3,
            FreeAttackZeroEnergySavingsUses = 1,
            FreeAttackEnergySaved = 5m,
            FreeAttackBasicAttacksDiscounted = 1,
            FreeAttackCommonAttacksDiscounted = 1,
            FreeAttackUncommonAttacksDiscounted = 1,
        };
        var pending = new PendingCombat();
        pending.MetaStats.PowerAggregates[FreeAttackPowerId] = new PowerAggregate
        {
            PowerId = FreeAttackPowerId,
            DisplayName = "Free Attack",
            FreeAttackChargesGranted = 6,
            FreeAttackChargesUsed = 5,
            FreeAttackZeroEnergySavingsUses = 1,
            FreeAttackEnergySaved = 8m,
            FreeAttackBasicAttacksDiscounted = 1,
            FreeAttackCommonAttacksDiscounted = 1,
            FreeAttackUncommonAttacksDiscounted = 2,
            FreeAttackRareAttacksDiscounted = 1,
        };

        RunTracker.PromotePendingCombatIntoRun(pending, run);

        AssertRepresentativeAggregate(run.MetaStats.PowerAggregates[FreeAttackPowerId]);
    }

    [Fact]
    public void UnrelentingTooltip_FullViewShowsUtilizationSavingsAndRarityBreakdown()
    {
        var body = AppendUnrelentingFreeAttackStats(
            CreateRepresentativeAggregate(),
            compact: false);

        Assert.Contains("Free Attack charges used/granted", body);
        Assert.Contains("[b]8/10[/b]", body);
        Assert.Contains("[b]80%[/b]", body);
        Assert.Contains("total saved", body);
        Assert.Contains(
            "[img=16x16]res://images/packed/sprite_fonts/ironclad_energy_icon.png[/img] charges used with 0 saved",
            body);
        Assert.Contains("avg saved per charge used", body);
        Assert.Contains("[b]1.63[/b]", body);
        Assert.Contains("Basic Attacks discounted", body);
        Assert.Contains("Common Attacks discounted", body);
        Assert.Contains("Uncommon Attacks discounted", body);
        Assert.Contains("Rare Attacks discounted", body);
    }

    [Fact]
    public void UnrelentingTooltip_CompactViewKeepsUsageAndTotalSavingsOnly()
    {
        var body = AppendUnrelentingFreeAttackStats(
            CreateRepresentativeAggregate(),
            compact: true);

        Assert.Contains("Free Attack charges used/granted", body);
        Assert.Contains("total saved", body);
        Assert.DoesNotContain("charges used with 0 saved", body);
        Assert.DoesNotContain("avg saved per charge used", body);
        Assert.DoesNotContain("Attacks discounted", body);
    }

    [Fact]
    public void PowerAggregate_OlderShapeWithoutFreeAttackFields_DefaultsToZero()
    {
        var agg = JsonSerializer.Deserialize<PowerAggregate>("{}", RunStorage.Options);

        Assert.NotNull(agg);
        Assert.Equal(0, agg!.FreeAttackChargesGranted);
        Assert.Equal(0, agg.FreeAttackChargesUsed);
        Assert.Equal(0, agg.FreeAttackZeroEnergySavingsUses);
        Assert.Equal(0m, agg.FreeAttackEnergySaved);
        Assert.Equal(0, agg.FreeAttackBasicAttacksDiscounted);
        Assert.Equal(0, agg.FreeAttackCommonAttacksDiscounted);
        Assert.Equal(0, agg.FreeAttackUncommonAttacksDiscounted);
        Assert.Equal(0, agg.FreeAttackRareAttacksDiscounted);
    }

    private static PowerAggregate CreateRepresentativeAggregate()
        => new()
        {
            PowerId = FreeAttackPowerId,
            DisplayName = "Free Attack",
            FreeAttackChargesGranted = 10,
            FreeAttackChargesUsed = 8,
            FreeAttackZeroEnergySavingsUses = 2,
            FreeAttackEnergySaved = 13m,
            FreeAttackBasicAttacksDiscounted = 2,
            FreeAttackCommonAttacksDiscounted = 2,
            FreeAttackUncommonAttacksDiscounted = 3,
            FreeAttackRareAttacksDiscounted = 1,
        };

    private static void AssertRepresentativeAggregate(PowerAggregate agg)
    {
        Assert.Equal(FreeAttackPowerId, agg.PowerId);
        Assert.Equal("Free Attack", agg.DisplayName);
        Assert.Equal(10, agg.FreeAttackChargesGranted);
        Assert.Equal(8, agg.FreeAttackChargesUsed);
        Assert.Equal(2, agg.FreeAttackZeroEnergySavingsUses);
        Assert.Equal(13m, agg.FreeAttackEnergySaved);
        Assert.Equal(2, agg.FreeAttackBasicAttacksDiscounted);
        Assert.Equal(2, agg.FreeAttackCommonAttacksDiscounted);
        Assert.Equal(3, agg.FreeAttackUncommonAttacksDiscounted);
        Assert.Equal(1, agg.FreeAttackRareAttacksDiscounted);
    }

    private static string AppendUnrelentingFreeAttackStats(
        PowerAggregate agg,
        bool compact)
    {
        var sb = new StringBuilder();
        var card = (Unrelenting)RuntimeHelpers.GetUninitializedObject(typeof(Unrelenting));
        var metaStats = new RunMetaStats();
        metaStats.PowerAggregates[FreeAttackPowerId] = agg;
        _ = AppendUnrelentingFreeAttackStatsMethod.Invoke(
            null,
            new object?[] { sb, card, metaStats, compact });
        return sb.ToString();
    }
}
