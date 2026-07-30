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
/// Observes Amethyst Aubergine at its owner-specific reward modifier. A true
/// result means the relic triggered, and the concrete GoldReward appended by
/// that call supplies the observed extra-gold amount without copying its
/// current dynamic-var value.
/// </summary>
[HarmonyPatch]
public static class AmethystAubergineStatsPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(
            typeof(AmethystAubergine),
            nameof(AmethystAubergine.TryModifyRewards),
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
        AmethystAubergine __instance,
        List<Reward> rewards,
        bool __result,
        int __state)
    {
        try
        {
            if (!__result || __instance == null || rewards == null || __state < 0)
                return;

            var owner = __instance.Owner;
            var addedGoldReward = rewards
                .Skip(__state)
                .OfType<GoldReward>()
                .FirstOrDefault(reward => ReferenceEquals(reward.Player, owner));
            if (owner == null || addedGoldReward == null) return;

            RunTracker.RecordAmethystAubergineTrigger(
                __instance,
                owner,
                addedGoldReward.Amount);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"AmethystAubergineStatsPatch.Postfix failed: {e.Message}");
        }
    }
}
