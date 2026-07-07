using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Lizard Tail's one-shot revive using the standard observed relic
/// healing window.
/// </summary>
[HarmonyPatch(typeof(LizardTail), nameof(LizardTail.AfterPreventingDeath))]
public static class LizardTailAfterPreventingDeathPatch
{
    private const string RelicId = "RELIC.LIZARD_TAIL";
    private const decimal FallbackHealPercent = 50m;

    [HarmonyPrefix]
    public static void Prefix(LizardTail __instance, Creature creature)
    {
        try
        {
            if (__instance == null || creature == null) return;
            if (__instance.WasUsed) return;
            if (!ReferenceEquals(creature, __instance.Owner?.Creature)) return;

            decimal healPercent = __instance.DynamicVars?.Heal?.BaseValue ?? FallbackHealPercent;
            decimal attemptedHealing = Math.Max(1m, creature.MaxHp * (healPercent / 100m));
            RunTracker.RecordLizardTailTrigger(__instance, creature, attemptedHealing);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"LizardTailAfterPreventingDeathPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(LizardTail __instance, Creature creature, Task __result)
    {
        try
        {
            if (__instance == null || creature == null) return;
            if (!ReferenceEquals(creature, __instance.Owner?.Creature)) return;

            if (__result == null || __result.IsCompleted)
            {
                RunTracker.FinalizeRelicHealing(creature, RelicId);
                return;
            }

            __result.ContinueWith(
                _ => RunTracker.FinalizeRelicHealing(creature, RelicId),
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"LizardTailAfterPreventingDeathPatch.Postfix failed: {e.Message}");
        }
    }
}
