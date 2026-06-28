using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Hooks;

namespace SpireLens.Core.Patches;

/// <summary>
/// Arms the Cloak Clasp block-gain attribution window just before the relic's
/// end-of-turn hook runs. Cloak Clasp gains 1 Block per card in Hand, so the
/// window stays open until <see cref="HookAfterTurnEndCloakClaspCleanupPatch"/>
/// clears it — allowing all per-card block gains to be accumulated by
/// <see cref="HookAfterBlockGainedPatch"/>.
/// </summary>
[HarmonyPatch]
public static class CloakClaspBeforeTurnEndPatch
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.CloakClasp");
        if (t == null) return null;
        return AccessTools.Method(t, "BeforeSideTurnEnd") ?? AccessTools.Method(t, "BeforeTurnEnd");
    }

    [HarmonyPrefix]
    public static void Prefix(CombatSide side)
    {
        try
        {
            if (side != CombatSide.Player) return;
            RunTracker.ArmCloakClaspBlockAttribution();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CloakClaspBeforeTurnEndPatch.Prefix failed: {e.Message}");
        }
    }
}

/// <summary>
/// Clears the Cloak Clasp attribution window after the player's end-of-turn
/// hook sequence. Mirrors the later-boundary cleanup pattern used by Orichalcum.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterTurnEnd))]
public static class HookAfterTurnEndCloakClaspCleanupPatch
{
    [HarmonyPrefix]
    public static void Prefix(CombatSide side)
    {
        try
        {
            if (side != CombatSide.Player) return;
            RunTracker.DisarmCloakClaspBlockAttribution();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookAfterTurnEndCloakClaspCleanupPatch failed: {e.Message}");
        }
    }
}
