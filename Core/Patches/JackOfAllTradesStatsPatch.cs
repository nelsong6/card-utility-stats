using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace SpireLens.Core.Patches;

/// <summary>
/// Observes the exact generated-card add result used by Jack of All Trades.
/// Capturing the physical source before the async command and inspecting its
/// returned result avoids counting candidates that never actually enter
/// combat.
/// </summary>
[HarmonyPatch(
    typeof(CardPileCmd),
    nameof(CardPileCmd.AddGeneratedCardToCombat),
    new[] { typeof(CardModel), typeof(PileType), typeof(Player), typeof(CardPilePosition) })]
public static class JackOfAllTradesStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player creator, out CardModel? __state)
    {
        __state = RunTracker.CaptureJackOfAllTradesSource(creator);
    }

    [HarmonyPostfix]
    public static void Postfix(
        Player creator,
        CardModel? __state,
        ref Task<CardPileAddResult> __result)
    {
        try
        {
            if (__state == null || __result == null) return;
            __result = ObserveAsync(__result, __state, creator);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"JackOfAllTradesStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task<CardPileAddResult> ObserveAsync(
        Task<CardPileAddResult> inner,
        CardModel sourceCard,
        Player creator)
    {
        var result = await inner.ConfigureAwait(false);
        try
        {
            RunTracker.RecordJackOfAllTradesCardAdded(sourceCard, creator, result);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"JackOfAllTradesStatsPatch.ObserveAsync failed: {e.Message}");
        }

        return result;
    }
}
