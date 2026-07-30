using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class FeelNoPainPowerStatsTests
{
    private const string FeelNoPainPowerId = "POWER.FEEL_NO_PAIN";

    private static readonly MethodInfo AppendFeelNoPainPowerStatsMethod =
        typeof(CardHoverShowPatch).GetMethod(
            "AppendFeelNoPainPowerStats",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "AppendFeelNoPainPowerStats not found.");

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void FeelNoPain_UsesExactPowerAndGainBlockTargets()
    {
        var powerTarget = AccessTools.Method(
            typeof(FeelNoPainPower),
            nameof(FeelNoPainPower.AfterCardExhausted),
            new[]
            {
                typeof(PlayerChoiceContext),
                typeof(CardModel),
                typeof(bool),
            });
        var blockTarget = AccessTools.Method(
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

        Assert.NotNull(powerTarget);
        Assert.NotNull(blockTarget);
    }

    [Fact]
    public void RunTracker_FeelNoPainHelper_RecordsOnlyPositiveBlock()
    {
        var agg = new PowerAggregate();

        RunTracker.RecordFeelNoPainBlockGainedForTest(agg, 36m);
        RunTracker.RecordFeelNoPainBlockGainedForTest(agg, 0m);
        RunTracker.RecordFeelNoPainBlockGainedForTest(agg, -4m);

        Assert.Equal(36m, agg.BlockGained);
    }

    [Fact]
    public void Promotion_MergesFeelNoPainNumeratorAndDenominator()
    {
        var run = new RunData();
        run.MetaStats.PowerAggregates[FeelNoPainPowerId] =
            CreateAggregate(block: 12m, turns: 2);
        var pending = new PendingCombat();
        pending.MetaStats.PowerAggregates[FeelNoPainPowerId] =
            CreateAggregate(block: 24m, turns: 4);

        RunTracker.PromotePendingCombatIntoRun(pending, run);

        var agg = run.MetaStats.PowerAggregates[FeelNoPainPowerId];
        Assert.Equal(36m, agg.BlockGained);
        Assert.Equal(6, agg.TurnsActive);
    }

    [Fact]
    public void FeelNoPainTooltip_ShowsBlockPerActiveTurn()
    {
        var sb = new StringBuilder();
        var card = (FeelNoPain)RuntimeHelpers.GetUninitializedObject(
            typeof(FeelNoPain));
        var metaStats = new RunMetaStats();
        metaStats.PowerAggregates[FeelNoPainPowerId] =
            CreateAggregate(block: 36m, turns: 6);

        _ = AppendFeelNoPainPowerStatsMethod.Invoke(
            null,
            new object?[] { sb, card, metaStats });

        var body = sb.ToString();
        Assert.Contains("added / active turn", body);
        Assert.Contains("[b]6[/b]", body);
    }

    private static PowerAggregate CreateAggregate(decimal block, int turns) =>
        new()
        {
            PowerId = FeelNoPainPowerId,
            DisplayName = "Feel No Pain",
            BlockGained = block,
            TurnsActive = turns,
        };
}
