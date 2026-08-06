using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace SpireLens.Core.Patches;

/// <summary>
/// Keeps attribution active across Consuming Shadow's sequential end-of-turn
/// EvokeLast calls. The callback window alone does not count an outcome.
/// </summary>
[HarmonyPatch(
    typeof(ConsumingShadowPower),
    nameof(ConsumingShadowPower.AfterSideTurnEnd),
    new[]
    {
        typeof(PlayerChoiceContext),
        typeof(CombatSide),
        typeof(IEnumerable<Creature>),
    })]
internal static class ConsumingShadowPowerAfterSideTurnEndStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        ConsumingShadowPower __instance,
        IEnumerable<Creature> participants,
        out PendingConsumingShadowCallback? __state)
    {
        __state = RunTracker.ArmConsumingShadowCallback(
            __instance,
            participants);
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingConsumingShadowCallback? __state,
        ref Task __result)
    {
        try
        {
            if (__state == null) return;
            if (__result == null)
            {
                RunTracker.DisarmConsumingShadowCallback(__state);
                return;
            }

            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"ConsumingShadowPowerAfterSideTurnEndStatsPatch failed: {e.Message}");
            RunTracker.DisarmConsumingShadowCallback(__state);
        }
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception? __exception,
        PendingConsumingShadowCallback? __state)
    {
        if (__exception != null)
            RunTracker.DisarmConsumingShadowCallback(__state);
        return __exception;
    }

    private static async Task ObserveAsync(
        Task inner,
        PendingConsumingShadowCallback pending)
    {
        try
        {
            await inner.ConfigureAwait(false);
        }
        finally
        {
            RunTracker.DisarmConsumingShadowCallback(pending);
        }
    }
}

/// <summary>
/// Claims only EvokeLast calls made directly by Consuming Shadow and retains
/// the exact last orb until the command completes. Nested calls are excluded
/// while this command is in progress.
/// </summary>
[HarmonyPatch(
    typeof(OrbCmd),
    nameof(OrbCmd.EvokeLast),
    new[]
    {
        typeof(PlayerChoiceContext),
        typeof(Player),
        typeof(bool),
    })]
internal static class ConsumingShadowOrbCmdEvokeLastStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        Player player,
        bool dequeue,
        out PendingConsumingShadowEvoke? __state)
    {
        __state = RunTracker.TryBeginConsumingShadowEvoke(player, dequeue);
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingConsumingShadowEvoke? __state,
        ref Task __result)
    {
        try
        {
            if (__state == null) return;
            if (__result == null)
            {
                RunTracker.CompleteConsumingShadowEvoke(__state);
                return;
            }

            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"ConsumingShadowOrbCmdEvokeLastStatsPatch failed: {e.Message}");
            RunTracker.CompleteConsumingShadowEvoke(__state);
        }
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception? __exception,
        PendingConsumingShadowEvoke? __state)
    {
        if (__exception != null)
            RunTracker.CompleteConsumingShadowEvoke(__state);
        return __exception;
    }

    private static async Task ObserveAsync(
        Task inner,
        PendingConsumingShadowEvoke pending)
    {
        try
        {
            await inner.ConfigureAwait(false);
        }
        finally
        {
            RunTracker.CompleteConsumingShadowEvoke(pending);
        }
    }
}

/// <summary>
/// Hook.AfterOrbEvoked begins only after the selected orb's Evoke task has
/// completed. This is therefore the observed success boundary for the exact
/// direct EvokeLast call claimed above.
/// </summary>
[HarmonyPatch(
    typeof(Hook),
    nameof(Hook.AfterOrbEvoked),
    new[]
    {
        typeof(PlayerChoiceContext),
        typeof(ICombatState),
        typeof(OrbModel),
        typeof(IEnumerable<Creature>),
    })]
internal static class HookAfterOrbEvokedConsumingShadowStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(OrbModel orb)
    {
        try
        {
            RunTracker.RecordConsumingShadowOrbEvoked(orb);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"HookAfterOrbEvokedConsumingShadowStatsPatch failed: {e.Message}");
        }
    }
}
