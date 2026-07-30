using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace SpireLens.Core.Patches;

/// <summary>
/// Observes Discovery's exact SetToFreeThisTurn call on the selected card.
/// The resolving-card guard excludes Mummified Hand and every other caller,
/// while before/after effective costs measure the actual energy discount.
/// </summary>
[HarmonyPatch(typeof(CardModel), nameof(CardModel.SetToFreeThisTurn))]
public static class DiscoveryStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        CardModel __instance,
        out DiscoveryPickObservation? __state)
    {
        __state = null;

        try
        {
            var sourceCard = RunTracker.CaptureDiscoveryChoiceSource(__instance);
            if (sourceCard == null) return;

            __state = new DiscoveryPickObservation(
                sourceCard,
                EffectiveEnergyCost(__instance));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"DiscoveryStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(
        CardModel __instance,
        DiscoveryPickObservation? __state)
    {
        if (__state == null) return;

        try
        {
            RunTracker.RecordDiscoveryCardPicked(
                __state.SourceCard,
                __instance,
                __state.CostBefore,
                EffectiveEnergyCost(__instance));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"DiscoveryStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static int EffectiveEnergyCost(CardModel card)
    {
        try
        {
            if (card.EnergyCost.CostsX) return 0;
            return Math.Max(0, card.EnergyCost.GetWithModifiers(CostModifiers.All));
        }
        catch
        {
            return 0;
        }
    }
}

public sealed record DiscoveryPickObservation(
    CardModel SourceCard,
    int CostBefore);
