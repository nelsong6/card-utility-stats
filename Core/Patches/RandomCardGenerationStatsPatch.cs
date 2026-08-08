using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace SpireLens.Core.Patches;

/// <summary>
/// One successful-arrival boundary for every supported random-card generator.
/// The game calls this hook only after the card has reached its final combat
/// pile, so cancelled choices and failed pile additions never become output.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardGeneratedForCombat))]
internal static class HookAfterCardGeneratedRandomCardStatsPatch
{
    [HarmonyPostfix]
    public static void Postfix(CardModel card, Player? creator)
    {
        try
        {
            RunTracker.RecordRandomCardGenerated(card, creator);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"HookAfterCardGeneratedRandomCardStatsPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Measure the exact temporary discount granted by random generators that
/// make their output free. The generated-card arrival consumes this value;
/// failed additions leave only combat-local state that is discarded.
/// </summary>
[HarmonyPatch]
internal static class RandomGeneratedCardDiscountStatsPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(
            typeof(CardModel),
            nameof(CardModel.SetToFreeThisTurn));
        yield return AccessTools.Method(
            typeof(CardModel),
            nameof(CardModel.SetToFreeThisCombat));
    }

    [HarmonyPrefix]
    public static void Prefix(
        CardModel __instance,
        out RandomCardDiscountObservation? __state)
    {
        __state = RunTracker.CaptureRandomCardDiscount(__instance);
    }

    [HarmonyPostfix]
    public static void Postfix(RandomCardDiscountObservation? __state)
    {
        RunTracker.RecordRandomCardDiscount(__state);
    }
}

/// <summary>
/// Recurring generators are outcomes of a shared stacked Power, not of one
/// physical source-card instance. Keep a narrow owner callback window open
/// across the awaited generated-card commands so the common arrival hook can
/// route them to the correct Power aggregate.
/// </summary>
[HarmonyPatch]
internal static class RandomCardGenerationPowerCallbackStatsPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(
            typeof(CalamityPower),
            nameof(CalamityPower.AfterCardPlayed));
        yield return AccessTools.Method(
            typeof(CallOfTheVoidPower),
            nameof(CallOfTheVoidPower.BeforeHandDraw));
        yield return AccessTools.Method(
            typeof(CreativeAiPower),
            nameof(CreativeAiPower.BeforeHandDraw));
        yield return AccessTools.Method(
            typeof(HelloWorldPower),
            nameof(HelloWorldPower.BeforeHandDraw));
        yield return AccessTools.Method(
            typeof(SpectrumShiftPower),
            nameof(SpectrumShiftPower.BeforeHandDraw));
    }

    [HarmonyPrefix]
    public static void Prefix(
        PowerModel __instance,
        out RandomCardGenerationPowerWindow? __state)
    {
        __state = null;
        try
        {
            __state = RunTracker.ArmRandomCardGenerationPower(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"RandomCardGenerationPowerCallbackStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(
        RandomCardGenerationPowerWindow? __state,
        ref Task __result)
    {
        try
        {
            if (__state == null) return;
            if (__result == null)
            {
                RunTracker.DisarmRandomCardGenerationPower(__state);
                return;
            }

            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"RandomCardGenerationPowerCallbackStatsPatch failed: {e.Message}");
            RunTracker.DisarmRandomCardGenerationPower(__state);
        }
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception? __exception,
        RandomCardGenerationPowerWindow? __state)
    {
        if (__exception != null)
            RunTracker.DisarmRandomCardGenerationPower(__state);
        return __exception;
    }

    private static async Task ObserveAsync(
        Task inner,
        RandomCardGenerationPowerWindow window)
    {
        try
        {
            await inner.ConfigureAwait(false);
        }
        finally
        {
            RunTracker.DisarmRandomCardGenerationPower(window);
        }
    }
}
