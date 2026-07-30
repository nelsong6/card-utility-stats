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
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class DarkEmbracePowerStatsTests
{
    private const string DarkEmbracePowerId = "POWER.DARK_EMBRACE";

    private static readonly MethodInfo AppendDarkEmbracePowerStatsMethod =
        typeof(CardHoverShowPatch).GetMethod(
            "AppendDarkEmbracePowerStats",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "AppendDarkEmbracePowerStats not found.");

    [Fact]
    public void DarkEmbraceImmediateDraw_RequiresOwnerAndNonEtherealExhaust()
    {
        Assert.True(RunTracker.DarkEmbraceImmediateDrawQualifiesForTest(
            exhaustedCardBelongsToOwner: true,
            causedByEthereal: false));
        Assert.False(RunTracker.DarkEmbraceImmediateDrawQualifiesForTest(
            exhaustedCardBelongsToOwner: false,
            causedByEthereal: false));
        Assert.False(RunTracker.DarkEmbraceImmediateDrawQualifiesForTest(
            exhaustedCardBelongsToOwner: true,
            causedByEthereal: true));
    }

    [Fact]
    public void PowerAggregate_DarkEmbraceFields_DefaultAndSerialize()
    {
        var empty = new PowerAggregate();
        Assert.Equal(0, empty.DarkEmbraceCardsDrawn);
        Assert.Equal(0, empty.DarkEmbraceCombatTurns);

        var run = new RunData();
        run.MetaStats.PowerAggregates[DarkEmbracePowerId] =
            CreateAggregate(cards: 18, activeTurns: 6, combatTurns: 9, combats: 3);

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(
            json,
            RunStorage.Options);

        Assert.Contains("\"dark_embrace_cards_drawn\"", json);
        Assert.Contains("\"dark_embrace_combat_turns\"", json);
        Assert.NotNull(restored);
        var agg = restored!.MetaStats.PowerAggregates[DarkEmbracePowerId];
        Assert.Equal(18, agg.DarkEmbraceCardsDrawn);
        Assert.Equal(9, agg.DarkEmbraceCombatTurns);
    }

    [Fact]
    public void RunTracker_DarkEmbraceHelpers_RecordOnlyPositiveOutcomes()
    {
        var agg = new PowerAggregate();

        RunTracker.RecordDarkEmbraceCardsDrawnForTest(agg, 18);
        RunTracker.RecordDarkEmbraceCardsDrawnForTest(agg, 0);
        RunTracker.RecordDarkEmbraceCardsDrawnForTest(agg, -1);
        RunTracker.RecordDarkEmbraceCombatTurnsForTest(agg, 9);
        RunTracker.RecordDarkEmbraceCombatTurnsForTest(agg, 0);
        RunTracker.RecordDarkEmbraceCombatTurnsForTest(agg, -1);

        Assert.Equal(18, agg.DarkEmbraceCardsDrawn);
        Assert.Equal(9, agg.DarkEmbraceCombatTurns);
    }

    [Fact]
    public void Promotion_MergesDarkEmbraceNumeratorsAndDenominators()
    {
        var run = new RunData();
        run.MetaStats.PowerAggregates[DarkEmbracePowerId] =
            CreateAggregate(cards: 7, activeTurns: 2, combatTurns: 4, combats: 1);
        var pending = new PendingCombat();
        pending.MetaStats.PowerAggregates[DarkEmbracePowerId] =
            CreateAggregate(cards: 11, activeTurns: 4, combatTurns: 5, combats: 2);

        RunTracker.PromotePendingCombatIntoRun(pending, run);

        var agg = run.MetaStats.PowerAggregates[DarkEmbracePowerId];
        Assert.Equal(18, agg.DarkEmbraceCardsDrawn);
        Assert.Equal(6, agg.TurnsActive);
        Assert.Equal(9, agg.DarkEmbraceCombatTurns);
        Assert.Equal(3, agg.CombatsActive);
    }

    [Fact]
    public void DarkEmbraceTooltip_UsesDistinctTurnDenominators()
    {
        var sb = new StringBuilder();
        var card = (DarkEmbrace)RuntimeHelpers.GetUninitializedObject(
            typeof(DarkEmbrace));
        var metaStats = new RunMetaStats();
        metaStats.PowerAggregates[DarkEmbracePowerId] =
            CreateAggregate(cards: 18, activeTurns: 6, combatTurns: 9, combats: 3);

        _ = AppendDarkEmbracePowerStatsMethod.Invoke(
            null,
            new object?[] { sb, card, metaStats });

        var body = sb.ToString();
        Assert.Contains("cards drawn", body);
        Assert.Contains("avg / active turn", body);
        Assert.Contains("avg / turn", body);
        Assert.Contains("avg / combat", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]6[/b]", body);
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void DarkEmbrace_UsesExactCallbacksAndDrawOverload()
    {
        var immediateTarget = AccessTools.Method(
            typeof(DarkEmbracePower),
            nameof(DarkEmbracePower.AfterCardExhausted),
            new[]
            {
                typeof(PlayerChoiceContext),
                typeof(CardModel),
                typeof(bool),
            });
        var deferredTarget = AccessTools.Method(
            typeof(DarkEmbracePower),
            nameof(DarkEmbracePower.AfterSideTurnEnd),
            new[]
            {
                typeof(PlayerChoiceContext),
                typeof(CombatSide),
                typeof(IEnumerable<Creature>),
            });
        var drawTarget = AccessTools.Method(
            typeof(CardPileCmd),
            nameof(CardPileCmd.Draw),
            new[]
            {
                typeof(PlayerChoiceContext),
                typeof(decimal),
                typeof(Player),
                typeof(bool),
            });

        Assert.NotNull(immediateTarget);
        Assert.NotNull(deferredTarget);
        Assert.NotNull(drawTarget);
    }

    private static PowerAggregate CreateAggregate(
        int cards,
        int activeTurns,
        int combatTurns,
        int combats) =>
        new()
        {
            PowerId = DarkEmbracePowerId,
            DisplayName = "Dark Embrace",
            DarkEmbraceCardsDrawn = cards,
            TurnsActive = activeTurns,
            DarkEmbraceCombatTurns = combatTurns,
            CombatsActive = combats,
        };
}
