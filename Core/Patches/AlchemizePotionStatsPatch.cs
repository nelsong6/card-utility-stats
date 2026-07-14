using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Models;

namespace SpireLens.Core.Patches;

/// <summary>
/// Observes the exact potion-procurement result used by Alchemize. The card
/// ignores this return value itself, so capture its physical source before the
/// async command and wrap the returned task to record success, failure, and
/// the actual generated potion rarity before the card play finishes.
/// </summary>
[HarmonyPatch(
    typeof(PotionCmd),
    nameof(PotionCmd.TryToProcure),
    new[] { typeof(PotionModel), typeof(Player), typeof(int) })]
public static class AlchemizePotionStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player, out CardModel? __state)
    {
        __state = RunTracker.CaptureAlchemizePotionSource(player);
    }

    [HarmonyPostfix]
    public static void Postfix(
        Player player,
        CardModel? __state,
        ref Task<PotionProcureResult> __result)
    {
        try
        {
            if (__state == null || __result == null) return;
            __result = ObserveAsync(__result, __state, player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"AlchemizePotionStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task<PotionProcureResult> ObserveAsync(
        Task<PotionProcureResult> inner,
        CardModel sourceCard,
        Player player)
    {
        var result = await inner.ConfigureAwait(false);
        try
        {
            RunTracker.RecordAlchemizePotionResult(sourceCard, player, result);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"AlchemizePotionStatsPatch.ObserveAsync failed: {e.Message}");
        }

        return result;
    }
}
