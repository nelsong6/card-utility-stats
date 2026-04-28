using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Bag of Marbles' combat-start Vulnerable application so the relic
/// tooltip can show how many enemies were affected across the run.
/// </summary>
[HarmonyPatch]
public static class BagOfMarblesBeforeSideTurnStartPatch
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.BagOfMarbles");
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

            int enemyCount = combatState.Enemies.Count(e => e.IsAlive);
            if (enemyCount <= 0) return;

            RunTracker.RecordBagOfMarblesApplication(enemyCount);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BagOfMarblesBeforeSideTurnStartPatch failed: {e.Message}");
        }
    }
}
