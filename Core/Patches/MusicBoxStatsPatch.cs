using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Music Box retains the exact first Attack it is copying in its private
/// CardBeingPlayed field. Arm only when its owner callback is about to consume
/// that card, then keep the window open across the awaited generated-card add.
/// </summary>
[HarmonyPatch(typeof(MusicBox), nameof(MusicBox.AfterCardPlayed))]
internal static class MusicBoxAfterCardPlayedStatsPatch
{
    private static readonly FieldInfo? CardBeingPlayedField =
        AccessTools.Field(typeof(MusicBox), "_cardBeingPlayed");

    [HarmonyPrefix]
    public static void Prefix(
        MusicBox __instance,
        CardPlay cardPlay,
        out PendingMusicBoxCreationWindow? __state)
    {
        __state = null;

        try
        {
            if (!CanTrigger(__instance, cardPlay)) return;
            __state = RunTracker.ArmMusicBoxCreationAttribution(__instance, cardPlay);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"MusicBoxAfterCardPlayedStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingMusicBoxCreationWindow? __state,
        ref Task __result)
    {
        try
        {
            if (__state == null) return;
            if (__result == null)
            {
                RunTracker.DisarmMusicBoxCreationAttribution(__state);
                return;
            }

            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"MusicBoxAfterCardPlayedStatsPatch.Postfix failed: {e.Message}");
            RunTracker.DisarmMusicBoxCreationAttribution(__state);
        }
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception? __exception,
        PendingMusicBoxCreationWindow? __state)
    {
        if (__exception != null)
            RunTracker.DisarmMusicBoxCreationAttribution(__state);
        return __exception;
    }

    private static bool CanTrigger(MusicBox? relic, CardPlay? cardPlay)
    {
        if (relic?.Owner == null || cardPlay?.Card == null || CardBeingPlayedField == null)
            return false;

        try
        {
            return ReferenceEquals(
                CardBeingPlayedField.GetValue(relic),
                cardPlay.Card);
        }
        catch
        {
            return false;
        }
    }

    private static async Task ObserveAsync(
        Task inner,
        PendingMusicBoxCreationWindow window)
    {
        try
        {
            await inner.ConfigureAwait(false);
        }
        finally
        {
            RunTracker.DisarmMusicBoxCreationAttribution(window);
        }
    }
}

/// <summary>
/// Count only the final Attack that actually entered combat, and retain that
/// exact mutable card reference for its later Ethereal disposition.
/// </summary>
[HarmonyPatch(
    typeof(CardPileCmd),
    nameof(CardPileCmd.AddGeneratedCardToCombat),
    new[] { typeof(CardModel), typeof(PileType), typeof(Player), typeof(CardPilePosition) })]
internal static class MusicBoxGeneratedCardStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        CardModel card,
        Player creator,
        out PendingMusicBoxCreationWindow? __state)
    {
        __state = RunTracker.CaptureMusicBoxCreationAttempt(card, creator);
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingMusicBoxCreationWindow? __state,
        ref Task<CardPileAddResult> __result)
    {
        try
        {
            if (__state == null || __result == null) return;
            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"MusicBoxGeneratedCardStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task<CardPileAddResult> ObserveAsync(
        Task<CardPileAddResult> inner,
        PendingMusicBoxCreationWindow window)
    {
        var result = await inner.ConfigureAwait(false);
        try
        {
            RunTracker.RecordMusicBoxCreationResult(window, result);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"MusicBoxGeneratedCardStatsPatch.ObserveAsync failed: {e.Message}");
        }

        return result;
    }
}

/// <summary>
/// CardExhaustedEntry does not preserve why a card exhausted. This hook runs
/// after the card reached Exhaust and supplies the authoritative Ethereal flag.
/// </summary>
[HarmonyPatch(
    typeof(Hook),
    nameof(Hook.AfterCardExhausted),
    new[]
    {
        typeof(ICombatState),
        typeof(PlayerChoiceContext),
        typeof(CardModel),
        typeof(bool),
    })]
internal static class HookAfterCardExhaustedMusicBoxStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(CardModel card, bool causedByEthereal)
    {
        RunTracker.RecordMusicBoxEtherealExhaust(card, causedByEthereal);
    }
}

/// <summary>
/// Every started owner turn is part of Music Box's zero-inclusive rate.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
internal static class HookAfterPlayerTurnStartMusicBoxStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player)
    {
        RunTracker.RecordMusicBoxTurnStarted(player);
    }
}
