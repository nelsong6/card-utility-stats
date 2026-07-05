using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Tracks Regal Pillow's actual bonus healing from rest-site heals.
/// </summary>
[HarmonyPatch]
public static class RegalPillowModifyRestSiteHealAmountPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(
            typeof(RegalPillow),
            nameof(RegalPillow.ModifyRestSiteHealAmount),
            new[] { typeof(Creature), typeof(decimal) });
    }

    [HarmonyPostfix]
    public static void Postfix(RegalPillow __instance, Creature creature, decimal amount, decimal __result)
    {
        try
        {
            if (__instance?.Owner?.Creature == null || creature == null) return;
            if (!RunTracker.IsTrackedRelic(__instance)) return;
            if (!ReferenceEquals(creature, __instance.Owner.Creature)) return;

            RunTracker.RememberRegalPillowRestHeal(__instance.Owner, amount, __result);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RegalPillowModifyRestSiteHealAmountPatch failed: {e.Message}");
        }
    }
}

[HarmonyPatch]
public static class RegalPillowAfterRestSiteHealPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(
            typeof(RegalPillow),
            nameof(RegalPillow.AfterRestSiteHeal),
            new[] { typeof(Player), typeof(bool) });
    }

    [HarmonyPrefix]
    public static void Prefix(RegalPillow __instance, Player player, bool isMimicked)
    {
        try
        {
            if (__instance?.Owner == null || player == null) return;
            if (!RunTracker.IsTrackedRelic(__instance)) return;
            if (!ReferenceEquals(player, __instance.Owner)) return;

            RunTracker.CommitRegalPillowRestHeal(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RegalPillowAfterRestSiteHealPatch.Prefix failed: {e.Message}");
        }
    }
}
