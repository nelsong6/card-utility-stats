using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Neow's Bones offers two relic rewards and then adds a random curse. The
/// callback only exposes those effects through game mutations, so compare the
/// player's relic inventory and deck before and after the async pickup flow.
/// </summary>
[HarmonyPatch]
public static class NeowsBonesAfterObtainedPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(
            typeof(NeowsBones),
            nameof(NeowsBones.AfterObtained),
            Type.EmptyTypes);
    }

    [HarmonyPrefix]
    public static void Prefix(NeowsBones __instance, out PickupState __state)
    {
        __state = default;

        try
        {
            if (__instance == null) return;
            if (RunTracker.BeginNeowsBonesPickup(
                    __instance,
                    out var player,
                    out var relicsBeforePickup,
                    out var deckBeforePickup))
                __state = new PickupState(player, relicsBeforePickup, deckBeforePickup);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"NeowsBonesAfterObtainedPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(PickupState __state, Task __result)
    {
        try
        {
            if (__state.Player == null
                || __state.RelicsBeforePickup == null
                || __state.DeckBeforePickup == null)
                return;

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
            CoreMain.LogDebug($"NeowsBonesAfterObtainedPatch.Postfix failed: {e.Message}");
        }
    }

    private static void Complete(PickupState state, bool succeeded)
    {
        try
        {
            RunTracker.CompleteNeowsBonesPickup(
                state.Player,
                state.RelicsBeforePickup,
                state.DeckBeforePickup,
                succeeded);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"NeowsBonesAfterObtainedPatch.Complete failed: {e.Message}");
        }
    }

    public readonly record struct PickupState(
        Player? Player,
        IReadOnlyCollection<RelicModel>? RelicsBeforePickup,
        IReadOnlyCollection<CardModel>? DeckBeforePickup);
}
