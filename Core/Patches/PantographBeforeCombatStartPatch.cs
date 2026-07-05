using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Pantograph's owner-specific boss-combat heal. The actual HP
/// restored is observed through Hook.AfterCurrentHpChanged while this relic
/// healing window is armed.
/// </summary>
[HarmonyPatch(typeof(Pantograph), nameof(Pantograph.BeforeCombatStart))]
public static class PantographBeforeCombatStartPatch
{
    private const string RelicId = "RELIC.PANTOGRAPH";

    [HarmonyPrefix]
    public static void Prefix(Pantograph __instance)
    {
        try
        {
            if (__instance == null || !RunTracker.IsTrackedRelic(__instance)) return;

            var healedCreature = __instance.Owner?.Creature;
            if (healedCreature == null || healedCreature.IsDead) return;
            if (__instance.Owner?.RunState?.CurrentRoom?.RoomType != RoomType.Boss) return;

            decimal attemptedHealing = __instance.DynamicVars?.Heal?.BaseValue ?? 0m;
            RunTracker.RecordPantographTrigger(healedCreature, attemptedHealing);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"PantographBeforeCombatStartPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(Pantograph __instance, Task __result)
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
            CoreMain.LogDebug($"PantographBeforeCombatStartPatch.Postfix failed: {e.Message}");
        }
    }
}
