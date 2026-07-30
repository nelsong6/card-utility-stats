using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Burning Sticks duplicates the first owned Skill exhausted each combat.
/// Arm attribution at that exact owner callback, then leave it active across
/// the awaited pile command so only the resulting generated card can claim it.
/// </summary>
[HarmonyPatch(
    typeof(BurningSticks),
    nameof(BurningSticks.AfterCardExhausted),
    new[] { typeof(PlayerChoiceContext), typeof(CardModel), typeof(bool) })]
internal static class BurningSticksAfterCardExhaustedStatsPatch
{
    private static readonly FieldInfo? WasUsedThisCombatField =
        AccessTools.Field(typeof(BurningSticks), "_wasUsedThisCombat");

    [HarmonyPrefix]
    public static void Prefix(
        BurningSticks __instance,
        CardModel card,
        out PendingBurningSticksDuplicateWindow? __state)
    {
        __state = null;

        try
        {
            if (!CanTrigger(__instance)) return;
            __state = RunTracker.ArmBurningSticksDuplicateAttribution(__instance, card);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BurningSticksAfterCardExhaustedStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingBurningSticksDuplicateWindow? __state,
        ref Task __result)
    {
        try
        {
            if (__state == null) return;
            if (__result == null)
            {
                RunTracker.DisarmBurningSticksDuplicateAttribution(__state);
                return;
            }

            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BurningSticksAfterCardExhaustedStatsPatch.Postfix failed: {e.Message}");
            RunTracker.DisarmBurningSticksDuplicateAttribution(__state);
        }
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception? __exception,
        PendingBurningSticksDuplicateWindow? __state)
    {
        if (__exception != null)
            RunTracker.DisarmBurningSticksDuplicateAttribution(__state);
        return __exception;
    }

    private static bool CanTrigger(BurningSticks? relic)
    {
        if (relic == null || WasUsedThisCombatField == null) return false;

        try
        {
            return WasUsedThisCombatField.GetValue(relic) is false;
        }
        catch
        {
            return false;
        }
    }

    private static async Task ObserveAsync(
        Task inner,
        PendingBurningSticksDuplicateWindow window)
    {
        try
        {
            await inner.ConfigureAwait(false);
        }
        finally
        {
            RunTracker.DisarmBurningSticksDuplicateAttribution(window);
        }
    }
}

/// <summary>
/// Observe the actual card returned by Burning Sticks' generated-card command.
/// A failed pile add does not count as an activation or duplicate.
/// </summary>
[HarmonyPatch(
    typeof(CardPileCmd),
    nameof(CardPileCmd.AddGeneratedCardToCombat),
    new[] { typeof(CardModel), typeof(PileType), typeof(Player), typeof(CardPilePosition) })]
internal static class BurningSticksGeneratedCardStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        CardModel card,
        Player creator,
        out PendingBurningSticksDuplicateWindow? __state)
    {
        __state = RunTracker.CaptureBurningSticksDuplicateAttempt(card, creator);
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingBurningSticksDuplicateWindow? __state,
        ref Task<CardPileAddResult> __result)
    {
        try
        {
            if (__state == null || __result == null) return;
            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BurningSticksGeneratedCardStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task<CardPileAddResult> ObserveAsync(
        Task<CardPileAddResult> inner,
        PendingBurningSticksDuplicateWindow window)
    {
        var result = await inner.ConfigureAwait(false);
        try
        {
            RunTracker.RecordBurningSticksDuplicateResult(window, result);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BurningSticksGeneratedCardStatsPatch.ObserveAsync failed: {e.Message}");
        }

        return result;
    }
}
