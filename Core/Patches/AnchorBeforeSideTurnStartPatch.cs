using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Anchor's combat-start Block gain so the relic tooltip can show
/// total Block gained across the run.
/// </summary>
[HarmonyPatch]
public static class AnchorBeforeSideTurnStartPatch
{
    private const int AnchorBlockAmount = 10;

    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.Anchor");
        return t == null ? null : AccessTools.Method(t, "BeforeSideTurnStart");
    }

    [HarmonyPostfix]
    public static void Postfix(CombatSide side, ICombatState combatState)
    {
        try
        {
            if (side != CombatSide.Player) return;
            if (combatState == null) return;
            if (combatState.RoundNumber != 1) return;

            RunTracker.RecordAnchorApplication(AnchorBlockAmount);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"AnchorBeforeSideTurnStartPatch failed: {e.Message}");
        }
    }
}
