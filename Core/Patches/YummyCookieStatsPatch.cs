using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Tracks the cards Yummy Cookie actually upgrades during its async pickup
/// effect. Card names are captured from CardModel.UpgradeInternal while this
/// pickup window is armed.
/// </summary>
[HarmonyPatch]
public static class YummyCookieAfterObtainedPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(
            typeof(YummyCookie),
            nameof(YummyCookie.AfterObtained),
            Type.EmptyTypes);
    }

    [HarmonyPrefix]
    public static void Prefix(YummyCookie __instance, out PickupState __state)
    {
        __state = default;

        try
        {
            if (__instance == null) return;
            if (RunTracker.BeginYummyCookiePickup(__instance, out var player))
                __state = new PickupState(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"YummyCookieAfterObtainedPatch.Prefix failed: {e.Message}");
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
            CoreMain.LogDebug($"YummyCookieAfterObtainedPatch.Postfix failed: {e.Message}");
        }
    }

    private static void Complete(PickupState state, bool succeeded)
    {
        try
        {
            RunTracker.CompleteYummyCookiePickup(state.Player, succeeded);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"YummyCookieAfterObtainedPatch.Complete failed: {e.Message}");
        }
    }

    public readonly record struct PickupState(Player? Player);
}
