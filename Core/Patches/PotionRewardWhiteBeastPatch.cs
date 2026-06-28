using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Rewards;

namespace SpireLens.Core.Patches;

/// <summary>
/// Bridges a White Beast force decision to the concrete potion reward object,
/// then records only when that marked reward is actually claimed.
/// </summary>
[HarmonyPatch]
public static class PotionRewardWhiteBeastConstructorPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Rewards.PotionReward");
        if (t == null) yield break;

        foreach (var ctor in AccessTools.GetDeclaredConstructors(t))
            yield return ctor;
    }

    [HarmonyPostfix]
    public static void Postfix(PotionReward __instance)
    {
        try
        {
            RunTracker.NotePotionRewardCreated(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"PotionRewardWhiteBeastConstructorPatch.Postfix failed: {e.Message}");
        }
    }
}

[HarmonyPatch]
public static class PotionRewardWhiteBeastOnSelectPatch
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Rewards.PotionReward");
        return t == null ? null : AccessTools.Method(t, "OnSelect");
    }

    [HarmonyPostfix]
    public static void Postfix(PotionReward __instance, Task<bool> __result)
    {
        try
        {
            if (__result == null)
                return;

            if (__result.IsCompleted)
            {
                if (__result.Status == TaskStatus.RanToCompletion && __result.Result)
                    RunTracker.RecordWhiteBeastPotionRewardClaimed(__instance);
                return;
            }

            __result.ContinueWith(
                task =>
                {
                    if (task.Status == TaskStatus.RanToCompletion && task.Result)
                        RunTracker.RecordWhiteBeastPotionRewardClaimed(__instance);
                },
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"PotionRewardWhiteBeastOnSelectPatch.Postfix failed: {e.Message}");
        }
    }
}
