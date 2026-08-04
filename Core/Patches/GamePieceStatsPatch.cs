using System;
using System.Collections.Generic;
using System.Linq;
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
/// Arms attribution from Game Piece's own qualifying Power-play callback.
/// The callback's matching draw command is observed separately so draw
/// prevention, hand capacity, and pile exhaustion remain visible outcomes.
/// </summary>
[HarmonyPatch(typeof(GamePiece), nameof(GamePiece.AfterCardPlayed))]
internal static class GamePieceAfterCardPlayedStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        GamePiece __instance,
        CardPlay cardPlay,
        out PendingGamePieceDraw? __state)
    {
        __state = RunTracker.ArmGamePieceDrawAttribution(__instance, cardPlay);
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingGamePieceDraw? __state,
        ref Task __result)
    {
        try
        {
            if (__state == null) return;
            if (__result == null)
            {
                RunTracker.DisarmGamePieceDrawAttribution(__state);
                return;
            }

            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"GamePieceAfterCardPlayedStatsPatch failed: {e.Message}");
            RunTracker.DisarmGamePieceDrawAttribution(__state);
        }
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception? __exception,
        PendingGamePieceDraw? __state)
    {
        if (__exception != null)
            RunTracker.DisarmGamePieceDrawAttribution(__state);
        return __exception;
    }

    private static async Task ObserveAsync(
        Task inner,
        PendingGamePieceDraw pending)
    {
        try
        {
            await inner.ConfigureAwait(false);
        }
        finally
        {
            RunTracker.DisarmGamePieceDrawAttribution(pending);
        }
    }
}

/// <summary>
/// Observes the exact cards returned by Game Piece's direct non-hand draw.
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
internal static class GamePieceCardPileDrawStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        decimal count,
        Player player,
        bool fromHandDraw,
        out PendingGamePieceDraw? __state)
    {
        __state = null;
        RunTracker.TryConsumeGamePieceDrawAttribution(
            player,
            count,
            fromHandDraw,
            out __state);
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingGamePieceDraw? __state,
        ref Task<IEnumerable<CardModel>> __result)
    {
        try
        {
            if (__state == null || __result == null) return;
            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"GamePieceCardPileDrawStatsPatch failed: {e.Message}");
        }
    }

    private static async Task<IEnumerable<CardModel>> ObserveAsync(
        Task<IEnumerable<CardModel>> inner,
        PendingGamePieceDraw pending)
    {
        var cards = await inner.ConfigureAwait(false);

        try
        {
            RunTracker.RecordGamePieceDrawResult(
                pending,
                cards?.Count(card => card != null) ?? 0);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"GamePieceCardPileDrawStatsPatch.ObserveAsync failed: {e.Message}");
        }

        return cards ?? Array.Empty<CardModel>();
    }
}
