using System;
using System.Reflection;
using HarmonyLib;

namespace SpireLens.Core.Patches;

/// <summary>
/// Arms the The Abacus block-gain attribution window just before the relic's
/// AfterShuffle hook runs. When the player shuffles their draw pile, The
/// Abacus gains 6 Block, and <see cref="HookAfterBlockGainedPatch"/> records
/// the amount.
/// </summary>
[HarmonyPatch]
public static class TheAbacusAfterShufflePatch
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.TheAbacus");
        if (t == null) return null;
        return AccessTools.Method(t, "AfterShuffle");
    }

    [HarmonyPrefix]
    public static void Prefix()
    {
        try
        {
            RunTracker.ArmTheAbacusBlockAttribution();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"TheAbacusAfterShufflePatch.Prefix failed: {e.Message}");
        }
    }
}
