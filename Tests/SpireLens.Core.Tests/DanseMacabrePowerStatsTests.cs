using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class DanseMacabrePowerStatsTests
{
    private const string DanseMacabrePowerId = "POWER.DANSE_MACABRE";

    private static readonly MethodInfo AppendDanseMacabrePowerStatsMethod =
        typeof(CardHoverShowPatch).GetMethod(
            "AppendDanseMacabrePowerStats",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "AppendDanseMacabrePowerStats not found.");

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void DanseMacabre_UsesTheExactDecimalGainBlockOverload()
    {
        var target = AccessTools.Method(
            typeof(CreatureCmd),
            nameof(CreatureCmd.GainBlock),
            new[]
            {
                typeof(Creature),
                typeof(decimal),
                typeof(ValueProp),
                typeof(CardPlay),
                typeof(bool),
            });

        Assert.NotNull(target);
    }

    [Theory]
    [InlineData(0, 2, false)]
    [InlineData(1, 2, false)]
    [InlineData(2, 2, true)]
    [InlineData(3, 2, true)]
    public void DanseMacabre_TriggerConditionUsesResolvedEnergyCost(
        int resolvedEnergyCost,
        int threshold,
        bool expected)
    {
        Assert.Equal(
            expected,
            RunTracker.DanseMacabreCardQualifiesForTest(
                resolvedEnergyCost,
                threshold));
    }

    [Fact]
    public void RunMetaStats_DanseMacabrePowerAggregate_DefaultsSafely()
    {
        var agg = new PowerAggregate();

        Assert.Equal(0, agg.TimesTriggered);
        Assert.Equal(0m, agg.BlockGained);
        Assert.Equal(0, agg.TurnsActive);
        Assert.Equal(0, agg.CombatsActive);
    }

    [Fact]
    public void RunTracker_DanseMacabreHelpers_RecordOnlyPositiveObservations()
    {
        var agg = new PowerAggregate();

        RunTracker.RecordDanseMacabreTriggerForTest(agg, 3);
        RunTracker.RecordDanseMacabreTriggerForTest(agg, 0);
        RunTracker.RecordDanseMacabreBlockGainedForTest(agg, 14m);
        RunTracker.RecordDanseMacabreBlockGainedForTest(agg, -2m);

        Assert.Equal(3, agg.TimesTriggered);
        Assert.Equal(14m, agg.BlockGained);
    }

    [Fact]
    public void RunTracker_Promotion_MergesDanseMacabrePowerAggregates()
    {
        var run = new RunData();
        run.MetaStats.PowerAggregates[DanseMacabrePowerId] = new PowerAggregate
        {
            PowerId = DanseMacabrePowerId,
            DisplayName = "Danse Macabre",
            TimesTriggered = 4,
            BlockGained = 20m,
            TurnsActive = 2,
            CombatsActive = 1,
        };
        var pending = new PendingCombat();
        pending.MetaStats.PowerAggregates[DanseMacabrePowerId] =
            new PowerAggregate
            {
                PowerId = DanseMacabrePowerId,
                DisplayName = "Danse Macabre",
                TimesTriggered = 5,
                BlockGained = 25m,
                TurnsActive = 4,
                CombatsActive = 2,
            };

        RunTracker.PromotePendingCombatIntoRun(pending, run);

        AssertRepresentativeAggregate(
            run.MetaStats.PowerAggregates[DanseMacabrePowerId]);
    }

    [Fact]
    public void RunMetaStats_DanseMacabrePowerAggregate_JsonRoundtripPreservesFields()
    {
        var run = new RunData();
        run.MetaStats.PowerAggregates[DanseMacabrePowerId] =
            CreateRepresentativeAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(
            json,
            RunStorage.Options);

        Assert.Contains("\"times_triggered\"", json);
        Assert.Contains("\"block_gained\"", json);
        Assert.NotNull(restored);
        AssertRepresentativeAggregate(
            restored!.MetaStats.PowerAggregates[DanseMacabrePowerId]);
    }

    [Fact]
    public void DanseMacabreTooltip_FullViewShowsSharedPowerTotalsAndAverages()
    {
        var body = AppendDanseMacabrePowerStats(
            CreateRepresentativeAggregate(),
            compact: false);

        Assert.Contains("Times triggered", body);
        Assert.Contains("Avg triggers per turn once active", body);
        Assert.Contains("[b]1.5[/b]", body);
        Assert.Contains("Avg triggers per combat", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("Block gained", body);
        Assert.Contains("[b]45[/b]", body);
        Assert.Contains("Avg block gained per turn once active", body);
        Assert.Contains("[b]7.5[/b]", body);
        Assert.Contains("Avg block gained per combat", body);
        Assert.Contains("[b]15[/b]", body);
    }

    [Fact]
    public void DanseMacabreTooltip_CompactViewKeepsOnlyTotals()
    {
        var body = AppendDanseMacabrePowerStats(
            CreateRepresentativeAggregate(),
            compact: true);

        Assert.Contains("Times triggered", body);
        Assert.Contains("Block gained", body);
        Assert.DoesNotContain("Avg triggers", body);
        Assert.DoesNotContain("Avg block", body);
    }

    private static PowerAggregate CreateRepresentativeAggregate() =>
        new()
        {
            PowerId = DanseMacabrePowerId,
            DisplayName = "Danse Macabre",
            TimesTriggered = 9,
            BlockGained = 45m,
            TurnsActive = 6,
            CombatsActive = 3,
        };

    private static void AssertRepresentativeAggregate(PowerAggregate agg)
    {
        Assert.Equal(DanseMacabrePowerId, agg.PowerId);
        Assert.Equal("Danse Macabre", agg.DisplayName);
        Assert.Equal(9, agg.TimesTriggered);
        Assert.Equal(45m, agg.BlockGained);
        Assert.Equal(6, agg.TurnsActive);
        Assert.Equal(3, agg.CombatsActive);
    }

    private static string AppendDanseMacabrePowerStats(
        PowerAggregate agg,
        bool compact)
    {
        var sb = new StringBuilder();
        var card = (DanseMacabre)RuntimeHelpers.GetUninitializedObject(
            typeof(DanseMacabre));
        var metaStats = new RunMetaStats();
        metaStats.PowerAggregates[DanseMacabrePowerId] = agg;
        _ = AppendDanseMacabrePowerStatsMethod.Invoke(
            null,
            new object?[] { sb, card, metaStats, compact });
        return sb.ToString();
    }
}
