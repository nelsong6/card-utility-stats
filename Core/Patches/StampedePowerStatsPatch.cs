using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace SpireLens.Core.Patches;

/// <summary>
/// Keeps a callback-wide Stampede context while the power sequentially
/// selects and awaits its free Attack plays.
/// </summary>
[HarmonyPatch(
    typeof(StampedePower),
    nameof(StampedePower.AfterAutoPostPlayPhaseEntered),
    new[]
    {
        typeof(PlayerChoiceContext),
        typeof(Player),
    })]
internal static class StampedePowerAfterAutoPostPlayPhaseEnteredStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        StampedePower __instance,
        Player player,
        out PendingStampedeCallback? __state)
    {
        __state = RunTracker.ArmStampedeCallback(__instance, player);
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingStampedeCallback? __state,
        ref Task __result)
    {
        try
        {
            if (__state == null) return;
            if (__result == null)
            {
                RunTracker.DisarmStampedeCallback(__state);
                return;
            }

            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"StampedePowerAfterAutoPostPlayPhaseEnteredStatsPatch failed: {e.Message}");
            RunTracker.DisarmStampedeCallback(__state);
        }
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception? __exception,
        PendingStampedeCallback? __state)
    {
        if (__exception != null)
            RunTracker.DisarmStampedeCallback(__state);
        return __exception;
    }

    private static async Task ObserveAsync(
        Task inner,
        PendingStampedeCallback pending)
    {
        try
        {
            await inner.ConfigureAwait(false);
        }
        finally
        {
            RunTracker.DisarmStampedeCallback(pending);
        }
    }
}

/// <summary>
/// Claims only the direct AutoPlay calls made by the active Stampede callback.
/// The callback is suspended until each claimed task completes, preventing
/// nested autoplays caused by the selected Attack from being attributed.
/// </summary>
[HarmonyPatch(
    typeof(CardCmd),
    nameof(CardCmd.AutoPlay),
    new[]
    {
        typeof(PlayerChoiceContext),
        typeof(CardModel),
        typeof(Creature),
        typeof(AutoPlayType),
        typeof(bool),
        typeof(bool),
    })]
internal static class StampedeCardAutoPlayStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        CardModel card,
        out PendingStampedeAutoPlay? __state)
    {
        __state = RunTracker.TryBeginStampedeAutoPlay(card);
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingStampedeAutoPlay? __state,
        ref Task __result)
    {
        try
        {
            if (__state == null) return;
            if (__result == null)
            {
                RunTracker.CompleteStampedeAutoPlay(__state);
                return;
            }

            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"StampedeCardAutoPlayStatsPatch failed: {e.Message}");
            RunTracker.CompleteStampedeAutoPlay(__state);
        }
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception? __exception,
        PendingStampedeAutoPlay? __state)
    {
        if (__exception != null)
            RunTracker.CompleteStampedeAutoPlay(__state);
        return __exception;
    }

    private static async Task ObserveAsync(
        Task inner,
        PendingStampedeAutoPlay pending)
    {
        try
        {
            await inner.ConfigureAwait(false);
        }
        finally
        {
            RunTracker.CompleteStampedeAutoPlay(pending);
        }
    }
}
