using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Brilliant Scarf's discount opportunities from the relic's own
/// card-play counter and fills in energy saved from the late cost modifier.
/// </summary>
[HarmonyPatch(typeof(BrilliantScarf), nameof(BrilliantScarf.TryModifyEnergyCostInCombatLate))]
public static class BrilliantScarfTryModifyEnergyCostPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        BrilliantScarf __instance,
        CardModel card,
        decimal originalCost,
        ref decimal modifiedCost,
        bool __result)
    {
        try
        {
            if (!__result) return;
            if (!RunTracker.IsTrackedRelic(__instance)) return;

            RunTracker.RecordBrilliantScarfPotentialEnergySaving(card, originalCost, modifiedCost);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BrilliantScarfTryModifyEnergyCostPatch failed: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(BrilliantScarf), nameof(BrilliantScarf.AfterCardPlayed))]
public static class BrilliantScarfAfterCardPlayedPatch
{
    [HarmonyPostfix]
    public static void Postfix(BrilliantScarf __instance, CardPlay cardPlay)
    {
        try
        {
            if (!RunTracker.IsTrackedRelic(__instance)) return;

            int threshold = __instance.DynamicVars.Cards.IntValue;
            RunTracker.RecordBrilliantScarfDiscountOffered(
                cardPlay,
                __instance.CardsPlayedThisTurn,
                threshold);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BrilliantScarfAfterCardPlayedPatch failed: {e.Message}");
        }
    }
}
