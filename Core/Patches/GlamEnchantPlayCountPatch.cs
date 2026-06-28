using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Enchantments;

namespace SpireLens.Core.Patches;

[HarmonyPatch(typeof(Glam), nameof(Glam.EnchantPlayCount))]
public static class GlamEnchantPlayCountPatch
{
    [HarmonyPostfix]
    public static void Postfix(Glam __instance, int originalPlayCount, int __result)
    {
        try
        {
            RunTracker.NoteGlamReplayPlayCount(__instance, originalPlayCount, __result);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"Glam replay play-count attribution failed: {e.Message}");
        }
    }
}
