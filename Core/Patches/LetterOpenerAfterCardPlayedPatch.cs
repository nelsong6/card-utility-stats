using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Letter Opener activates after every third Skill played in a turn. The game
/// does not source the emitted damage entries to the relic, so we record the
/// activation and attempted damage at the owner callback.
/// </summary>
[HarmonyPatch(typeof(LetterOpener), nameof(LetterOpener.AfterCardPlayed))]
public static class LetterOpenerAfterCardPlayedPatch
{
    [HarmonyPrefix]
    public static void Prefix(LetterOpener __instance, CardPlay cardPlay)
    {
        try
        {
            if (__instance == null || cardPlay?.Card == null) return;
            if (!RunTracker.IsTrackedRelic(__instance)) return;
            if (cardPlay.Card.Owner != __instance.Owner) return;
            int threshold = Math.Max(1, __instance.DynamicVars.Cards.IntValue);
            RunTracker.RecordLetterOpenerBeforeCardPlayed(
                cardPlay,
                __instance.SkillsPlayedThisTurn + 1,
                threshold);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"LetterOpenerAfterCardPlayedPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Snapshots Letter Opener's charge at the end of each player turn while held.
/// Bound by runtime lookup so a game hook rename does not break build.
/// </summary>
[HarmonyPatch]
public static class HookBeforeSideTurnEndLetterOpenerPatch
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
            RunTracker.RecordLetterOpenerTurnEnded(participants);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookBeforeSideTurnEndLetterOpenerPatch failed: {e.Message}");
        }
    }
}
