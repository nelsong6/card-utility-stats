using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Observes Bing Bong's exact permanent-deck clone request. Bing Bong passes
/// itself as clonedBy, and the completed result supplies the final card that
/// actually entered the deck after the shared add pipeline finishes.
/// </summary>
[HarmonyPatch(
    typeof(CardPileCmd),
    nameof(CardPileCmd.Add),
    new Type[]
    {
        typeof(CardModel),
        typeof(PileType),
        typeof(CardPilePosition),
        typeof(AbstractModel),
        typeof(bool),
    })]
public static class BingBongStatsPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        AbstractModel clonedBy,
        ref Task<CardPileAddResult> __result)
    {
        try
        {
            if (clonedBy is not BingBong relic || __result == null) return;
            __result = ObserveAsync(__result, relic);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BingBongStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task<CardPileAddResult> ObserveAsync(
        Task<CardPileAddResult> inner,
        BingBong relic)
    {
        var result = await inner;
        RunTracker.RecordBingBongCardAdded(relic, result);
        return result;
    }
}
