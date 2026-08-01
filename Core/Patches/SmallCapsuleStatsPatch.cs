using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;

namespace SpireLens.Core.Patches;

/// <summary>
/// Small Capsule creates its RelicReward synchronously before awaiting the
/// custom reward screen. Keep a narrow registration window so the shared
/// OfferCustom hook binds only that exact reward object.
/// </summary>
[HarmonyPatch(typeof(SmallCapsule), nameof(SmallCapsule.AfterObtained))]
public static class SmallCapsuleAfterObtainedStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(SmallCapsule __instance, out bool __state)
    {
        __state = RunTracker.BeginSmallCapsuleRewardRegistration(__instance);
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(Exception? __exception, bool __state)
    {
        if (__state)
            RunTracker.EndSmallCapsuleRewardRegistration();

        return __exception;
    }
}

[HarmonyPatch]
public static class RewardsCmdSmallCapsuleOfferCustomPatch
{
    private static MethodBase? TargetMethod()
        => AccessTools.Method(
            typeof(RewardsCmd),
            nameof(RewardsCmd.OfferCustom),
            [typeof(Player), typeof(List<Reward>)]);

    [HarmonyPrefix]
    public static void Prefix(Player player, List<Reward> rewards)
    {
        try
        {
            RunTracker.RegisterSmallCapsuleCustomRewards(player, rewards);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"RewardsCmdSmallCapsuleOfferCustomPatch failed: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(RelicReward), nameof(RelicReward.Populate))]
public static class SmallCapsuleRelicRewardPopulatePatch
{
    [HarmonyPostfix]
    public static void Postfix(RelicReward __instance)
    {
        try
        {
            RunTracker.RecordSmallCapsuleRelicRewardOffered(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"SmallCapsuleRelicRewardPopulatePatch failed: {e.Message}");
        }
    }
}

[HarmonyPatch]
public static class SmallCapsuleRelicRewardOnSelectPatch
{
    private static MethodBase? TargetMethod()
        => AccessTools.Method(typeof(RelicReward), "OnSelect");

    [HarmonyPostfix]
    public static void Postfix(
        RelicReward __instance,
        ref Task<bool> __result)
    {
        try
        {
            if (__instance == null
                || __result == null
                || !RunTracker.IsSmallCapsuleRelicReward(__instance))
            {
                return;
            }

            __result = ObserveAsync(__instance, __result);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"SmallCapsuleRelicRewardOnSelectPatch failed: {e.Message}");
        }
    }

    private static async Task<bool> ObserveAsync(
        RelicReward reward,
        Task<bool> inner)
    {
        try
        {
            var succeeded = await inner.ConfigureAwait(false);
            if (succeeded)
                RunTracker.RecordSmallCapsuleRelicRewardClaimed(reward);
            return succeeded;
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"SmallCapsuleRelicRewardOnSelectPatch.ObserveAsync failed: {e.Message}");
            throw;
        }
    }
}

[HarmonyPatch(typeof(RelicReward), nameof(RelicReward.OnSkipped))]
public static class SmallCapsuleRelicRewardOnSkippedPatch
{
    [HarmonyPostfix]
    public static void Postfix(RelicReward __instance)
    {
        try
        {
            RunTracker.RecordSmallCapsuleRelicRewardSkipped(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"SmallCapsuleRelicRewardOnSkippedPatch failed: {e.Message}");
        }
    }
}
