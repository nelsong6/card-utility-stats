using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace SpireLens.Core.Patches;

/// <summary>
/// Carries the merchant's concrete removal charge into the established
/// CardPileCmd.RemoveFromDeck observation. DoLocalMerchantCardRemoval starts
/// the private async selector/removal flow before returning its Task, so the
/// AsyncLocal frame is captured by that continuation and can be restored here
/// without keeping unrelated main-thread work attributed to the shopkeeper.
/// </summary>
[HarmonyPatch(
    typeof(OneOffSynchronizer),
    nameof(OneOffSynchronizer.DoLocalMerchantCardRemoval),
    new Type[] { typeof(int), typeof(bool) })]
public static class MerchantCardRemovalAttributionPatch
{
    [HarmonyPrefix]
    private static void Prefix(int goldCost, out RemovalContextState __state)
    {
        __state = default;

        try
        {
            __state = new RemovalContextState(
                true,
                RunTracker.PushCardRemovalSource("Shopkeeper", Math.Max(0, goldCost)));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"MerchantCardRemovalAttributionPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(
        Exception? __exception,
        RemovalContextState __state)
    {
        try
        {
            if (__state.Armed)
                RunTracker.RestoreCardRemovalSource(__state.Previous);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"MerchantCardRemovalAttributionPatch.Finalizer failed: {e.Message}");
        }

        return __exception;
    }

    private readonly record struct RemovalContextState(
        bool Armed,
        CardRemovalSourceFrame? Previous);
}
