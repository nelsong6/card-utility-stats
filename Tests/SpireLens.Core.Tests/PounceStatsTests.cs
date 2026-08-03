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

public class PounceStatsTests
{
    private const string FreeSkillPowerId = "POWER.FREE_SKILL_POWER";

    private static readonly MethodInfo AppendPounceFreeSkillStatsMethod =
        typeof(CardHoverShowPatch).GetMethod(
            "AppendPounceFreeSkillStats",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("AppendPounceFreeSkillStats not found.");

    [Fact]
    public void Patches_TargetFreeSkillPowerMethodsWithExpectedParameters()
    {
        var costTarget = typeof(FreeSkillPower).GetMethod(
            nameof(FreeSkillPower.TryModifyEnergyCostInCombatLate));
        var useTarget = typeof(FreeSkillPower).GetMethod(
            nameof(FreeSkillPower.BeforeCardPlayed));

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
    public void PowerAggregate_FreeSkillFields_DefaultAndRoundtripCorrectly()
    {
        var empty = new PowerAggregate();
        Assert.Equal(0, empty.FreeSkillChargesGranted);
        Assert.Equal(0, empty.FreeSkillChargesUsed);
        Assert.Equal(0, empty.FreeSkillZeroEnergySavingsUses);
        Assert.Equal(0m, empty.FreeSkillEnergySaved);
        Assert.Equal(0, empty.FreeSkillBasicSkillsDiscounted);
        Assert.Equal(0, empty.FreeSkillCommonSkillsDiscounted);
        Assert.Equal(0, empty.FreeSkillUncommonSkillsDiscounted);
        Assert.Equal(0, empty.FreeSkillRareSkillsDiscounted);

        var run = new RunData();
        run.MetaStats.PowerAggregates[FreeSkillPowerId] = CreateRepresentativeAggregate();
        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"free_skill_charges_granted\"", json);
        Assert.Contains("\"free_skill_energy_saved\"", json);
        Assert.Contains("\"free_skill_rare_skills_discounted\"", json);
        Assert.NotNull(restored);
        AssertRepresentativeAggregate(
            restored!.MetaStats.PowerAggregates[FreeSkillPowerId]);
    }

    [Fact]
    public void RunTracker_FreeSkillHelpers_AccumulateUsageSavingsAndRarities()
    {
        var agg = new PowerAggregate
        {
            PowerId = FreeSkillPowerId,
            DisplayName = "Free Skill",
        };

        RunTracker.RecordFreeSkillGrantForTest(agg, 10);
        RunTracker.RecordFreeSkillGrantForTest(agg, -1);
        RunTracker.RecordFreeSkillUseForTest(agg, 2m, CardRarity.Basic);
        RunTracker.RecordFreeSkillUseForTest(agg, 1m, CardRarity.Basic);
        RunTracker.RecordFreeSkillUseForTest(agg, 0m, CardRarity.Common);
        RunTracker.RecordFreeSkillUseForTest(agg, -2m, CardRarity.Common);
        RunTracker.RecordFreeSkillUseForTest(agg, 2m, CardRarity.Uncommon);
        RunTracker.RecordFreeSkillUseForTest(agg, 3m, CardRarity.Uncommon);
        RunTracker.RecordFreeSkillUseForTest(agg, 2m, CardRarity.Uncommon);
        RunTracker.RecordFreeSkillUseForTest(agg, 3m, CardRarity.Rare);

        AssertRepresentativeAggregate(agg);
    }

    [Fact]
    public void RunTracker_Promotion_MergesFreeSkillPowerAggregates()
    {
        var run = new RunData();
        run.MetaStats.PowerAggregates[FreeSkillPowerId] = new PowerAggregate
        {
            PowerId = FreeSkillPowerId,
            DisplayName = "Free Skill",
            FreeSkillChargesGranted = 4,
            FreeSkillChargesUsed = 3,
            FreeSkillZeroEnergySavingsUses = 1,
            FreeSkillEnergySaved = 5m,
            FreeSkillBasicSkillsDiscounted = 1,
            FreeSkillCommonSkillsDiscounted = 1,
            FreeSkillUncommonSkillsDiscounted = 1,
        };
        var pending = new PendingCombat();
        pending.MetaStats.PowerAggregates[FreeSkillPowerId] = new PowerAggregate
        {
            PowerId = FreeSkillPowerId,
            DisplayName = "Free Skill",
            FreeSkillChargesGranted = 6,
            FreeSkillChargesUsed = 5,
            FreeSkillZeroEnergySavingsUses = 1,
            FreeSkillEnergySaved = 8m,
            FreeSkillBasicSkillsDiscounted = 1,
            FreeSkillCommonSkillsDiscounted = 1,
            FreeSkillUncommonSkillsDiscounted = 2,
            FreeSkillRareSkillsDiscounted = 1,
        };

        RunTracker.PromotePendingCombatIntoRun(pending, run);

        AssertRepresentativeAggregate(
            run.MetaStats.PowerAggregates[FreeSkillPowerId]);
    }

    [Fact]
    public void PounceTooltip_MirrorsUnrelentingDiscountRows()
    {
        var full = AppendPounceFreeSkillStats(
            CreateRepresentativeAggregate(),
            compact: false);

        Assert.Contains("Free Skill charges used/granted", full);
        Assert.Contains("[b]8/10[/b]", full);
        Assert.Contains("80%", full);
        Assert.Contains("total saved", full);
        Assert.Contains("charges used with 0 saved", full);
        Assert.Contains("avg saved per charge used", full);
        Assert.Contains("[b]1.63[/b]", full);
        Assert.Contains("Basic Skills discounted", full);
        Assert.Contains("Common Skills discounted", full);
        Assert.Contains("Uncommon Skills discounted", full);
        Assert.Contains("Rare Skills discounted", full);

        var compact = AppendPounceFreeSkillStats(
            CreateRepresentativeAggregate(),
            compact: true);
        Assert.Contains("Free Skill charges used/granted", compact);
        Assert.Contains("total saved", compact);
        Assert.DoesNotContain("charges used with 0 saved", compact);
        Assert.DoesNotContain("Skills discounted", compact);
    }

    private static PowerAggregate CreateRepresentativeAggregate()
        => new()
        {
            PowerId = FreeSkillPowerId,
            DisplayName = "Free Skill",
            FreeSkillChargesGranted = 10,
            FreeSkillChargesUsed = 8,
            FreeSkillZeroEnergySavingsUses = 2,
            FreeSkillEnergySaved = 13m,
            FreeSkillBasicSkillsDiscounted = 2,
            FreeSkillCommonSkillsDiscounted = 2,
            FreeSkillUncommonSkillsDiscounted = 3,
            FreeSkillRareSkillsDiscounted = 1,
        };

    private static void AssertRepresentativeAggregate(PowerAggregate agg)
    {
        Assert.Equal(FreeSkillPowerId, agg.PowerId);
        Assert.Equal("Free Skill", agg.DisplayName);
        Assert.Equal(10, agg.FreeSkillChargesGranted);
        Assert.Equal(8, agg.FreeSkillChargesUsed);
        Assert.Equal(2, agg.FreeSkillZeroEnergySavingsUses);
        Assert.Equal(13m, agg.FreeSkillEnergySaved);
        Assert.Equal(2, agg.FreeSkillBasicSkillsDiscounted);
        Assert.Equal(2, agg.FreeSkillCommonSkillsDiscounted);
        Assert.Equal(3, agg.FreeSkillUncommonSkillsDiscounted);
        Assert.Equal(1, agg.FreeSkillRareSkillsDiscounted);
    }

    private static string AppendPounceFreeSkillStats(
        PowerAggregate agg,
        bool compact)
    {
        var sb = new StringBuilder();
        var card = (Pounce)RuntimeHelpers.GetUninitializedObject(typeof(Pounce));
        var metaStats = new RunMetaStats();
        metaStats.PowerAggregates[FreeSkillPowerId] = agg;
        _ = AppendPounceFreeSkillStatsMethod.Invoke(
            null,
            new object?[] { sb, card, metaStats, compact });
        return sb.ToString();
    }
}
