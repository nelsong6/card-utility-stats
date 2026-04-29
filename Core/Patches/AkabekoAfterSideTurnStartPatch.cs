using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;

namespace SpireLens.Core.Patches;

/// <summary>
/// Arms a short-lived attribution window around Akabeko's turn-start Vigor
/// application. The prefix arms the flag so that any VigorPower gain observed
/// in <see cref="HookBeforePowerAmountChangedPatch"/> during this call is
/// credited to the relic. The postfix disarms as a safety cleanup.
/// </summary>
[HarmonyPatch]
public static class AkabekoAfterSideTurnStartPatch
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.Akabeko");
        return t == null ? null : AccessTools.Method(t, "AfterSideTurnStart");
    }

    [HarmonyPrefix]
    public static void Prefix(CombatSide side)
    {
        try
        {
            if (side != CombatSide.Player) return;
            RunTracker.ArmAkabekoVigorAttribution();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"AkabekoAfterSideTurnStartPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(CombatSide side)
    {
        try
        {
            if (side != CombatSide.Player) return;
            RunTracker.DisarmAkabekoVigorAttribution();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"AkabekoAfterSideTurnStartPatch.Postfix failed: {e.Message}");
        }
    }
}
