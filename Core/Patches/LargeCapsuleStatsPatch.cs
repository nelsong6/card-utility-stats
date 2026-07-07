using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Large Capsule grants relics during its async pickup callback. The callback
/// does not expose the granted relics directly, so compare the player's relic
/// inventory before and after pickup completion.
/// </summary>
[HarmonyPatch]
public static class LargeCapsuleAfterObtainedPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(
            typeof(LargeCapsule),
            nameof(LargeCapsule.AfterObtained),
            Type.EmptyTypes);
    }

    [HarmonyPrefix]
    public static void Prefix(LargeCapsule __instance, out PickupState __state)
    {
        __state = default;

        try
        {
            if (__instance == null) return;
            if (RunTracker.BeginLargeCapsulePickup(__instance, out var player, out var relicsBeforePickup))
                __state = new PickupState(player, relicsBeforePickup);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"LargeCapsuleAfterObtainedPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(PickupState __state, Task __result)
    {
        try
        {
            if (__state.Player == null || __state.RelicsBeforePickup == null) return;

            if (__result == null)
            {
                Complete(__state, succeeded: true);
                return;
            }

            if (__result.IsCompleted)
            {
                Complete(__state, succeeded: !__result.IsCanceled && !__result.IsFaulted);
                return;
            }

            __result.ContinueWith(
                task => Complete(__state, succeeded: !task.IsCanceled && !task.IsFaulted),
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"LargeCapsuleAfterObtainedPatch.Postfix failed: {e.Message}");
        }
    }

    private static void Complete(PickupState state, bool succeeded)
    {
        try
        {
            RunTracker.CompleteLargeCapsulePickup(state.Player, state.RelicsBeforePickup, succeeded);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"LargeCapsuleAfterObtainedPatch.Complete failed: {e.Message}");
        }
    }

    public readonly record struct PickupState(
        Player? Player,
        IReadOnlyCollection<RelicModel>? RelicsBeforePickup);
}
