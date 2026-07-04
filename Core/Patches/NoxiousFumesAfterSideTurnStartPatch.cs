using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace SpireLens.Core.Patches;

/// <summary>
/// Arm a short-lived attribution window right as Noxious Fumes begins its
/// turn-start tick. The ensuing poison applications do not carry a card
/// source, so this hook lets the tracker route them back through the live
/// Noxious Fumes effect source before the normal poison ledger takes over.
/// </summary>
[HarmonyPatch]
public static class NoxiousFumesAfterSideTurnStartPatch
{
    private static MethodBase? TargetMethod()
    {
        var noxiousFumesType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Powers.NoxiousFumesPower");
        return noxiousFumesType == null ? null : AccessTools.Method(noxiousFumesType, "AfterSideTurnStart");
    }

    // NoxiousFumesPower.AfterSideTurnStart bails unless participants.Contains(base.Owner)
    // (verified NoxiousFumesPower.cs line 28). Gate the arm on owner participation.
    [HarmonyPrefix]
    public static void Prefix(object __instance, IReadOnlyList<Creature> participants)
    {
        try
        {
            if (__instance != null)
                RunTracker.NoteNoxiousFumesTick(__instance, participants);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"NoxiousFumesAfterSideTurnStartPatch failed: {e.Message}");
        }
    }
}
