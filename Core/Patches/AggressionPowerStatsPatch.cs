using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace SpireLens.Core.Patches;

/// <summary>
/// Keeps attribution active across Aggression's sequential discard-to-hand
/// moves. The callback itself chooses the cards; the pile-add result confirms
/// which cards actually reached hand, and CardModel.UpgradeInternal separately
/// confirms which of those callback-selected cards were actually upgraded.
/// </summary>
[HarmonyPatch(
    typeof(AggressionPower),
    nameof(AggressionPower.BeforeSideTurnStart),
    new Type[]
    {
        typeof(PlayerChoiceContext),
        typeof(CombatSide),
        typeof(IReadOnlyList<Creature>),
        typeof(ICombatState),
    })]
internal static class AggressionPowerBeforeSideTurnStartStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        AggressionPower __instance,
        IReadOnlyList<Creature> participants,
        out PendingAggressionCallback? __state)
    {
        __state = RunTracker.ArmAggressionCallback(__instance, participants);
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingAggressionCallback? __state,
        ref Task __result)
    {
        try
        {
            if (__state == null) return;
            if (__result == null)
            {
                RunTracker.DisarmAggressionCallback(__state);
                return;
            }

            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"AggressionPowerBeforeSideTurnStartStatsPatch failed: {e.Message}");
            RunTracker.DisarmAggressionCallback(__state);
        }
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception? __exception,
        PendingAggressionCallback? __state)
    {
        if (__exception != null)
            RunTracker.DisarmAggressionCallback(__state);
        return __exception;
    }

    private static async Task ObserveAsync(
        Task inner,
        PendingAggressionCallback pending)
    {
        try
        {
            await inner.ConfigureAwait(false);
        }
        finally
        {
            RunTracker.DisarmAggressionCallback(pending);
        }
    }
}

/// <summary>
/// Claims only Aggression's direct discard-to-hand add. While that awaited add
/// is running, nested adds are excluded. Completion restores the callback
/// window before Aggression performs its immediate upgrade check.
/// </summary>
[HarmonyPatch(
    typeof(CardPileCmd),
    nameof(CardPileCmd.Add),
    new Type[]
    {
        typeof(CardModel),
        typeof(PileType),
        typeof(CardPilePosition),
        typeof(AbstractModel),
        typeof(bool),
    })]
internal static class AggressionCardPileAddStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        CardModel card,
        PileType newPileType,
        out PendingAggressionCardMove? __state)
    {
        __state = RunTracker.TryBeginAggressionCardMove(card, newPileType);
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingAggressionCardMove? __state,
        ref Task<CardPileAddResult> __result)
    {
        try
        {
            if (__state == null || __result == null) return;
            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"AggressionCardPileAddStatsPatch.Postfix failed: {e.Message}");
            RunTracker.AbortAggressionCardMove(__state);
        }
    }

    private static async Task<CardPileAddResult> ObserveAsync(
        Task<CardPileAddResult> inner,
        PendingAggressionCardMove pending)
    {
        try
        {
            var result = await inner.ConfigureAwait(false);
            RunTracker.CompleteAggressionCardMove(pending, result);
            return result;
        }
        catch
        {
            RunTracker.AbortAggressionCardMove(pending);
            throw;
        }
    }
}
