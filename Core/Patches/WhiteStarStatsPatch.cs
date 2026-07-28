using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;

namespace SpireLens.Core.Patches;

/// <summary>
/// White Star appends one dedicated boss-tier CardReward to an Elite's reward
/// list. Bind that exact appended object so later shared CardReward hooks never
/// confuse the normal Elite card reward with White Star's extra reward.
/// </summary>
[HarmonyPatch]
public static class WhiteStarTryModifyRewardsStatsPatch
{
    private static MethodBase? TargetMethod()
        => AccessTools.Method(
            typeof(WhiteStar),
            nameof(WhiteStar.TryModifyRewards),
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
        WhiteStar __instance,
        Player player,
        List<Reward> rewards,
        AbstractRoom? room,
        bool __result,
        int __state)
    {
        try
        {
            if (!__result || rewards == null || rewards.Count <= __state) return;
            if (room?.RoomType != RoomType.Elite) return;

            var reward = rewards
                .Skip(Math.Max(0, __state))
                .OfType<CardReward>()
                .FirstOrDefault();
            if (reward == null) return;

            RunTracker.RegisterWhiteStarReward(__instance, player, reward);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"WhiteStarTryModifyRewardsStatsPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Counts the final cards produced for each marked relic-owned option set.
/// Rerolls clear and repopulate the same reward, so each genuinely generated
/// set is counted once while no-op Populate calls are ignored.
/// </summary>
[HarmonyPatch(typeof(CardReward), nameof(CardReward.Populate))]
public static class CardRewardAttributedRelicPopulateStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(CardReward __instance, out int __state)
    {
        __state = 0;
        if (__instance?.IsPopulated != false) return;
        if (RunTracker.IsTrackedWhiteStarReward(__instance))
            __state |= 1;
        if (RunTracker.IsTrackedPrayerWheelReward(__instance))
            __state |= 2;
    }

    [HarmonyPostfix]
    public static void Postfix(CardReward __instance, int __state)
    {
        try
        {
            if (__state == 0 || __instance?.IsPopulated != true) return;
            if ((__state & 1) != 0)
                RunTracker.RecordWhiteStarOffers(__instance);
            if ((__state & 2) != 0)
                RunTracker.RecordPrayerWheelOffers(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"CardRewardAttributedRelicPopulateStatsPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Snapshots each marked reward before selection. CardReward removes a
/// successfully obtained card from its internal list, so terminal resolution
/// with no relevant decrease means the player declined that relic reward.
/// </summary>
[HarmonyPatch]
public static class CardRewardAttributedRelicOnSelectStatsPatch
{
    private static MethodBase? TargetMethod()
        => AccessTools.Method(typeof(CardReward), "OnSelect");

    [HarmonyPrefix]
    public static void Prefix(CardReward __instance)
    {
        try
        {
            RunTracker.NoteWhiteStarRewardOpened(__instance);
            RunTracker.NotePrayerWheelRewardOpened(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"CardRewardAttributedRelicOnSelectStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(CardReward __instance, Task<bool> __result)
    {
        if (__instance == null || __result == null) return;
        ObserveResolutionAsync(__instance, __result);
    }

    private static async void ObserveResolutionAsync(
        CardReward reward,
        Task<bool> selectionTask)
    {
        try
        {
            var completed = await selectionTask;
            RunTracker.RecordWhiteStarRewardResolved(reward, completed);
            RunTracker.RecordPrayerWheelRewardResolved(reward, completed);
        }
        catch (Exception e)
        {
            RunTracker.RecordWhiteStarRewardResolved(reward, completed: false);
            RunTracker.RecordPrayerWheelRewardResolved(reward, completed: false);
            CoreMain.LogDebug(
                $"CardRewardAttributedRelicOnSelectStatsPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// The outer rewards-page skip is a terminal decline even when the marked
/// relic reward was never opened.
/// </summary>
[HarmonyPatch(typeof(CardReward), nameof(CardReward.OnSkipped))]
public static class CardRewardAttributedRelicOnSkippedStatsPatch
{
    [HarmonyPostfix]
    public static void Postfix(CardReward __instance)
    {
        try
        {
            RunTracker.RecordWhiteStarRewardSkipped(__instance);
            RunTracker.RecordPrayerWheelRewardSkipped(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"CardRewardAttributedRelicOnSkippedStatsPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Driftwood replaces the visible options inside an already-running OnSelect
/// task. Refresh the decline baseline after that replacement.
/// </summary>
[HarmonyPatch(typeof(CardReward), nameof(CardReward.Reroll))]
public static class CardRewardAttributedRelicRerollStatsPatch
{
    [HarmonyPostfix]
    public static void Postfix(CardReward __instance)
    {
        try
        {
            RunTracker.RefreshWhiteStarRewardAfterReroll(__instance);
            RunTracker.RefreshPrayerWheelRewardAfterReroll(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"CardRewardAttributedRelicRerollStatsPatch failed: {e.Message}");
        }
    }
}
