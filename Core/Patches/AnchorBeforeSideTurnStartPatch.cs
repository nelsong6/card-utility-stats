using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;

namespace SpireLens.Core.Patches;

/// <summary>
/// Arms Anchor block attribution at the relic's combat-start callback. The
/// actual block gained is observed by Hook.AfterBlockGained.
/// </summary>
[HarmonyPatch]
public static class AnchorBeforeCombatStartPatch
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.Anchor");
        return t == null ? null : AccessTools.Method(t, "BeforeCombatStart");
    }

    [HarmonyPrefix]
    public static void Prefix(RelicModel __instance)
    {
        ArmIfTracked(__instance, nameof(AnchorBeforeCombatStartPatch));
    }

    internal static void ArmIfTracked(RelicModel? relic, string caller)
    {
        try
        {
            if (!RunTracker.IsTrackedRelic(relic)) return;
            RunTracker.ArmAnchorBlockAttribution();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"{caller} failed: {e.Message}");
        }
    }
}

/// <summary>
/// Fake Anchor grants a smaller combat-start block amount but otherwise uses
/// the same observed Anchor stat bucket.
/// </summary>
[HarmonyPatch]
public static class FakeAnchorBeforeCombatStartPatch
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.FakeAnchor");
        return t == null ? null : AccessTools.Method(t, "BeforeCombatStart");
    }

    [HarmonyPrefix]
    public static void Prefix(RelicModel __instance)
    {
        AnchorBeforeCombatStartPatch.ArmIfTracked(__instance, nameof(FakeAnchorBeforeCombatStartPatch));
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
public static class HookAfterPlayerTurnStartAnchorCleanupPatch
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        try
        {
            RunTracker.DisarmAnchorBlockAttribution();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookAfterPlayerTurnStartAnchorCleanupPatch failed: {e.Message}");
        }
    }
}
