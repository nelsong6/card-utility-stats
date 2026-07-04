using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Pendulum when its owner-specific player-turn callback is about to
/// wrap its turn counter and perform the draw.
/// </summary>
[HarmonyPatch(typeof(Pendulum), nameof(Pendulum.AfterPlayerTurnStart))]
public static class PendulumAfterPlayerTurnStartPatch
{
    [HarmonyPrefix]
    public static void Prefix(Pendulum __instance, Player player)
    {
        try
        {
            if (__instance?.Owner == null || player == null) return;
            if (!ReferenceEquals(player, __instance.Owner)) return;

            int turns = Math.Max(1, __instance.DynamicVars["Turns"].IntValue);
            int nextTurnsSeen = (__instance.TurnsSeen + 1) % turns;
            if (nextTurnsSeen != 0) return;

            RunTracker.ArmPendulumAttribution(__instance.Owner);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"PendulumAfterPlayerTurnStartPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Captures the actual number of cards drawn by Pendulum's draw command.
/// </summary>
[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Draw), new[] { typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool) })]
public static class PendulumCardPileDrawPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player, out bool __state)
    {
        __state = false;

        try
        {
            __state = RunTracker.TryConsumePendulumDrawAttribution(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"PendulumCardPileDrawPatch.Prefix failed: {e.Message}");
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
            RunTracker.RecordPendulumCardsDrawn(cardsDrawn);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"PendulumCardPileDrawPatch draw observation failed: {e.Message}");
        }
    }
}
