using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace SpireLens.Core.Patches;

/// <summary>
/// Arm a one-shot attribution window right as Poison begins its start-of-turn
/// tick for a creature. The resulting DamageReceivedEntry often arrives with
/// CardSource=null, so we need this hook to recognize the next null-source hit
/// on that creature as poison instead of generic anonymous damage.
/// </summary>
[HarmonyPatch]
public static class PoisonPowerAfterSideTurnStartPatch
{
    private static MethodBase? TargetMethod()
    {
        var poisonType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Powers.PoisonPower");
        return poisonType == null ? null : AccessTools.Method(poisonType, "AfterSideTurnStart");
    }

    // PoisonPower.AfterSideTurnStart bails unless participants.Contains(base.Owner)
    // (verified PoisonPower.cs line 57). Bind participants by name and only arm
    // when the tick will actually fire.
    [HarmonyPrefix]
    public static void Prefix(object __instance, IReadOnlyList<Creature> participants)
    {
        try
        {
            if (__instance != null)
                RunTracker.NotePoisonTickStarting(__instance, participants);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"PoisonPowerAfterSideTurnStartPatch failed: {e.Message}");
        }
    }
}
