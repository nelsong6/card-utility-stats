using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Keeps Toasty Mittens attribution attached to its async hand-draw callback
/// without exposing that scope to the caller after the callback returns.
/// </summary>
[HarmonyPatch(typeof(ToastyMittens), nameof(ToastyMittens.BeforeHandDraw))]
internal static class ToastyMittensBeforeHandDrawStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        ToastyMittens __instance,
        Player player,
        out PendingToastyMittensActivation? __state)
    {
        __state = RunTracker.BeginToastyMittensActivation(__instance, player);
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingToastyMittensActivation? __state,
        ref Task __result)
    {
        if (__state == null || __result == null) return;
        __result = ObserveAsync(__result, __state);
    }

    private static async Task ObserveAsync(
        Task inner,
        PendingToastyMittensActivation frame)
    {
        await inner.ConfigureAwait(false);
        RunTracker.CompleteToastyMittensActivation(frame);
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception? __exception,
        PendingToastyMittensActivation? __state)
    {
        RunTracker.RestoreToastyMittensActivation(__state);
        return __exception;
    }
}
