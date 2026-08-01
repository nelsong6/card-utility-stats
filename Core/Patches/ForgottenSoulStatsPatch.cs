using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Counts Forgotten Soul's exact owner callback as an activation and keeps a
/// narrow window around the single-target damage command it may emit. The
/// callback can have no hittable target, so the window is always disarmed when
/// its returned task completes.
/// </summary>
[HarmonyPatch(
    typeof(ForgottenSoul),
    nameof(ForgottenSoul.AfterCardExhausted),
    new[] { typeof(PlayerChoiceContext), typeof(CardModel), typeof(bool) })]
internal static class ForgottenSoulAfterCardExhaustedStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        ForgottenSoul __instance,
        CardModel card,
        out PendingForgottenSoulDamageAttribution? __state)
    {
        __state = null;

        try
        {
            __state = RunTracker.ArmForgottenSoulAttribution(__instance, card);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"ForgottenSoulAfterCardExhaustedStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingForgottenSoulDamageAttribution? __state,
        ref Task __result)
    {
        try
        {
            if (__state == null) return;
            if (__result == null)
            {
                RunTracker.DisarmForgottenSoulAttribution(__state);
                return;
            }

            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"ForgottenSoulAfterCardExhaustedStatsPatch.Postfix failed: {e.Message}");
            RunTracker.DisarmForgottenSoulAttribution(__state);
        }
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception? __exception,
        PendingForgottenSoulDamageAttribution? __state)
    {
        if (__exception != null)
            RunTracker.DisarmForgottenSoulAttribution(__state);
        return __exception;
    }

    private static async Task ObserveAsync(
        Task inner,
        PendingForgottenSoulDamageAttribution attribution)
    {
        try
        {
            await inner.ConfigureAwait(false);
        }
        finally
        {
            RunTracker.DisarmForgottenSoulAttribution(attribution);
        }
    }
}

/// <summary>
/// Captures Forgotten Soul's actual single-target damage result, including
/// blocked damage and a combat-ending kill omitted by normal combat history.
/// </summary>
[HarmonyPatch(
    typeof(CreatureCmd),
    nameof(CreatureCmd.Damage),
    new[] { typeof(PlayerChoiceContext), typeof(Creature), typeof(DamageVar), typeof(Creature) })]
internal static class ForgottenSoulCreatureDamageStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Creature dealer, out bool __state)
    {
        __state = false;

        try
        {
            __state = RunTracker.TryConsumeForgottenSoulDamageAttribution(dealer);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"ForgottenSoulCreatureDamageStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(
        bool __state,
        ref Task<IEnumerable<DamageResult>> __result)
    {
        try
        {
            if (!__state || __result == null) return;
            __result = ObserveDamageAsync(__result);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"ForgottenSoulCreatureDamageStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task<IEnumerable<DamageResult>> ObserveDamageAsync(
        Task<IEnumerable<DamageResult>> inner)
    {
        var results = await inner.ConfigureAwait(false);
        RunTracker.RecordForgottenSoulDamage(results);
        return results;
    }
}

/// <summary>
/// Counts zero-inclusive held turns for Forgotten Soul's damage average.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
internal static class HookAfterPlayerTurnStartForgottenSoulStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player)
    {
        try
        {
            RunTracker.RecordForgottenSoulTurnStarted(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookAfterPlayerTurnStartForgottenSoulStatsPatch failed: {e.Message}");
        }
    }
}
