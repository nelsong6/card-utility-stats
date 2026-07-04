using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;

namespace SpireLens.Core.Patches;

/// <summary>
/// Arms Akabeko attribution at the relic's combat-start callback. The actual
/// Vigor amount is observed at the power-change hook.
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
    public static void Prefix(CombatSide side, ICombatState combatState)
    {
        PatchGuard.Run(nameof(AkabekoAfterSideTurnStartPatch), () =>
        {
            if (side != CombatSide.Player) return;
            if (combatState == null || combatState.RoundNumber != 1) return;
            RunTracker.ArmAkabekoVigorAttribution();
        });
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.BeforePowerAmountChanged))]
public static class HookBeforePowerAmountChangedAkabekoPatch
{
    private static readonly Type? VigorPowerType =
        AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Powers.VigorPower");

    [HarmonyPostfix]
    public static void Postfix(PowerModel power, decimal amount, Creature target)
    {
        try
        {
            if (VigorPowerType == null) return;
            if (target == null || !target.IsPlayer) return;
            if (amount <= 0m) return;
            if (!VigorPowerType.IsInstanceOfType(power)) return;
            RunTracker.RecordAkabekoVigorGained((int)amount);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookBeforePowerAmountChangedAkabekoPatch failed: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
public static class HookAfterPlayerTurnStartAkabekoCleanupPatch
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        try
        {
            RunTracker.DisarmAkabekoVigorAttribution();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookAfterPlayerTurnStartAkabekoCleanupPatch failed: {e.Message}");
        }
    }
}
