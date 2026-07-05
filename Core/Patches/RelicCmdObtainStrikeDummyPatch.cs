using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Stamps Strike Dummy's deck-composition stats when the relic is obtained.
/// The command path catches reward, shop, event, and direct relic grants.
/// </summary>
[HarmonyPatch]
public static class RelicCmdObtainStrikeDummyPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(
            typeof(RelicCmd),
            nameof(RelicCmd.Obtain),
            new[] { typeof(RelicModel), typeof(Player), typeof(int) });
    }

    [HarmonyPostfix]
    public static void Postfix(RelicModel relic, Player player, Task<RelicModel> __result)
    {
        try
        {
            if (!RunTracker.IsStrikeDummyStatsRelic(relic)) return;

            if (__result == null)
            {
                RunTracker.RecordStrikeDummyObtained(relic, player);
                return;
            }

            if (__result.IsCompleted)
            {
                if (!__result.IsCanceled && !__result.IsFaulted)
                    RunTracker.RecordStrikeDummyObtained(__result.Result ?? relic, player);
                return;
            }

            __result.ContinueWith(
                task =>
                {
                    if (!task.IsCanceled && !task.IsFaulted)
                        RunTracker.RecordStrikeDummyObtained(task.Result ?? relic, player);
                },
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RelicCmdObtainStrikeDummyPatch failed: {e.Message}");
        }
    }
}
