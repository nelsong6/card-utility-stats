using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;

namespace SpireLens.Core.Patches;

/// <summary>
/// Counts every distinct player turn while Cloak Clasp is held so empty-hand
/// turns and combat-ending turns remain in its average-block denominator.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
public static class HookAfterPlayerTurnStartCloakClaspPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player)
    {
        try
        {
            RunTracker.RecordCloakClaspTurnStarted(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookAfterPlayerTurnStartCloakClaspPatch failed: {e.Message}");
        }
    }
}

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
        var hookType = Sts2CoreAssembly()?.GetType("MegaCrit.Sts2.Core.Hooks.Hook", throwOnError: false);
        if (hookType == null) return null;

        return AccessTools.Method(hookType, "AfterSideTurnEnd")
            ?? AccessTools.Method(hookType, "AfterTurnEnd");
    }

    private static Assembly? Sts2CoreAssembly()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().Name == "sts2") return assembly;
        }

        return null;
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
