using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace SpireLens.Core.Patches;

/// <summary>
/// Arms attribution for Dark Embrace's immediate draw after a non-Ethereal
/// owner card exhausts. Ethereal exhausts are deferred to AfterSideTurnEnd.
/// </summary>
[HarmonyPatch(
    typeof(DarkEmbracePower),
    nameof(DarkEmbracePower.AfterCardExhausted),
    new[]
    {
        typeof(PlayerChoiceContext),
        typeof(CardModel),
        typeof(bool),
    })]
internal static class DarkEmbracePowerAfterCardExhaustedStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        DarkEmbracePower __instance,
        CardModel card,
        bool causedByEthereal,
        out PendingDarkEmbraceDraw? __state)
    {
        __state = RunTracker.ArmDarkEmbraceImmediateDrawAttribution(
            __instance,
            card,
            causedByEthereal);
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingDarkEmbraceDraw? __state,
        ref Task __result)
    {
        try
        {
            if (__state == null) return;
            if (__result == null)
            {
                RunTracker.DisarmDarkEmbraceDrawAttribution(__state);
                return;
            }

            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"DarkEmbracePowerAfterCardExhaustedStatsPatch failed: {e.Message}");
            RunTracker.DisarmDarkEmbraceDrawAttribution(__state);
        }
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception? __exception,
        PendingDarkEmbraceDraw? __state)
    {
        if (__exception != null)
            RunTracker.DisarmDarkEmbraceDrawAttribution(__state);
        return __exception;
    }

    private static async Task ObserveAsync(
        Task inner,
        PendingDarkEmbraceDraw pending)
    {
        try
        {
            await inner.ConfigureAwait(false);
        }
        finally
        {
            RunTracker.DisarmDarkEmbraceDrawAttribution(pending);
        }
    }
}

/// <summary>
/// Arms attribution for the batched draw Dark Embrace issues after Ethereal
/// owner cards are flushed at player-side turn end.
/// </summary>
[HarmonyPatch(
    typeof(DarkEmbracePower),
    nameof(DarkEmbracePower.AfterSideTurnEnd),
    new[]
    {
        typeof(PlayerChoiceContext),
        typeof(CombatSide),
        typeof(IEnumerable<Creature>),
    })]
internal static class DarkEmbracePowerAfterSideTurnEndStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        DarkEmbracePower __instance,
        IEnumerable<Creature> participants,
        out PendingDarkEmbraceDraw? __state)
    {
        __state = RunTracker.ArmDarkEmbraceDeferredDrawAttribution(
            __instance,
            participants);
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingDarkEmbraceDraw? __state,
        ref Task __result)
    {
        try
        {
            if (__state == null) return;
            if (__result == null)
            {
                RunTracker.DisarmDarkEmbraceDrawAttribution(__state);
                return;
            }

            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"DarkEmbracePowerAfterSideTurnEndStatsPatch failed: {e.Message}");
            RunTracker.DisarmDarkEmbraceDrawAttribution(__state);
        }
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception? __exception,
        PendingDarkEmbraceDraw? __state)
    {
        if (__exception != null)
            RunTracker.DisarmDarkEmbraceDrawAttribution(__state);
        return __exception;
    }

    private static async Task ObserveAsync(
        Task inner,
        PendingDarkEmbraceDraw pending)
    {
        try
        {
            await inner.ConfigureAwait(false);
        }
        finally
        {
            RunTracker.DisarmDarkEmbraceDrawAttribution(pending);
        }
    }
}

/// <summary>
/// Counts the cards the exact Dark Embrace draw command actually returned.
/// Draw prevention, hand capacity, and exhausted piles therefore reduce the
/// recorded total.
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
internal static class DarkEmbraceCardPileDrawStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        Player player,
        bool fromHandDraw,
        out PendingDarkEmbraceDraw? __state)
    {
        __state = null;
        RunTracker.TryConsumeDarkEmbraceDrawAttribution(
            player,
            fromHandDraw,
            out __state);
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingDarkEmbraceDraw? __state,
        ref Task<IEnumerable<CardModel>> __result)
    {
        try
        {
            if (__state == null || __result == null) return;
            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"DarkEmbraceCardPileDrawStatsPatch failed: {e.Message}");
        }
    }

    private static async Task<IEnumerable<CardModel>> ObserveAsync(
        Task<IEnumerable<CardModel>> inner,
        PendingDarkEmbraceDraw pending)
    {
        var cards = await inner.ConfigureAwait(false);

        try
        {
            RunTracker.RecordDarkEmbraceCardsDrawn(
                pending,
                cards?.Count(card => card != null) ?? 0);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"DarkEmbraceCardPileDrawStatsPatch.ObserveAsync failed: {e.Message}");
        }

        return cards ?? Array.Empty<CardModel>();
    }
}

/// <summary>
/// Counts later zero-draw turns that begin while Dark Embrace remains active.
/// The application turn is counted from the observed power application.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
internal static class HookAfterPlayerTurnStartDarkEmbraceStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player)
    {
        RunTracker.RecordDarkEmbracePowerTurnStarted(player);
    }
}
