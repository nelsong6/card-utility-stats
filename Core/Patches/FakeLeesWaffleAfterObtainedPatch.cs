using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Lee's Waffle???'s owner-specific percentage heal through the shared
/// relic-healing ledger so restored and blocked HP remain separate.
/// </summary>
[HarmonyPatch(typeof(FakeLeesWaffle), nameof(FakeLeesWaffle.AfterObtained))]
public static class FakeLeesWaffleAfterObtainedPatch
{
    private const string RelicId = "RELIC.FAKE_LEES_WAFFLE";

    [HarmonyPrefix]
    public static void Prefix(FakeLeesWaffle __instance, out bool __state)
    {
        __state = false;

        try
        {
            if (__instance == null || !RunTracker.IsTrackedRelic(__instance)) return;

            var creature = __instance.Owner?.Creature;
            if (creature == null || creature.IsDead) return;

            var attemptedHealing =
                creature.MaxHp * (__instance.DynamicVars.Heal.BaseValue / 100m);
            if (attemptedHealing <= 0m) return;

            RunTracker.RecordFakeLeesWaffleTrigger(creature, attemptedHealing);
            __state = true;
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"FakeLeesWaffleAfterObtainedPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(
        FakeLeesWaffle __instance,
        Task __result,
        bool __state)
    {
        try
        {
            if (!__state) return;

            var healedCreature = __instance?.Owner?.Creature;
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
            CoreMain.LogDebug($"FakeLeesWaffleAfterObtainedPatch.Postfix failed: {e.Message}");
        }
    }
}
