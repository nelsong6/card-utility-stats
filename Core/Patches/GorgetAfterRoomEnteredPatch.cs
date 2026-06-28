using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Gorget's combat-room activation from the relic's own callback.
/// The game applies Plating only after confirming the entered room is a
/// CombatRoom; this patch mirrors that check and records the same dynamic
/// Plating payload.
/// </summary>
[HarmonyPatch(typeof(Gorget), nameof(Gorget.AfterRoomEntered))]
public static class GorgetAfterRoomEnteredPatch
{
    [HarmonyPrefix]
    public static void Prefix(Gorget __instance, AbstractRoom room)
    {
        try
        {
            if (__instance == null) return;
            if (room is not CombatRoom) return;

            var platingAdded = __instance.DynamicVars["PlatingPower"].BaseValue;
            RunTracker.RecordGorgetActivation(platingAdded);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"GorgetAfterRoomEnteredPatch failed: {e.Message}");
        }
    }
}
