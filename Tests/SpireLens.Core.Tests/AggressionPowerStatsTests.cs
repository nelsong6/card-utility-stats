using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class AggressionPowerStatsTests
{
    private const string AggressionPowerId = "POWER.AGGRESSION";

    private static readonly MethodInfo AppendAggressionPowerStatsMethod =
        typeof(CardHoverShowPatch).GetMethod(
            "AppendAggressionPowerStats",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "AppendAggressionPowerStats not found.");

    [Fact]
    public void PowerAggregate_AggressionFields_DefaultAndSerialize()
    {
        var empty = new PowerAggregate();
        Assert.Equal(0, empty.AggressionCardsReturnedToHand);
        Assert.Equal(0, empty.AggressionCardsUpgraded);

        var run = new RunData();
        run.MetaStats.PowerAggregates[AggressionPowerId] =
            CreateAggregate(cardsReturned: 8, cardsUpgraded: 5);

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(
            json,
            RunStorage.Options);

        Assert.Contains("\"aggression_cards_returned_to_hand\"", json);
        Assert.Contains("\"aggression_cards_upgraded\"", json);
        Assert.NotNull(restored);
        AssertAggregate(
            restored!.MetaStats.PowerAggregates[AggressionPowerId],
            cardsReturned: 8,
            cardsUpgraded: 5);
    }

    [Fact]
    public void RunTracker_AggressionHelper_RecordsOutcomesIndependently()
    {
        var agg = new PowerAggregate();

        RunTracker.RecordAggressionOutcomesForTest(
            agg,
            cardsReturnedToHand: 3,
            cardsUpgraded: 2);
        RunTracker.RecordAggressionOutcomesForTest(
            agg,
            cardsReturnedToHand: -1,
            cardsUpgraded: 0);
        RunTracker.RecordAggressionOutcomesForTest(
            agg,
            cardsReturnedToHand: 0,
            cardsUpgraded: 1);

        Assert.Equal(3, agg.AggressionCardsReturnedToHand);
        Assert.Equal(3, agg.AggressionCardsUpgraded);
    }

    [Fact]
    public void Promotion_MergesAggressionPowerTotals()
    {
        var run = new RunData();
        run.MetaStats.PowerAggregates[AggressionPowerId] =
            CreateAggregate(cardsReturned: 3, cardsUpgraded: 2);
        var pending = new PendingCombat();
        pending.MetaStats.PowerAggregates[AggressionPowerId] =
            CreateAggregate(cardsReturned: 5, cardsUpgraded: 3);

        RunTracker.PromotePendingCombatIntoRun(pending, run);

        AssertAggregate(
            run.MetaStats.PowerAggregates[AggressionPowerId],
            cardsReturned: 8,
            cardsUpgraded: 5);
    }

    [Fact]
    public void AggressionTooltip_ProjectsSharedPowerOutcomes()
    {
        var sb = new StringBuilder();
        var card = (Aggression)RuntimeHelpers.GetUninitializedObject(
            typeof(Aggression));
        var metaStats = new RunMetaStats();
        metaStats.PowerAggregates[AggressionPowerId] =
            CreateAggregate(cardsReturned: 8, cardsUpgraded: 5);

        _ = AppendAggressionPowerStatsMethod.Invoke(
            null,
            new object?[] { sb, card, metaStats });

        var body = sb.ToString();
        Assert.Contains("Cards returned to hand", body);
        Assert.Contains("[b]8[/b]", body);
        Assert.Contains("Cards upgraded", body);
        Assert.Contains("[b]5[/b]", body);
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void Patches_TargetExpectedAggressionAndPileAddMethods()
    {
        var powerTarget = AccessTools.Method(
            typeof(AggressionPower),
            nameof(AggressionPower.BeforeSideTurnStart),
            new[]
            {
                typeof(PlayerChoiceContext),
                typeof(CombatSide),
                typeof(IReadOnlyList<Creature>),
                typeof(ICombatState),
            });
        var addTarget = AccessTools.Method(
            typeof(CardPileCmd),
            nameof(CardPileCmd.Add),
            new[]
            {
                typeof(CardModel),
                typeof(PileType),
                typeof(CardPilePosition),
                typeof(AbstractModel),
                typeof(bool),
            });

        Assert.NotNull(powerTarget);
        Assert.NotNull(addTarget);
    }

    private static PowerAggregate CreateAggregate(
        int cardsReturned,
        int cardsUpgraded) =>
        new()
        {
            PowerId = AggressionPowerId,
            DisplayName = "Aggression",
            AggressionCardsReturnedToHand = cardsReturned,
            AggressionCardsUpgraded = cardsUpgraded,
        };

    private static void AssertAggregate(
        PowerAggregate agg,
        int cardsReturned,
        int cardsUpgraded)
    {
        Assert.Equal(AggressionPowerId, agg.PowerId);
        Assert.Equal("Aggression", agg.DisplayName);
        Assert.Equal(cardsReturned, agg.AggressionCardsReturnedToHand);
        Assert.Equal(cardsUpgraded, agg.AggressionCardsUpgraded);
    }
}
