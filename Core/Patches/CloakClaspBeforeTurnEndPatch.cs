using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;

namespace SpireLens.Core.Patches;

/// <summary>
/// Arms the Cloak Clasp block-gain attribution window just before the relic's
/// end-of-turn hook runs. Cloak Clasp gains 1 Block per card in Hand, so the
/// window stays open until <see cref="HookAfterSideTurnEndCloakClaspCleanupPatch"/>
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
///
/// Bound via runtime lookup rather than <c>nameof(Hook.AfterTurnEnd)</c> so a
/// Slay the Spire 2 update that renames or removes the hook does not break the
/// build.
/// </summary>
[HarmonyPatch]
public static class HookAfterSideTurnEndCloakClaspCleanupPatch
{
    private static MethodBase? CleanupHook()
    {
        var hookType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Hooks.Hook");
        if (hookType == null) return null;

        return AccessTools.Method(hookType, "AfterSideTurnEnd")
            ?? AccessTools.Method(hookType, "AfterTurnEnd");
    }

    private static bool Prepare() => CleanupHook() != null;

    private static MethodBase? TargetMethod()
    {
        return CleanupHook();
    }

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
            CoreMain.LogDebug($"HookAfterSideTurnEndCloakClaspCleanupPatch failed: {e.Message}");
        }
    }
}
