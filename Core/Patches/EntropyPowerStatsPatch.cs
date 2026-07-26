using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Random;

namespace SpireLens.Core.Patches;

/// <summary>
/// Count Entropy's application combat only after its own play callback has
/// completed and the shared power is actually present.
/// </summary>
[HarmonyPatch(typeof(Entropy), "OnPlay")]
internal static class EntropyOnPlayStatsPatch
{
    [HarmonyPostfix]
    public static void Postfix(Entropy __instance, ref Task __result)
    {
        try
        {
            var player = __instance?.Owner;
            if (player == null || __result == null) return;
            __result = ObserveAsync(__result, player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"EntropyOnPlayStatsPatch failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(Task inner, Player player)
    {
        await inner.ConfigureAwait(false);
        RunTracker.RecordEntropyPowerApplied(player);
    }
}

/// <summary>
/// Entropy owns the exact selection-and-transform sequence. Keep attribution
/// armed only while that asynchronous callback is resolving.
/// </summary>
[HarmonyPatch(typeof(EntropyPower), nameof(EntropyPower.AfterPlayerTurnStart))]
internal static class EntropyPowerAfterPlayerTurnStartStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        EntropyPower __instance,
        Player player,
        out PendingEntropyTransformWindow? __state)
    {
        __state = RunTracker.ArmEntropyTransformAttribution(__instance, player);
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingEntropyTransformWindow? __state,
        ref Task __result)
    {
        try
        {
            if (__state == null) return;
            if (__result == null)
            {
                RunTracker.DisarmEntropyTransformAttribution(__state);
                return;
            }

            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"EntropyPowerAfterPlayerTurnStartStatsPatch failed: {e.Message}");
            RunTracker.DisarmEntropyTransformAttribution(__state);
        }
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception? __exception,
        PendingEntropyTransformWindow? __state)
    {
        if (__exception != null)
            RunTracker.DisarmEntropyTransformAttribution(__state);
        return __exception;
    }

    private static async Task ObserveAsync(
        Task inner,
        PendingEntropyTransformWindow window)
    {
        try
        {
            await inner.ConfigureAwait(false);
        }
        finally
        {
            RunTracker.DisarmEntropyTransformAttribution(window);
        }
    }
}

/// <summary>
/// Observe Entropy's final replacement cards at the already-established
/// CardCmd.Transform boundary. Rarity comes from each successful result; the
/// source batch remembers whether each original was Bound before removal.
/// </summary>
[HarmonyPatch]
internal static class EntropyCardTransformStatsPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(
            typeof(CardCmd),
            nameof(CardCmd.Transform),
            new[]
            {
                typeof(IEnumerable<CardTransformation>),
                typeof(Rng),
                typeof(CardPreviewStyle),
            });
    }

    [HarmonyPrefix]
    public static void Prefix(
        ref IEnumerable<CardTransformation> transformations,
        out PendingEntropyTransformBatch? __state)
    {
        __state = null;
        try
        {
            RunTracker.TryCaptureEntropyTransformSources(
                ref transformations,
                out __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"EntropyCardTransformStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(
        ref Task<IEnumerable<CardPileAddResult>> __result,
        PendingEntropyTransformBatch? __state)
    {
        try
        {
            if (__state == null || __result == null) return;
            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"EntropyCardTransformStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task<IEnumerable<CardPileAddResult>> ObserveAsync(
        Task<IEnumerable<CardPileAddResult>> inner,
        PendingEntropyTransformBatch batch)
    {
        var results = await inner.ConfigureAwait(false);
        try
        {
            RunTracker.RecordEntropyTransformResults(batch, results);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"EntropyCardTransformStatsPatch.ObserveAsync failed: {e.Message}");
        }

        return results;
    }
}
