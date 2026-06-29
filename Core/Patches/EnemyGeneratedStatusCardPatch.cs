using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace SpireLens.Core.Patches;

/// <summary>
/// Observes actual generated-card add results while an exact enemy status
/// source is active. This records the observed outcome rather than the move
/// text or intended count.
/// </summary>
[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.AddGeneratedCardsToCombat))]
public static class EnemyGeneratedStatusCardsPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        ref Task<IReadOnlyList<CardPileAddResult>> __result,
        IEnumerable<CardModel> cards,
        PileType newPileType,
        Player creator,
        CardPilePosition position)
    {
        try
        {
            if (__result == null) return;
            __result = ObserveAsync(__result, newPileType);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"EnemyGeneratedStatusCardsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task<IReadOnlyList<CardPileAddResult>> ObserveAsync(
        Task<IReadOnlyList<CardPileAddResult>> inner,
        PileType pileType)
    {
        var results = await inner.ConfigureAwait(false);
        try
        {
            foreach (var result in results)
                RunTracker.RecordGeneratedStatusCardAdded(result, pileType);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"EnemyGeneratedStatusCardsPatch.ObserveAsync failed: {e.Message}");
        }

        return results;
    }
}
