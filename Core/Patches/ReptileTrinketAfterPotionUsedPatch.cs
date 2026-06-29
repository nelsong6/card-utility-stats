using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Reptile Trinket's potion-use activation from the relic's own
/// callback. The game only applies the Strength power when the used potion
/// belongs to the relic owner and combat is in progress; this patch mirrors
/// those checks and records the same Strength dynamic var payload.
/// </summary>
[HarmonyPatch(typeof(ReptileTrinket), nameof(ReptileTrinket.AfterPotionUsed))]
public static class ReptileTrinketAfterPotionUsedPatch
{
    [HarmonyPrefix]
    public static void Prefix(ReptileTrinket __instance, PotionModel potion, Creature target)
    {
        try
        {
            if (__instance?.Owner == null) return;
            if (potion?.Owner == null) return;
            if (!ReferenceEquals(potion.Owner, __instance.Owner)) return;
            if (CombatManager.Instance?.IsInProgress != true) return;

            var strengthAdded = __instance.DynamicVars.Strength.BaseValue;
            RunTracker.RecordReptileTrinketActivation(strengthAdded);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"ReptileTrinketAfterPotionUsedPatch failed: {e.Message}");
        }
    }
}
