using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records the extra debuff amount Unsettling Lamp contributes when it doubles
/// a triggering card's enemy debuffs.
/// </summary>
[HarmonyPatch]
public static class UnsettlingLampModifyPowerAmountGivenPatch
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.UnsettlingLamp");
        return t == null ? null : AccessTools.Method(t, "ModifyPowerAmountGivenMultiplicative");
    }

    [HarmonyPostfix]
    public static void Postfix(
        RelicModel __instance,
        PowerModel power,
        Creature giver,
        decimal amount,
        Creature? target,
        CardModel? cardSource,
        decimal __result)
    {
        try
        {
            RunTracker.RecordUnsettlingLampDebuffMultiplier(
                __instance,
                power,
                giver,
                amount,
                target,
                cardSource,
                __result);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"UnsettlingLampModifyPowerAmountGivenPatch failed: {e.Message}");
        }
    }
}
