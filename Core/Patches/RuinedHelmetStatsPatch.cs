using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Captures Ruined Helmet's exact extra Strength at its receiver-modifier
/// boundary, then commits it only when the game's matching post-modification
/// callback confirms that the power was actually applied.
/// </summary>
[HarmonyPatch(typeof(RuinedHelmet), nameof(RuinedHelmet.TryModifyPowerAmountReceived))]
public static class RuinedHelmetModifyStrengthStatsPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        RuinedHelmet __instance,
        PowerModel canonicalPower,
        Creature target,
        decimal amount,
        Creature? applier,
        ref decimal modifiedAmount,
        bool __result)
    {
        try
        {
            if (!__result) return;

            RunTracker.StageRuinedHelmetStrengthGain(
                __instance,
                canonicalPower,
                target,
                amount,
                modifiedAmount);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RuinedHelmetModifyStrengthStatsPatch failed: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(RuinedHelmet), nameof(RuinedHelmet.AfterModifyingPowerAmountReceived))]
public static class RuinedHelmetAppliedStrengthStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(RuinedHelmet __instance, PowerModel power)
    {
        try
        {
            RunTracker.CompleteRuinedHelmetStrengthGain(__instance, power);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RuinedHelmetAppliedStrengthStatsPatch failed: {e.Message}");
        }
    }
}
