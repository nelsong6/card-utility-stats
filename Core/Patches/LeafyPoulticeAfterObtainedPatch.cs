using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Leafy Poultice's pickup max-HP loss by observing the owner's max HP
/// before and after the async pickup effect resolves.
/// </summary>
[HarmonyPatch]
public static class LeafyPoulticeAfterObtainedPatch
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.LeafyPoultice");
        return t == null ? null : AccessTools.Method(t, "AfterObtained");
    }

    [HarmonyPrefix]
    public static void Prefix(LeafyPoultice __instance, out PickupState __state)
    {
        __state = default;

        try
        {
            if (__instance == null || !RunTracker.IsTrackedRelic(__instance)) return;

            var creature = __instance.Owner?.Creature;
            if (creature == null || creature.IsDead) return;

            __state = new PickupState(creature, creature.MaxHp);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"LeafyPoulticeAfterObtainedPatch.Prefix failed: {e.Message}");
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
            CoreMain.LogDebug($"LeafyPoulticeAfterObtainedPatch.Postfix failed: {e.Message}");
        }
    }

    private static void ObservePickup(PickupState state)
    {
        try
        {
            if (state.Creature == null) return;

            RunTracker.RecordLeafyPoulticeMaxHpChanged(state.InitialMaxHp, state.Creature.MaxHp);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"LeafyPoulticeAfterObtainedPatch.ObservePickup failed: {e.Message}");
        }
    }

    public readonly record struct PickupState(Creature? Creature, decimal InitialMaxHp);
}
