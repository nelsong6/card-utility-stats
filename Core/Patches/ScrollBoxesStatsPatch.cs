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
/// Scroll Boxes adds the three cards from the chosen bundle directly to the
/// permanent deck. Compare physical deck membership across the completed
/// pickup callback so replacement modifiers and failed additions are observed.
/// </summary>
[HarmonyPatch]
public static class ScrollBoxesAfterObtainedPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(
            typeof(ScrollBoxes),
            nameof(ScrollBoxes.AfterObtained),
            Type.EmptyTypes);
    }

    [HarmonyPrefix]
    public static void Prefix(ScrollBoxes __instance, out PickupState __state)
    {
        __state = default;

        try
        {
            if (__instance == null) return;
            if (RunTracker.BeginScrollBoxesPickup(
                    __instance,
                    out var player,
                    out var deckBeforePickup))
            {
                __state = new PickupState(player, deckBeforePickup);
            }
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"ScrollBoxesAfterObtainedPatch.Prefix failed: {e.Message}");
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
            CoreMain.LogDebug($"ScrollBoxesAfterObtainedPatch.Postfix failed: {e.Message}");
        }
    }

    private static void Complete(PickupState state, bool succeeded)
    {
        try
        {
            RunTracker.CompleteScrollBoxesPickup(
                state.Player,
                state.DeckBeforePickup,
                succeeded);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"ScrollBoxesAfterObtainedPatch.Complete failed: {e.Message}");
        }
    }

    public readonly record struct PickupState(
        Player? Player,
        IReadOnlyCollection<CardModel>? DeckBeforePickup);
}
