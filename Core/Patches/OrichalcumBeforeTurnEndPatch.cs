using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Arms the Orichalcum block-gain attribution window just before the relic's
/// end-of-turn check runs. If the player has no block, Orichalcum will gain
/// block and <see cref="HookAfterBlockGainedPatch"/> records the amount.
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

            RunTracker.RecordOrichalcumBlockedTrigger();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"OrichalcumBeforeSideTurnEndVeryEarlyPatch.Prefix failed: {e.Message}");
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
/// Bound via a runtime <c>TargetMethods()</c> lookup rather than
/// <c>nameof(Hook.AfterTurnEnd)</c>: a Slay the Spire 2 update can rename or
/// remove the hook, and a compile-time reference would break the whole build
/// (and, under bare PatchAll, take down every other patch). When the method is
/// absent the class simply yields nothing and Harmony skips it.
/// </summary>
[HarmonyPatch]
public static class HookAfterTurnEndOrichalcumCleanupPatch
{
    // Prepare() returning false is Harmony's idiom for skipping a patch class
    // cleanly — no exception, no error log — when the target game method is
    // absent (the STS2 update removed Hook.AfterTurnEnd). This replaces an
    // empty-TargetMethods() approach, which Harmony throws on.
    private static bool Prepare() => AccessTools.Method(typeof(Hook), "AfterTurnEnd") != null;

    private static MethodBase TargetMethod() => AccessTools.Method(typeof(Hook), "AfterTurnEnd");

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
            CoreMain.LogDebug($"HookAfterTurnEndOrichalcumCleanupPatch failed: {e.Message}");
        }
    }
}
