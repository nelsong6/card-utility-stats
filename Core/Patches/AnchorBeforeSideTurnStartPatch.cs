using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Hooks;

namespace SpireLens.Core.Patches;

/// <summary>
/// Arms Anchor block attribution at the relic's combat-start callback. The
/// actual block gained is observed by Hook.AfterBlockGained.
/// </summary>
[HarmonyPatch]
public static class AnchorBeforeSideTurnStartPatch
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.Anchor");
        return t == null ? null : AccessTools.Method(t, "BeforeSideTurnStart");
    }

    [HarmonyPrefix]
    public static void Prefix(CombatSide side, ICombatState combatState)
    {
        try
        {
            if (side != CombatSide.Player) return;
            if (combatState == null || combatState.RoundNumber != 1) return;
            RunTracker.ArmAnchorBlockAttribution();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"AnchorBeforeSideTurnStartPatch failed: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
public static class HookAfterPlayerTurnStartAnchorCleanupPatch
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        try
        {
            RunTracker.DisarmAnchorBlockAttribution();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookAfterPlayerTurnStartAnchorCleanupPatch failed: {e.Message}");
        }
    }
}
