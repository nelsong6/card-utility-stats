using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Hooks;

namespace SpireLens.Core.Patches;

/// <summary>
/// Arms Booming Conch energy attribution at Elite combat start. The actual
/// energy gained is measured by PlayerCombatState.GainEnergy.
/// </summary>
[HarmonyPatch]
public static class BoomingConchBeforeSideTurnStartPatch
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.BoomingConch");
        return t == null ? null : AccessTools.Method(t, "BeforeSideTurnStart");
    }

    [HarmonyPrefix]
    public static void Prefix(CombatSide side, ICombatState combatState)
    {
        try
        {
            if (side != CombatSide.Player) return;
            if (combatState == null || combatState.RoundNumber != 1) return;
            RunTracker.ArmBoomingConchEnergyAttribution();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BoomingConchBeforeSideTurnStartPatch failed: {e.Message}");
        }
    }
}

[HarmonyPatch]
public static class BoomingConchModifyHandDrawPatch
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.BoomingConch");
        return t == null ? null : AccessTools.Method(t, "ModifyHandDraw");
    }

    [HarmonyPostfix]
    public static void Postfix(decimal count, decimal __result)
    {
        try
        {
            var added = __result - count;
            if (added <= 0m) return;
            RunTracker.RecordBoomingConchDraw((int)added);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BoomingConchModifyHandDrawPatch failed: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
public static class HookAfterPlayerTurnStartBoomingConchCleanupPatch
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        try
        {
            RunTracker.DisarmBoomingConchEnergyAttribution();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookAfterPlayerTurnStartBoomingConchCleanupPatch failed: {e.Message}");
        }
    }
}
