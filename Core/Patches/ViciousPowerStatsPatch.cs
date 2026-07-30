using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace SpireLens.Core.Patches;

/// <summary>
/// Arms Vicious attribution only for the same positive owner-applied
/// Vulnerable change that makes the native power issue its draw command.
/// </summary>
[HarmonyPatch(
    typeof(ViciousPower),
    nameof(ViciousPower.AfterPowerAmountChanged),
    new[]
    {
        typeof(PlayerChoiceContext),
        typeof(PowerModel),
        typeof(decimal),
        typeof(Creature),
        typeof(CardModel),
    })]
internal static class ViciousPowerAfterPowerAmountChangedStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        ViciousPower __instance,
        PowerModel power,
        decimal amount,
        Creature? applier,
        out PendingViciousDraw? __state)
    {
        __state = RunTracker.ArmViciousDrawAttribution(
            __instance,
            power,
            amount,
            applier);
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingViciousDraw? __state,
        ref Task __result)
    {
        try
        {
            if (__state == null) return;
            if (__result == null)
            {
                RunTracker.DisarmViciousDrawAttribution(__state);
                return;
            }

            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"ViciousPowerAfterPowerAmountChangedStatsPatch failed: {e.Message}");
            RunTracker.DisarmViciousDrawAttribution(__state);
        }
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception? __exception,
        PendingViciousDraw? __state)
    {
        if (__exception != null)
            RunTracker.DisarmViciousDrawAttribution(__state);
        return __exception;
    }

    private static async Task ObserveAsync(
        Task inner,
        PendingViciousDraw pending)
    {
        try
        {
            await inner.ConfigureAwait(false);
        }
        finally
        {
            RunTracker.DisarmViciousDrawAttribution(pending);
        }
    }
}

/// <summary>
/// Observes the exact cards returned by Vicious' direct draw. Draw prevention,
/// hand capacity, and pile exhaustion therefore reduce the tracked total.
/// </summary>
[HarmonyPatch(
    typeof(CardPileCmd),
    nameof(CardPileCmd.Draw),
    new[]
    {
        typeof(PlayerChoiceContext),
        typeof(decimal),
        typeof(Player),
        typeof(bool),
    })]
internal static class ViciousCardPileDrawStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        Player player,
        bool fromHandDraw,
        out PendingViciousDraw? __state)
    {
        __state = null;
        RunTracker.TryConsumeViciousDrawAttribution(
            player,
            fromHandDraw,
            out __state);
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingViciousDraw? __state,
        ref Task<IEnumerable<CardModel>> __result)
    {
        try
        {
            if (__state == null || __result == null) return;
            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"ViciousCardPileDrawStatsPatch failed: {e.Message}");
        }
    }

    private static async Task<IEnumerable<CardModel>> ObserveAsync(
        Task<IEnumerable<CardModel>> inner,
        PendingViciousDraw pending)
    {
        var cards = await inner.ConfigureAwait(false);

        try
        {
            RunTracker.RecordViciousCardsDrawn(
                pending,
                cards?.Count(card => card != null) ?? 0);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"ViciousCardPileDrawStatsPatch.ObserveAsync failed: {e.Message}");
        }

        return cards ?? Array.Empty<CardModel>();
    }
}
