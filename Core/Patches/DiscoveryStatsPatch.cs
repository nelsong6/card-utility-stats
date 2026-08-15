using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
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

/// <summary>
/// Counts the options on the choose-a-card screen Discovery awaits before it
/// discounts a pick. Discovery enters this shared command from its own resolve,
/// so the resolving-card guard tells its screen apart from every other caller's
/// and the offers land on the same physical card the pick does.
/// </summary>
[HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromChooseACardScreen))]
public static class DiscoveryChooseACardScreenPatch
{
    [HarmonyPrefix]
    public static void Prefix(IReadOnlyList<CardModel> cards, Player player)
    {
        try
        {
            RunTracker.RecordDiscoveryCardsOffered(cards, player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"DiscoveryChooseACardScreenPatch.Prefix failed: {e.Message}");
        }
    }
}

public sealed record DiscoveryPickObservation(
    CardModel SourceCard,
    int CostBefore);
