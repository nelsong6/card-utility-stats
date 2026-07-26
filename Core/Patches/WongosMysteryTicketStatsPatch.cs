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
/// Captures the exact three RelicReward objects appended by Wongo's Mystery
/// Ticket when its five-combat countdown completes.
/// </summary>
[HarmonyPatch]
public static class WongosMysteryTicketTryModifyRewardsPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(
            typeof(WongosMysteryTicket),
            nameof(WongosMysteryTicket.TryModifyRewards),
            new[]
            {
                typeof(Player),
                typeof(List<Reward>),
                typeof(AbstractRoom),
            });
    }

    [HarmonyPrefix]
    public static void Prefix(List<Reward> rewards, out int __state)
    {
        __state = rewards?.Count ?? -1;
    }

    [HarmonyPostfix]
    public static void Postfix(
        WongosMysteryTicket __instance,
        List<Reward> rewards,
        bool __result,
        int __state)
    {
        try
        {
            if (!__result
                || __instance == null
                || rewards == null
                || __state < 0)
            {
                return;
            }

            var addedRelicRewards = rewards
                .Skip(__state)
                .OfType<RelicReward>()
                .ToList();
            RunTracker.RegisterWongosMysteryTicketRewards(
                __instance,
                addedRelicRewards);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"WongosMysteryTicketTryModifyRewardsPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Observes only marked ticket rewards and records the relic returned by their
/// completed RelicCmd.Obtain call.
/// </summary>
[HarmonyPatch]
public static class WongosMysteryTicketRelicRewardOnSelectPatch
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
                || !RunTracker.IsWongosMysteryTicketReward(__instance))
            {
                return;
            }

            __result = ObserveAsync(__instance, __result);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"WongosMysteryTicketRelicRewardOnSelectPatch failed: {e.Message}");
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
                RunTracker.RecordWongosMysteryTicketRelicReceived(reward);
            return succeeded;
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"WongosMysteryTicketRelicRewardOnSelectPatch.ObserveAsync failed: {e.Message}");
            throw;
        }
    }
}
