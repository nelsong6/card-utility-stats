using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;

namespace SpireLens.Core.Patches;

/// <summary>
/// Fishing Rod chooses and upgrades its card synchronously inside
/// AfterCombatEnd. Keep an attribution window around that exact callback so
/// CardModel.UpgradeInternal identifies the card the relic actually changed.
/// </summary>
[HarmonyPatch]
public static class FishingRodAfterCombatEndStatsPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(
            typeof(FishingRod),
            nameof(FishingRod.AfterCombatEnd),
            new[] { typeof(CombatRoom) });
    }

    [HarmonyPrefix]
    public static void Prefix(
        FishingRod __instance,
        CombatRoom room,
        out UpgradeState __state)
    {
        __state = default;

        try
        {
            if (__instance == null) return;
            if (RunTracker.BeginFishingRodUpgrade(__instance, room, out var player))
                __state = new UpgradeState(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"FishingRodAfterCombatEndStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(UpgradeState __state, Task __result)
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
            CoreMain.LogDebug($"FishingRodAfterCombatEndStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static void Complete(UpgradeState state, bool succeeded)
    {
        try
        {
            RunTracker.CompleteFishingRodUpgrade(state.Player, succeeded);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"FishingRodAfterCombatEndStatsPatch.Complete failed: {e.Message}");
        }
    }

    public readonly record struct UpgradeState(Player? Player);
}
