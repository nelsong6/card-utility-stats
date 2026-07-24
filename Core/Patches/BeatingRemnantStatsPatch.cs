using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Observes the exact amount Beating Remnant removes from an HP-loss event.
/// This runs at the relic's own modifier, so other prevention is not credited
/// to Beating Remnant.
/// </summary>
[HarmonyPatch(typeof(BeatingRemnant), nameof(BeatingRemnant.ModifyHpLostAfterOsty))]
public static class BeatingRemnantModifyHpLostAfterOstyStatsPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        BeatingRemnant __instance,
        Creature target,
        decimal amount,
        decimal __result)
    {
        try
        {
            RunTracker.RecordBeatingRemnantHpLossPrevented(
                __instance,
                target,
                amount,
                __result);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"BeatingRemnantModifyHpLostAfterOstyStatsPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Counts every owner turn where Beating Remnant is held, including turns
/// where it prevents no HP loss. This is also the boundary where the relic
/// resets its own per-turn damage counter.
/// </summary>
[HarmonyPatch(typeof(BeatingRemnant), nameof(BeatingRemnant.BeforeSideTurnStart))]
public static class BeatingRemnantBeforeSideTurnStartStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        BeatingRemnant __instance,
        IReadOnlyList<Creature> participants)
    {
        try
        {
            if (__instance?.Owner?.Creature == null) return;
            if (participants == null || !participants.Contains(__instance.Owner.Creature)) return;

            RunTracker.RecordBeatingRemnantTurnStarted(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"BeatingRemnantBeforeSideTurnStartStatsPatch failed: {e.Message}");
        }
    }
}
