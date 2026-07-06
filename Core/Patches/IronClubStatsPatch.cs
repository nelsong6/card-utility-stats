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
/// Iron Club increments its card-play counter in AfterCardPlayed and draws
/// immediately when that counter wraps. The prefix arms attribution from the
/// pre-increment counter so the draw patch can count the observed result.
/// </summary>
[HarmonyPatch(typeof(IronClub), nameof(IronClub.AfterCardPlayed))]
public static class IronClubAfterCardPlayedPatch
{
    [HarmonyPrefix]
    public static void Prefix(IronClub __instance, PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            if (__instance == null || !RunTracker.IsTrackedRelic(__instance)) return;
            RunTracker.ArmIronClubDrawAttribution(__instance, cardPlay);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"IronClubAfterCardPlayedPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(IronClub __instance, Task __result)
    {
        try
        {
            var owner = __instance?.Owner;
            if (owner == null) return;

            if (__result == null || __result.IsCompleted)
            {
                RunTracker.DisarmIronClubDrawAttribution(owner);
                return;
            }

            __result.ContinueWith(
                _ => RunTracker.DisarmIronClubDrawAttribution(owner),
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"IronClubAfterCardPlayedPatch.Postfix failed: {e.Message}");
        }
    }
}

/// <summary>
/// Captures the actual cards drawn by Iron Club's draw command.
/// </summary>
[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Draw), new[] { typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool) })]
public static class IronClubCardPileDrawPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player, out bool __state)
    {
        __state = false;

        try
        {
            __state = RunTracker.TryConsumeIronClubDrawAttribution(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"IronClubCardPileDrawPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(bool __state, Task<IEnumerable<CardModel>> __result)
    {
        if (!__state || __result == null) return;
        ObserveDrawResultAsync(__result);
    }

    private static async void ObserveDrawResultAsync(Task<IEnumerable<CardModel>> drawTask)
    {
        try
        {
            var cards = await drawTask.ConfigureAwait(false);
            int cardsDrawn = cards?.Count(card => card != null) ?? 0;
            RunTracker.RecordIronClubCardsDrawn(cardsDrawn);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"IronClubCardPileDrawPatch draw observation failed: {e.Message}");
        }
    }
}
