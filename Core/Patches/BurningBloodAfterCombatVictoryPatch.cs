using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Burning Blood's owner-specific combat-victory heal. The actual HP
/// restored is observed through Hook.AfterCurrentHpChanged while this relic
/// healing window is armed.
/// </summary>
[HarmonyPatch]
public static class BurningBloodAfterCombatVictoryPatch
{
    private const string RelicId = "RELIC.BURNING_BLOOD";

    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.BurningBlood");
        return t == null ? null : AccessTools.Method(t, "AfterCombatVictory");
    }

    [HarmonyPrefix]
    public static void Prefix(BurningBlood __instance, CombatRoom _)
    {
        try
        {
            var healedCreature = __instance.Owner?.Creature;
            if (healedCreature == null || healedCreature.IsDead) return;

            decimal attemptedHealing = __instance.DynamicVars?.Heal?.BaseValue ?? 0m;
            RunTracker.RecordBurningBloodTrigger(healedCreature, attemptedHealing);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BurningBloodAfterCombatVictoryPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(BurningBlood __instance, Task __result)
    {
        try
        {
            var healedCreature = __instance.Owner?.Creature;
            if (healedCreature == null) return;

            if (__result == null || __result.IsCompleted)
            {
                RunTracker.FinalizeRelicHealing(healedCreature, RelicId);
                return;
            }

            __result.ContinueWith(
                _ => RunTracker.FinalizeRelicHealing(healedCreature, RelicId),
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BurningBloodAfterCombatVictoryPatch.Postfix failed: {e.Message}");
        }
    }
}
