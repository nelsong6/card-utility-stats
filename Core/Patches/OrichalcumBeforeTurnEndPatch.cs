using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Arms the Orichalcum block-gain attribution window just before the relic's
/// end-of-turn check runs. If the player has no block, Orichalcum will gain
/// block and <see cref="HookAfterBlockGainedPatch"/> records the amount.
///
/// When the player does have block the relic stays silent, and the leftover
/// amount read here is the one the game itself compares against. Passing the
/// relic's own <c>DynamicVars.Block.BaseValue</c> alongside it lets the tracker
/// score the shortfall against the live trigger amount instead of a hardcoded 6.
/// </summary>
[HarmonyPatch]
public static class OrichalcumBeforeSideTurnEndVeryEarlyPatch
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.Orichalcum");
        return t == null ? null : AccessTools.Method(t, "BeforeSideTurnEndVeryEarly");
    }

    [HarmonyPrefix]
    public static void Prefix(Orichalcum __instance, CombatSide side, IEnumerable<Creature> participants)
    {
        try
        {
            if (side != CombatSide.Player) return;

            var owner = __instance?.Owner?.Creature;
            if (owner == null) return;
            if (participants == null || !participants.Contains(owner)) return;
            if (owner.Block <= 0) return;

            RunTracker.RecordOrichalcumBlockedTrigger(owner.Block, TriggerBlock(__instance!));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"OrichalcumBeforeSideTurnEndVeryEarlyPatch.Prefix failed: {e.Message}");
        }
    }

    private static decimal TriggerBlock(Orichalcum relic)
    {
        try
        {
            return relic.DynamicVars.Block.BaseValue;
        }
        catch
        {
            // No readable amount means no defensible shortfall; the tracker
            // still counts the blocked trigger itself.
            return 0m;
        }
    }
}

/// <summary>
/// Counts every distinct player turn while Orichalcum is held so turns that end
/// at zero block — and turns where combat ends before the end-of-turn check —
/// stay in the missed-block averages.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
public static class HookAfterPlayerTurnStartOrichalcumPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player)
    {
        try
        {
            RunTracker.RecordOrichalcumTurnStarted(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookAfterPlayerTurnStartOrichalcumPatch failed: {e.Message}");
        }
    }
}

[HarmonyPatch]
public static class OrichalcumBeforeTurnEndPatch
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.Orichalcum");
        if (t == null) return null;
        return AccessTools.Method(t, "BeforeSideTurnEnd") ?? AccessTools.Method(t, "BeforeTurnEnd");
    }

    [HarmonyPrefix]
    public static void Prefix(CombatSide side)
    {
        try
        {
            if (side != CombatSide.Player) return;
            RunTracker.ArmOrichalcumBlockAttribution();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"OrichalcumBeforeTurnEndPatch.Prefix failed: {e.Message}");
        }
    }
}

/// <summary>
/// Clears an armed Orichalcum attribution window after the player's
/// end-of-turn hook sequence. Orichalcum's async hook can gain block after
/// the relic method has returned its task, so cleanup cannot live in the
/// relic postfix itself.
///
/// Bound via runtime lookup rather than <c>nameof(Hook.AfterTurnEnd)</c>: a
/// Slay the Spire 2 update can rename or remove the hook, and a compile-time
/// reference would break the whole build.
/// </summary>
[HarmonyPatch]
public static class HookAfterSideTurnEndOrichalcumCleanupPatch
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
            RunTracker.DisarmOrichalcumBlockAttribution();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookAfterSideTurnEndOrichalcumCleanupPatch failed: {e.Message}");
        }
    }
}
