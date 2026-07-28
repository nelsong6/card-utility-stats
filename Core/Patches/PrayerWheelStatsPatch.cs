using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;

namespace SpireLens.Core.Patches;

/// <summary>
/// Prayer Wheel appends one dedicated CardReward to a normal monster's reward
/// list. Bind only that appended object so the ordinary reward remains outside
/// Prayer Wheel's offered and rejected totals.
/// </summary>
[HarmonyPatch]
public static class PrayerWheelTryModifyRewardsStatsPatch
{
    private static MethodBase? TargetMethod()
        => AccessTools.Method(
            typeof(PrayerWheel),
            nameof(PrayerWheel.TryModifyRewards),
            new[]
            {
                typeof(Player),
                typeof(List<Reward>),
                typeof(AbstractRoom),
            });

    [HarmonyPrefix]
    public static void Prefix(List<Reward> rewards, out int __state)
    {
        __state = rewards?.Count ?? 0;
    }

    [HarmonyPostfix]
    public static void Postfix(
        PrayerWheel __instance,
        Player player,
        List<Reward> rewards,
        AbstractRoom? room,
        bool __result,
        int __state)
    {
        try
        {
            if (!__result || rewards == null || rewards.Count <= __state) return;
            if (room?.RoomType != RoomType.Monster) return;

            var reward = rewards
                .Skip(Math.Max(0, __state))
                .OfType<CardReward>()
                .FirstOrDefault();
            if (reward == null) return;

            RunTracker.RegisterPrayerWheelReward(__instance, player, reward);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"PrayerWheelTryModifyRewardsStatsPatch failed: {e.Message}");
        }
    }
}
