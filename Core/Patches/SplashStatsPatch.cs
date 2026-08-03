using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace SpireLens.Core.Patches;

/// <summary>
/// Observes Splash's exact SetToFreeThisTurn call on the selected Attack.
/// The resolving-card guard excludes Discovery, Crossbow, and every other
/// caller, while before/after effective costs measure the actual discount.
/// </summary>
[HarmonyPatch(typeof(CardModel), nameof(CardModel.SetToFreeThisTurn))]
public static class SplashStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        CardModel __instance,
        out SplashAttackObservation? __state)
    {
        __state = null;

        try
        {
            var sourceCard = RunTracker.CaptureSplashChoiceSource(__instance);
            if (sourceCard == null) return;

            __state = new SplashAttackObservation(
                sourceCard,
                EffectiveEnergyCost(__instance));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"SplashStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(
        CardModel __instance,
        SplashAttackObservation? __state)
    {
        if (__state == null) return;

        try
        {
            RunTracker.RecordSplashAttackTaken(
                __state.SourceCard,
                __instance,
                __state.CostBefore,
                EffectiveEnergyCost(__instance));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"SplashStatsPatch.Postfix failed: {e.Message}");
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

public sealed record SplashAttackObservation(
    CardModel SourceCard,
    int CostBefore);
