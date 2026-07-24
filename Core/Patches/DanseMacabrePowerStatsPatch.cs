using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace SpireLens.Core.Patches;

/// <summary>
/// Mirrors Danse Macabre's exact owner-card and resolved-energy-cost check.
/// Each qualifying callback is one trigger, and only its immediately issued
/// gain-block command is armed for observed-result attribution.
/// </summary>
[HarmonyPatch(typeof(DanseMacabrePower), nameof(DanseMacabrePower.BeforeCardPlayed))]
internal static class DanseMacabrePowerBeforeCardPlayedStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        DanseMacabrePower __instance,
        CardPlay cardPlay,
        out PendingDanseMacabreBlockAttribution? __state)
    {
        __state = RunTracker.RecordDanseMacabreTriggerAndArmBlockAttribution(
            __instance,
            cardPlay);
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingDanseMacabreBlockAttribution? __state,
        Task __result)
    {
        if (__state == null) return;

        try
        {
            if (__result == null || __result.IsCompleted)
            {
                RunTracker.DisarmDanseMacabreBlockAttribution(__state);
                return;
            }

            __result.ContinueWith(
                _ => RunTracker.DisarmDanseMacabreBlockAttribution(__state),
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"DanseMacabrePowerBeforeCardPlayedStatsPatch.Postfix failed: {e.Message}");
            RunTracker.DisarmDanseMacabreBlockAttribution(__state);
        }
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception? __exception,
        PendingDanseMacabreBlockAttribution? __state)
    {
        if (__exception != null)
            RunTracker.DisarmDanseMacabreBlockAttribution(__state);
        return __exception;
    }
}

/// <summary>
/// Captures the post-modifier amount returned by the exact decimal/ValueProp
/// GainBlock overload used by Danse Macabre.
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
internal static class DanseMacabreCreatureGainBlockStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        Creature creature,
        out PendingDanseMacabreBlockAttribution? __state)
    {
        __state = RunTracker.TryConsumeDanseMacabreBlockAttribution(creature);
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingDanseMacabreBlockAttribution? __state,
        Task<decimal> __result)
    {
        if (__state == null || __result == null) return;
        ObserveBlockResultAsync(__state, __result);
    }

    private static async void ObserveBlockResultAsync(
        PendingDanseMacabreBlockAttribution attribution,
        Task<decimal> blockTask)
    {
        try
        {
            decimal gained = await blockTask.ConfigureAwait(false);
            RunTracker.RecordDanseMacabreBlockGained(attribution, gained);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"DanseMacabreCreatureGainBlockStatsPatch block observation failed: {e.Message}");
        }
    }
}

/// <summary>
/// Counts later zero-trigger turns while the power remains active. The
/// application turn is counted from the observed PowerReceived entry.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
internal static class HookAfterPlayerTurnStartDanseMacabreStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player)
    {
        RunTracker.RecordDanseMacabrePowerTurnStarted(player);
    }
}
