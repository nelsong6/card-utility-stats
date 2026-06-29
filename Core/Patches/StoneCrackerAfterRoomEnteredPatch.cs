using System;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Stone Cracker's combat-room activation from the relic's own
/// callback. The game filters the owner's deck to upgradable cards, shuffles,
/// then upgrades up to the relic's Cards dynamic var; this patch records the
/// resulting selected count before the game mutates those cards.
/// </summary>
[HarmonyPatch(typeof(StoneCracker), nameof(StoneCracker.AfterRoomEntered))]
public static class StoneCrackerAfterRoomEnteredPatch
{
    [HarmonyPrefix]
    public static void Prefix(StoneCracker __instance, AbstractRoom room)
    {
        try
        {
            if (__instance?.Owner == null) return;
            if (room is not CombatRoom) return;

            int requested = Math.Max(0, __instance.DynamicVars.Cards.IntValue);
            int eligible = __instance.Owner.Deck.Cards.Count(card => card?.IsUpgradable == true);

            RunTracker.RecordStoneCrackerActivation(Math.Min(requested, eligible));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"StoneCrackerAfterRoomEnteredPatch failed: {e.Message}");
        }
    }
}
