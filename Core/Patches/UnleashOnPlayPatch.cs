using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Cards;

namespace SpireLens.Core.Patches;

/// <summary>
/// Captures Unleash's card-specific Osty HP scaling at the owner callback.
/// The normal damage ledger still records observed damage from the resulting
/// AttackCommand; this records how much of that attack payload came from
/// Osty's current HP at play time.
/// </summary>
[HarmonyPatch(typeof(Unleash), "OnPlay")]
public static class UnleashOnPlayPatch
{
    [HarmonyPrefix]
    public static void Prefix(Unleash __instance, CardPlay cardPlay)
    {
        try
        {
            if (cardPlay?.Target == null) return;

            var osty = __instance.Owner?.Osty;
            if (osty == null || !osty.IsAlive) return;

            int ostyHpBonus = osty.CurrentHp;
            if (ostyHpBonus <= 0) return;

            RunTracker.RecordUnleashOstyHpAttackBonus(__instance, ostyHpBonus);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"UnleashOnPlayPatch.Prefix failed: {e.Message}");
        }
    }
}
