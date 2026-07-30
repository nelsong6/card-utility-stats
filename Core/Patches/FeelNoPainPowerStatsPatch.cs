using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace SpireLens.Core.Patches;

/// <summary>
/// Arms observed block attribution only for the exact owner-card exhaust
/// callback that makes Feel No Pain issue its gain-block command.
/// </summary>
[HarmonyPatch(
    typeof(FeelNoPainPower),
    nameof(FeelNoPainPower.AfterCardExhausted),
    new[]
    {
        typeof(PlayerChoiceContext),
        typeof(CardModel),
        typeof(bool),
    })]
internal static class FeelNoPainPowerAfterCardExhaustedStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        FeelNoPainPower __instance,
        CardModel card,
        out PendingFeelNoPainBlockAttribution? __state)
    {
        __state = RunTracker.ArmFeelNoPainBlockAttribution(__instance, card);
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingFeelNoPainBlockAttribution? __state,
        ref Task __result)
    {
        try
        {
            if (__state == null) return;
            if (__result == null)
            {
                RunTracker.DisarmFeelNoPainBlockAttribution(__state);
                return;
            }

            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"FeelNoPainPowerAfterCardExhaustedStatsPatch failed: {e.Message}");
            RunTracker.DisarmFeelNoPainBlockAttribution(__state);
        }
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception? __exception,
        PendingFeelNoPainBlockAttribution? __state)
    {
        if (__exception != null)
            RunTracker.DisarmFeelNoPainBlockAttribution(__state);
        return __exception;
    }

    private static async Task ObserveAsync(
        Task inner,
        PendingFeelNoPainBlockAttribution attribution)
    {
        try
        {
            await inner.ConfigureAwait(false);
        }
        finally
        {
            RunTracker.DisarmFeelNoPainBlockAttribution(attribution);
        }
    }
}

/// <summary>
/// Replaces the returned task with an observer so the exact post-modifier
/// block amount is recorded before Feel No Pain's awaited callback completes.
/// </summary>
[HarmonyPatch(
    typeof(CreatureCmd),
    nameof(CreatureCmd.GainBlock),
    new[]
    {
        typeof(Creature),
        typeof(decimal),
        typeof(ValueProp),
        typeof(CardPlay),
        typeof(bool),
    })]
internal static class FeelNoPainCreatureGainBlockStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        Creature creature,
        out PendingFeelNoPainBlockAttribution? __state)
    {
        __state = RunTracker.TryConsumeFeelNoPainBlockAttribution(creature);
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingFeelNoPainBlockAttribution? __state,
        ref Task<decimal> __result)
    {
        try
        {
            if (__state == null || __result == null) return;
            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"FeelNoPainCreatureGainBlockStatsPatch failed: {e.Message}");
        }
    }

    private static async Task<decimal> ObserveAsync(
        Task<decimal> inner,
        PendingFeelNoPainBlockAttribution attribution)
    {
        var gained = await inner.ConfigureAwait(false);
        RunTracker.RecordFeelNoPainBlockGained(attribution, gained);
        return gained;
    }
}

/// <summary>
/// Counts zero-block turns that begin while Feel No Pain remains active.
/// The application turn is counted from the observed power application.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
internal static class HookAfterPlayerTurnStartFeelNoPainStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player)
    {
        RunTracker.RecordFeelNoPainPowerTurnStarted(player);
    }
}
