using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Cards;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class DebtStatsTests
{
    private const string DebtCardId = "CARD.DEBT";

    private static readonly MethodInfo TargetMethod =
        typeof(DebtStatsPatch).GetMethod("TargetMethod", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Debt TargetMethod not found.");

    private static readonly MethodInfo AppendDebtStatsMethod =
        typeof(CardHoverShowPatch).GetMethod(
            "AppendDebtStats",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("AppendDebtStats not found.");

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void Patch_TargetsDebtTurnEndInHandWithExpectedParameter()
    {
        var target = TargetMethod.Invoke(null, null) as MethodBase;

        Assert.NotNull(target);
        Assert.Equal(typeof(Debt), target!.DeclaringType);
        Assert.Equal("OnTurnEndInHand", target.Name);
        Assert.Equal(
            new[] { typeof(PlayerChoiceContext) },
            target.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void CardAggregate_DebtFields_DefaultToZero()
    {
        var agg = new CardAggregate();

        Assert.Equal(0, agg.DebtTriggers);
        Assert.Equal(0, agg.DebtGoldLost);
        Assert.Equal(0, agg.DebtGoldLossBlocked);
    }

    [Fact]
    public void CardAggregate_DebtFields_JsonRoundtripPreservesValues()
    {
        var run = new RunData();
        run.Aggregates[$"{DebtCardId}#1"] = new CardAggregate
        {
            DebtTriggers = 4,
            DebtGoldLost = 13,
            DebtGoldLossBlocked = 7,
        };

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"debt_triggers\"", json);
        Assert.Contains("\"debt_gold_lost\"", json);
        Assert.Contains("\"debt_gold_loss_blocked\"", json);
        Assert.NotNull(restored);

        var agg = restored!.Aggregates[$"{DebtCardId}#1"];
        Assert.Equal(4, agg.DebtTriggers);
        Assert.Equal(13, agg.DebtGoldLost);
        Assert.Equal(7, agg.DebtGoldLossBlocked);
    }

    [Fact]
    public void RunTracker_Debt_CountsActualLossAndUnaffordableRemainder()
    {
        var agg = new CardAggregate();

        RunTracker.RecordDebtTriggerForTest(agg, intendedGoldLoss: 5, initialGold: 50, finalGold: 45);
        RunTracker.RecordDebtTriggerForTest(agg, intendedGoldLoss: 5, initialGold: 3, finalGold: 0);
        RunTracker.RecordDebtTriggerForTest(agg, intendedGoldLoss: 5, initialGold: 0, finalGold: 0);

        Assert.Equal(3, agg.DebtTriggers);
        Assert.Equal(8, agg.DebtGoldLost);
        Assert.Equal(7, agg.DebtGoldLossBlocked);
    }

    [Fact]
    public void CardAggregatePooler_DebtFields_MergeAcrossInstances()
    {
        var pooled = CardAggregatePooler.PoolByDefinition(
            new Dictionary<string, CardAggregate>
            {
                [$"{DebtCardId}#1"] = new()
                {
                    DebtTriggers = 2,
                    DebtGoldLost = 7,
                    DebtGoldLossBlocked = 3,
                },
                [$"{DebtCardId}#2"] = new()
                {
                    DebtTriggers = 3,
                    DebtGoldLost = 6,
                    DebtGoldLossBlocked = 9,
                },
                ["CARD.PAIN#1"] = new()
                {
                    DebtTriggers = 99,
                    DebtGoldLost = 99,
                    DebtGoldLossBlocked = 99,
                },
            },
            DebtCardId);

        Assert.NotNull(pooled);
        Assert.Equal(5, pooled!.DebtTriggers);
        Assert.Equal(13, pooled.DebtGoldLost);
        Assert.Equal(12, pooled.DebtGoldLossBlocked);
    }

    [Fact]
    public void DebtTooltip_ShowsAttemptedLossAsFivePerTriggerAndOutcomeRows()
    {
        var agg = new CardAggregate
        {
            DebtTriggers = 4,
            DebtGoldLost = 13,
            DebtGoldLossBlocked = 7,
        };

        var body = AppendDebtStats(agg);

        Assert.Contains("Times triggered", body);
        Assert.Contains("Gold loss attempted", body);
        Assert.Contains("Gold lost", body);
        Assert.Contains("Gold loss blocked", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.Contains("[b]20[/b]", body);
        Assert.Contains("[b]13[/b]", body);
        Assert.Contains("[b]7[/b]", body);
    }

    [Fact]
    public void CardAggregate_OlderShapeWithoutDebtFields_DefaultsToZero()
    {
        var agg = JsonSerializer.Deserialize<CardAggregate>("{}", RunStorage.Options);

        Assert.NotNull(agg);
        Assert.Equal(0, agg!.DebtTriggers);
        Assert.Equal(0, agg.DebtGoldLost);
        Assert.Equal(0, agg.DebtGoldLossBlocked);
    }

    private static string AppendDebtStats(CardAggregate agg)
    {
        var sb = new StringBuilder();
        var card = (Debt)RuntimeHelpers.GetUninitializedObject(typeof(Debt));
        _ = AppendDebtStatsMethod.Invoke(null, new object?[] { sb, card, agg });
        return sb.ToString();
    }
}
