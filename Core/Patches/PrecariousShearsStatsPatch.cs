using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Tracks Precarious Shears' pickup card removals and max-HP cost across the
/// async pickup flow.
/// </summary>
[HarmonyPatch]
public static class PrecariousShearsAfterObtainedPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(
            typeof(PrecariousShears),
            nameof(PrecariousShears.AfterObtained),
            Type.EmptyTypes);
    }

    [HarmonyPrefix]
    public static void Prefix(PrecariousShears __instance, out PickupState __state)
    {
        __state = default;

        try
        {
            if (__instance == null) return;
            if (RunTracker.BeginPrecariousShearsPickup(__instance, out var player))
                __state = new PickupState(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"PrecariousShearsAfterObtainedPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(PickupState __state, Task __result)
    {
        try
        {
            if (__state.Player == null) return;

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
            CoreMain.LogDebug($"PrecariousShearsAfterObtainedPatch.Postfix failed: {e.Message}");
        }
    }

    private static void Complete(PickupState state, bool succeeded)
    {
        try
        {
            RunTracker.CompletePrecariousShearsPickup(state.Player, succeeded);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"PrecariousShearsAfterObtainedPatch.Complete failed: {e.Message}");
        }
    }

    public readonly record struct PickupState(Player? Player);
}
