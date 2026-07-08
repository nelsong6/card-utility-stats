using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Tuning Fork when its owner-owned Skill counter is about to cross
/// the relic threshold. The block amount itself is observed later by
/// Hook.AfterBlockGained, after the game applies the block command.
/// </summary>
[HarmonyPatch(typeof(TuningFork), nameof(TuningFork.AfterCardPlayed))]
public static class TuningForkAfterCardPlayedPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        TuningFork __instance,
        PlayerChoiceContext choiceContext,
        CardPlay cardPlay,
        out bool __state)
    {
        __state = false;

        try
        {
            if (!RunTracker.RecordTuningForkSkillPlayedAndShouldArmBlockAttribution(__instance, cardPlay)) return;
            __state = true;
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"TuningForkAfterCardPlayedPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(bool __state, Task __result)
    {
        try
        {
            if (!__state) return;
            if (__result == null)
            {
                RunTracker.DisarmTuningForkBlockAttribution();
                return;
            }

            __result.ContinueWith(
                _ => RunTracker.DisarmTuningForkBlockAttribution(),
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"TuningForkAfterCardPlayedPatch.Postfix failed: {e.Message}");
        }
    }
}

/// <summary>
/// Snapshots Tuning Fork's persistent Skill counter at the end of each player
/// turn while held. Bound by runtime lookup so a game hook rename does not
/// break build.
/// </summary>
[HarmonyPatch]
public static class HookBeforeSideTurnEndTuningForkPatch
{
    private static MethodBase? TargetMethod()
    {
        var hookType = Sts2CoreAssembly()?.GetType("MegaCrit.Sts2.Core.Hooks.Hook", throwOnError: false);
        if (hookType == null) return null;

        return AccessTools.Method(hookType, "BeforeSideTurnEnd")
            ?? AccessTools.Method(hookType, "BeforeTurnEnd");
    }

    private static Assembly? Sts2CoreAssembly()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().Name == "sts2") return assembly;
        }

        return null;
    }

    private static bool Prepare() => TargetMethod() != null;

    [HarmonyPrefix]
    public static void Prefix(CombatSide side, IEnumerable<Creature> participants)
    {
        try
        {
            if (side != CombatSide.Player) return;
            RunTracker.RecordTuningForkTurnEnded(participants);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookBeforeSideTurnEndTuningForkPatch failed: {e.Message}");
        }
    }
}
