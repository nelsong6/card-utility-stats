using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Tracks current HP across Fragrant Mushroom's async pickup damage and the
/// cards it actually upgrades. Card names are captured from
/// CardModel.UpgradeInternal while this pickup window is armed.
/// </summary>
[HarmonyPatch]
public static class FragrantMushroomAfterObtainedPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(
            typeof(FragrantMushroom),
            nameof(FragrantMushroom.AfterObtained),
            Type.EmptyTypes);
    }

    [HarmonyPrefix]
    public static void Prefix(FragrantMushroom __instance, out PickupState __state)
    {
        __state = default;

        try
        {
            if (__instance == null) return;
            if (RunTracker.BeginFragrantMushroomPickup(__instance, out var player))
                __state = new PickupState(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"FragrantMushroomAfterObtainedPatch.Prefix failed: {e.Message}");
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
            CoreMain.LogDebug($"FragrantMushroomAfterObtainedPatch.Postfix failed: {e.Message}");
        }
    }

    private static void Complete(PickupState state, bool succeeded)
    {
        try
        {
            RunTracker.CompleteFragrantMushroomPickup(state.Player, succeeded);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"FragrantMushroomAfterObtainedPatch.Complete failed: {e.Message}");
        }
    }

    public readonly record struct PickupState(Player? Player);
}
