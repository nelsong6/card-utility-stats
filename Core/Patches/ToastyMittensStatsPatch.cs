using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
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

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception? __exception,
        PendingToastyMittensActivation? __state)
    {
        RunTracker.RestoreToastyMittensActivation(__state);
        return __exception;
    }
}

/// <summary>
/// The relic always shuffles before choosing its card. Only arm Strength
/// attribution after that awaited operation is complete, so shuffle hooks
/// cannot be mistaken for Toasty Mittens' own Strength application.
/// </summary>
[HarmonyPatch(
    typeof(CardPileCmd),
    nameof(CardPileCmd.ShuffleIfNecessary),
    new[] { typeof(PlayerChoiceContext), typeof(Player) })]
internal static class ToastyMittensShuffleStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        Player player,
        out PendingToastyMittensActivation? __state)
    {
        __state = RunTracker.ClaimToastyMittensShuffle(player);
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
        RunTracker.CompleteToastyMittensShuffle(frame);
    }
}

/// <summary>
/// Counts the exact draw-pile card exhausted by Toasty Mittens after the
/// command succeeds. Nested exhausts caused by exhaust hooks are excluded.
/// Completing this command also arms the immediately following Strength
/// application.
/// </summary>
[HarmonyPatch(
    typeof(CardCmd),
    nameof(CardCmd.Exhaust),
    new[]
    {
        typeof(PlayerChoiceContext),
        typeof(CardModel),
        typeof(bool),
        typeof(bool),
    })]
internal static class ToastyMittensCardExhaustStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        CardModel card,
        out PendingToastyMittensActivation? __state)
    {
        __state = RunTracker.ClaimToastyMittensExhaust(card);
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
        RunTracker.CompleteToastyMittensExhaust(frame);
    }
}
