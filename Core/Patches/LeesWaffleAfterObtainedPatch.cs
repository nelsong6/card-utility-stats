using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Lee's Waffle pickup HP as the observed current-HP delta across the
/// full pickup effect, including the max-HP grant and the follow-up heal.
/// </summary>
[HarmonyPatch]
public static class LeesWaffleAfterObtainedPatch
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.LeesWaffle");
        return t == null ? null : AccessTools.Method(t, "AfterObtained");
    }

    [HarmonyPrefix]
    public static void Prefix(LeesWaffle __instance, out PickupState __state)
    {
        __state = default;

        try
        {
            if (__instance == null || !RunTracker.IsTrackedRelic(__instance)) return;

            var creature = __instance.Owner?.Creature;
            if (creature == null || creature.IsDead) return;

            __state = new PickupState(creature, creature.CurrentHp);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"LeesWaffleAfterObtainedPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(PickupState __state, Task __result)
    {
        try
        {
            if (__state.Creature == null) return;

            if (__result == null)
            {
                ObservePickup(__state);
                return;
            }

            if (__result.IsCompleted)
            {
                if (!__result.IsCanceled && !__result.IsFaulted)
                    ObservePickup(__state);
                return;
            }

            __result.ContinueWith(
                task =>
                {
                    if (!task.IsCanceled && !task.IsFaulted)
                        ObservePickup(__state);
                },
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"LeesWaffleAfterObtainedPatch.Postfix failed: {e.Message}");
        }
    }

    private static void ObservePickup(PickupState state)
    {
        try
        {
            if (state.Creature == null) return;

            decimal hpGained = Math.Max(0m, state.Creature.CurrentHp - state.InitialCurrentHp);
            RunTracker.RecordLeesWafflePickupHpGained(hpGained);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"LeesWaffleAfterObtainedPatch.ObservePickup failed: {e.Message}");
        }
    }

    public readonly record struct PickupState(Creature? Creature, decimal InitialCurrentHp);
}
