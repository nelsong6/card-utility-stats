using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Arcane Scroll adds a random rare card directly to the deck on pickup. The
/// relic callback does not expose the add result, so compare the deck before
/// and after the async pickup completes.
/// </summary>
[HarmonyPatch]
public static class ArcaneScrollAfterObtainedPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(
            typeof(ArcaneScroll),
            nameof(ArcaneScroll.AfterObtained),
            Type.EmptyTypes);
    }

    [HarmonyPrefix]
    public static void Prefix(ArcaneScroll __instance, out PickupState __state)
    {
        __state = default;

        try
        {
            if (__instance == null) return;
            if (RunTracker.BeginArcaneScrollPickup(__instance, out var player, out var deckBeforePickup))
                __state = new PickupState(player, deckBeforePickup);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"ArcaneScrollAfterObtainedPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(PickupState __state, Task __result)
    {
        try
        {
            if (__state.Player == null || __state.DeckBeforePickup == null) return;

            if (__result == null)
            {
                Complete(__state, succeeded: true);
                return;
            }

            if (__result.IsCompleted)
            {
                Complete(__state, succeeded: !__result.IsCanceled && !__result.IsFaulted);
                return;
            }

            __result.ContinueWith(
                task => Complete(__state, succeeded: !task.IsCanceled && !task.IsFaulted),
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"ArcaneScrollAfterObtainedPatch.Postfix failed: {e.Message}");
        }
    }

    private static void Complete(PickupState state, bool succeeded)
    {
        try
        {
            RunTracker.CompleteArcaneScrollPickup(state.Player, state.DeckBeforePickup, succeeded);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"ArcaneScrollAfterObtainedPatch.Complete failed: {e.Message}");
        }
    }

    public readonly record struct PickupState(
        Player? Player,
        IReadOnlyCollection<CardModel>? DeckBeforePickup);
}
