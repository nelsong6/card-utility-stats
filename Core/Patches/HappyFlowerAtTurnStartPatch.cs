using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;

namespace SpireLens.Core.Patches;

/// <summary>
/// Arms the Happy Flower energy attribution window when the relic's turn-start
/// hook fires on the player's side. When Happy Flower's every-3-turns condition
/// is met it calls GainEnergy, which <see cref="PlayerGainEnergyPatch"/> captures
/// and routes to <see cref="RunTracker.RecordHappyFlowerEnergyGained"/>.
/// The postfix disarms the flag as a safety reset for turns where the condition
/// is not met.
/// </summary>
[HarmonyPatch]
public static class HappyFlowerAtTurnStartPatch
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.HappyFlower");
        return t == null ? null : AccessTools.Method(t, "AtTurnStart");
    }

    [HarmonyPrefix]
    public static void Prefix(CombatSide side)
    {
        try
        {
            if (side != CombatSide.Player) return;
            RunTracker.ArmHappyFlowerEnergyAttribution();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HappyFlowerAtTurnStartPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(CombatSide side)
    {
        try
        {
            if (side != CombatSide.Player) return;
            RunTracker.DisarmHappyFlowerEnergyAttribution();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HappyFlowerAtTurnStartPatch.Postfix failed: {e.Message}");
        }
    }
}
