using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Hooks;

namespace SpireLens.Core.Patches;

/// <summary>
/// Arms the Happy Flower energy attribution window at the start of the player's
/// turn. Happy Flower grants 1 energy every 3 turns via <c>AfterSideTurnStart</c>;
/// the energy call itself is captured by <see cref="PlayerGainEnergyPatch"/>.
/// </summary>
[HarmonyPatch]
public static class HappyFlowerAfterSideTurnStartPatch
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.HappyFlower");
        if (t == null) return null;
        return AccessTools.Method(t, "AfterSideTurnStart");
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
            CoreMain.LogDebug($"HappyFlowerAfterSideTurnStartPatch.Prefix failed: {e.Message}");
        }
    }
}

/// <summary>
/// Clears any armed Happy Flower attribution window after all turn-start hooks
/// have processed for the player's turn. If Happy Flower did not fire its
/// energy grant this turn (counter not yet at 3), the flag is discarded here
/// so it does not leak into later energy-gain events during the player's turn.
/// Mirrors the later-boundary cleanup pattern used by Orichalcum's
/// <c>Hook.AfterSideTurnEnd</c> patch.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
public static class HookAfterPlayerTurnStartHappyFlowerCleanupPatch
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        try
        {
            RunTracker.DisarmHappyFlowerEnergyAttribution();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookAfterPlayerTurnStartHappyFlowerCleanupPatch failed: {e.Message}");
        }
    }
}
