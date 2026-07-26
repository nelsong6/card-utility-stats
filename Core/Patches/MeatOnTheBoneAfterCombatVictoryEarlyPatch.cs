using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Meat on the Bone's owner-specific post-combat heal through the
/// shared relic-healing ledger.
/// </summary>
[HarmonyPatch(typeof(MeatOnTheBone), nameof(MeatOnTheBone.AfterCombatVictoryEarly))]
public static class MeatOnTheBoneAfterCombatVictoryEarlyPatch
{
    private const string RelicId = "RELIC.MEAT_ON_THE_BONE";

    [HarmonyPrefix]
    public static void Prefix(MeatOnTheBone __instance, CombatRoom _, out bool __state)
    {
        __state = false;

        try
        {
            if (__instance == null) return;

            var healedCreature = __instance.Owner?.Creature;
            if (healedCreature == null || healedCreature.IsDead) return;

            var thresholdPercent = __instance.DynamicVars["HpThreshold"].BaseValue;
            if (!ShouldActivate(healedCreature.CurrentHp, healedCreature.MaxHp, thresholdPercent))
                return;

            var attemptedHealing = __instance.DynamicVars.Heal.BaseValue;
            if (attemptedHealing <= 0m) return;

            RunTracker.RecordMeatOnTheBoneTrigger(healedCreature, attemptedHealing);
            __state = true;
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"MeatOnTheBoneAfterCombatVictoryEarlyPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(MeatOnTheBone __instance, Task __result, bool __state)
    {
        try
        {
            if (!__state) return;
            if (__instance == null) return;

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
            CoreMain.LogDebug($"MeatOnTheBoneAfterCombatVictoryEarlyPatch.Postfix failed: {e.Message}");
        }
    }

    internal static bool ShouldActivate(
        decimal currentHp,
        decimal maxHp,
        decimal thresholdPercent)
    {
        var thresholdHp = (int)(maxHp * (thresholdPercent / 100m));
        return currentHp <= thresholdHp;
    }
}
