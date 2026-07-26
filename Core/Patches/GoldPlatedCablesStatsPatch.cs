using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Orbs;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Uses the game's completed modifier list as the source of truth for each
/// Gold-Plated Cables activation. The orb argument is the exact first orb
/// whose passive count the relic increased.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterModifyingOrbPassiveTriggerCount))]
public static class HookAfterModifyingOrbPassiveTriggerCountGoldPlatedCablesPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        OrbModel orb,
        IEnumerable<AbstractModel>? modifiers)
    {
        try
        {
            if (orb == null || modifiers == null) return;

            foreach (var relic in modifiers)
            {
                if (relic is GoldPlatedCables cables)
                    RunTracker.RecordGoldPlatedCablesActivation(cables, orb);
            }
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"HookAfterModifyingOrbPassiveTriggerCountGoldPlatedCablesPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// No orb means the modifier chain above is never entered. Sample that empty
/// state at the exact queue pass that would otherwise trigger end-turn orb
/// passives.
/// </summary>
[HarmonyPatch(typeof(OrbQueue), nameof(OrbQueue.BeforeTurnEnd))]
public static class OrbQueueBeforeTurnEndGoldPlatedCablesPatch
{
    [HarmonyPrefix]
    public static void Prefix(OrbQueue __instance)
    {
        try
        {
            if (__instance?.Orbs.Count != 0) return;
            RunTracker.RecordGoldPlatedCablesNoOrbTurnEnd(__instance._owner);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"OrbQueueBeforeTurnEndGoldPlatedCablesPatch failed: {e.Message}");
        }
    }
}
