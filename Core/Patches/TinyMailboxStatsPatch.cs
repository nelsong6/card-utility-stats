using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;

namespace SpireLens.Core.Patches;

/// <summary>
/// Binds Tiny Mailbox to the exact PotionReward objects it appends after a
/// rest-site heal. The rewards are unpopulated here; their concrete potion
/// rarity is observed later when the player selects or skips them.
/// </summary>
[HarmonyPatch(
    typeof(TinyMailbox),
    nameof(TinyMailbox.TryModifyRestSiteHealRewards))]
public static class TinyMailboxRestHealRewardsStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(List<Reward> rewards, out int __state)
    {
        __state = rewards?.Count ?? 0;
    }

    [HarmonyPostfix]
    public static void Postfix(
        TinyMailbox __instance,
        Player player,
        List<Reward> rewards,
        bool __result,
        int __state)
    {
        try
        {
            if (!__result || rewards == null) return;

            var addedPotionRewards = rewards
                .Skip(Math.Clamp(__state, 0, rewards.Count))
                .OfType<PotionReward>()
                .ToList();
            RunTracker.RegisterTinyMailboxPotionRewards(
                __instance,
                player,
                addedPotionRewards);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"TinyMailboxRestHealRewardsStatsPatch failed: {e.Message}");
        }
    }
}
