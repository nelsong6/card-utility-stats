using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace SpireLens.Core.Patches;

/// <summary>
/// Outbreak explicitly calls PoisonPower.Trigger while its physical card play
/// is resolving. This window excludes ordinary side-turn Poison triggers.
/// </summary>
[HarmonyPatch(typeof(PoisonPower), nameof(PoisonPower.Trigger))]
internal static class OutbreakPoisonPowerTriggerStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        PoisonPower __instance,
        out PendingOutbreakPoisonTriggerAttribution? __state)
    {
        __state = RunTracker.ArmOutbreakPoisonTriggerAttribution(__instance);
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingOutbreakPoisonTriggerAttribution? __state,
        ref Task __result)
    {
        try
        {
            if (__state == null) return;
            if (__result == null)
            {
                RunTracker.DisarmOutbreakPoisonTriggerAttribution(__state);
                return;
            }

            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"OutbreakPoisonPowerTriggerStatsPatch failed: {e.Message}");
            RunTracker.DisarmOutbreakPoisonTriggerAttribution(__state);
        }
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception? __exception,
        PendingOutbreakPoisonTriggerAttribution? __state)
    {
        if (__exception != null)
            RunTracker.DisarmOutbreakPoisonTriggerAttribution(__state);
        return __exception;
    }

    private static async Task ObserveAsync(
        Task inner,
        PendingOutbreakPoisonTriggerAttribution pending)
    {
        try
        {
            await inner.ConfigureAwait(false);
        }
        finally
        {
            RunTracker.DisarmOutbreakPoisonTriggerAttribution(pending);
        }
    }
}

/// <summary>
/// Captures each actual damage result emitted by an Outbreak-owned explicit
/// Poison trigger, including multiple trigger iterations on one target.
/// </summary>
[HarmonyPatch(
    typeof(CreatureCmd),
    nameof(CreatureCmd.Damage),
    new[]
    {
        typeof(PlayerChoiceContext),
        typeof(Creature),
        typeof(decimal),
        typeof(ValueProp),
        typeof(CardModel),
        typeof(CardPlay),
    })]
internal static class OutbreakPoisonDamageStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        Creature target,
        decimal amount,
        ValueProp props,
        CardModel? cardSource,
        CardPlay? cardPlay,
        out PendingOutbreakDamageAttribution? __state)
    {
        __state = RunTracker.TryConsumeOutbreakPoisonDamageAttribution(
            target,
            amount,
            props,
            cardSource,
            cardPlay);
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingOutbreakDamageAttribution? __state,
        ref Task<IEnumerable<DamageResult>> __result)
    {
        try
        {
            if (__state == null || __result == null) return;
            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"OutbreakPoisonDamageStatsPatch failed: {e.Message}");
        }
    }

    private static async Task<IEnumerable<DamageResult>> ObserveAsync(
        Task<IEnumerable<DamageResult>> inner,
        PendingOutbreakDamageAttribution pending)
    {
        var results = (await inner.ConfigureAwait(false))?.ToArray()
            ?? Array.Empty<DamageResult>();

        try
        {
            RunTracker.RecordOutbreakPoisonTriggerDamage(pending, results);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"OutbreakPoisonDamageStatsPatch.ObserveAsync failed: {e.Message}");
        }

        return results;
    }
}
