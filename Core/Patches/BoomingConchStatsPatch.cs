using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;

namespace SpireLens.Core.Patches;

/// <summary>
/// Arms Booming Conch energy attribution at Elite combat start. The actual
/// energy gained is measured by PlayerCombatState.GainEnergy.
/// </summary>
[HarmonyPatch]
public static class BoomingConchAfterSideTurnStartPatch
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.BoomingConch");
        return t == null ? null : AccessTools.Method(t, "AfterSideTurnStart");
    }

    [HarmonyPrefix]
    public static void Prefix(
        RelicModel __instance,
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        try
        {
            if (!TurnEnergyRelicPatchHelpers.TryGetTrackedOwnerOnPlayerTurn(__instance, side, participants, out var owner)) return;
            if (owner.PlayerCombatState == null || owner.PlayerCombatState.TurnNumber > 1) return;
            if (combatState?.RunState?.CurrentRoom?.RoomType != RoomType.Elite) return;

            RunTracker.ArmBoomingConchEnergyAttribution(owner);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BoomingConchAfterSideTurnStartPatch failed: {e.Message}");
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
