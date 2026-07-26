using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records additional cards drawn by Pocketwatch's turn-start bonus so the
/// relic tooltip can show the total draw contribution across the run.
///
/// Pocketwatch.ModifyHandDraw returns the modified draw count; when the
/// "played 3 or fewer cards last turn" condition is met the return value
/// exceeds the incoming count by 3. The difference is the relic's bonus.
/// </summary>
[HarmonyPatch]
public static class PocketwatchModifyHandDrawPatch
{
    private static readonly FieldInfo? CardsPlayedLastTurnField =
        AccessTools.Field(typeof(Pocketwatch), "_cardsPlayedLastTurn");

    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.Pocketwatch");
        return t == null ? null : AccessTools.Method(t, "ModifyHandDraw");
    }

    [HarmonyPostfix]
    public static void Postfix(
        Pocketwatch __instance,
        Player player,
        decimal count,
        decimal __result)
    {
        try
        {
            var added = __result - count;
            if (added <= 0m) return;

            var cardsPlayedLastTurn =
                CardsPlayedLastTurnField?.GetValue(__instance) is int value
                    ? value
                    : -1;
            RunTracker.RecordPocketwatchDraw(
                __instance,
                player,
                (int)added,
                cardsPlayedLastTurn);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"PocketwatchModifyHandDrawPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Snapshots Pocketwatch's counter at player-turn end. The tracker also
/// reconciles the final combat-ending turn because that turn may skip this
/// hook entirely.
/// </summary>
[HarmonyPatch]
public static class HookBeforeSideTurnEndPocketwatchPatch
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
    public static void Prefix(
        ICombatState combatState,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        try
        {
            if (side != CombatSide.Player) return;
            RunTracker.RecordPocketwatchTurnEnded(combatState, participants);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookBeforeSideTurnEndPocketwatchPatch failed: {e.Message}");
        }
    }
}
