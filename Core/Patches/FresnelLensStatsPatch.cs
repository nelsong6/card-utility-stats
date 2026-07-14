using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Rewards;

namespace SpireLens.Core.Patches;

/// <summary>
/// Drowning Beacon applies its max-HP cost before it obtains Fresnel Lens.
/// Wrap the full event choice so the relic aggregate gets the observed
/// before/after values instead of the event's requested amount.
/// </summary>
[HarmonyPatch]
public static class DrowningBeaconFresnelLensStatsPatch
{
    private static MethodBase? TargetMethod()
        => AccessTools.Method(typeof(DrowningBeacon), "ClimbOption", Type.EmptyTypes);

    [HarmonyPrefix]
    public static void Prefix(DrowningBeacon __instance, out EventState __state)
    {
        __state = default;

        try
        {
            var player = __instance?.Owner;
            var creature = player?.Creature;
            if (creature == null || creature.IsDead) return;

            __state = new EventState(player, creature, creature.MaxHp);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"DrowningBeaconFresnelLensStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(EventState __state, Task __result)
    {
        try
        {
            if (__state.Player == null || __state.Creature == null) return;

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
            CoreMain.LogDebug($"DrowningBeaconFresnelLensStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static void Complete(EventState state, bool succeeded)
    {
        try
        {
            if (!succeeded || state.Player == null || state.Creature == null) return;
            RunTracker.RecordFresnelLensEventMaxHpChanged(
                state.Player,
                state.OriginalMaxHp,
                state.Creature.MaxHp);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"DrowningBeaconFresnelLensStatsPatch.Complete failed: {e.Message}");
        }
    }

    public readonly record struct EventState(Player? Player, Creature? Creature, decimal OriginalMaxHp);
}

/// <summary>
/// Counts the final Nimble option distribution on each card-reward selection
/// opened while Fresnel Lens is held, then observes how many of those options
/// were successfully removed from the reward because they entered the deck.
/// </summary>
[HarmonyPatch]
public static class CardRewardFresnelLensOnSelectPatch
{
    private static MethodBase? TargetMethod()
        => AccessTools.Method(typeof(CardReward), "OnSelect");

    [HarmonyPrefix]
    public static void Prefix(CardReward __instance)
    {
        try
        {
            RunTracker.NoteFresnelLensRewardOpened(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CardRewardFresnelLensOnSelectPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(CardReward __instance, Task<bool> __result)
    {
        if (__instance == null || __result == null) return;
        ObserveRewardResolutionAsync(__instance, __result);
    }

    private static async void ObserveRewardResolutionAsync(CardReward reward, Task<bool> selectionTask)
    {
        try
        {
            var completed = await selectionTask;
            if (completed)
                RunTracker.RecordFresnelLensRewardResolved(reward);
        }
        catch (Exception e)
        {
            RunTracker.CancelFresnelLensReward(reward);
            CoreMain.LogDebug($"CardRewardFresnelLensOnSelectPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// The card-screen Skip only returns to the outer rewards screen; it does not
/// consume the CardReward. Finalize a still-pending Fresnel opportunity only
/// when that outer reward is actually abandoned.
/// </summary>
[HarmonyPatch(typeof(CardReward), nameof(CardReward.OnSkipped))]
public static class CardRewardFresnelLensOnSkippedPatch
{
    [HarmonyPostfix]
    public static void Postfix(CardReward __instance)
    {
        try
        {
            RunTracker.RecordFresnelLensRewardResolved(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CardRewardFresnelLensOnSkippedPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Driftwood rerolls replace the options on the same reward/screen. Refresh
/// Fresnel Lens's snapshot so resolution is compared with the final offer.
/// </summary>
[HarmonyPatch(typeof(CardReward), nameof(CardReward.Reroll))]
public static class CardRewardFresnelLensRerollPatch
{
    [HarmonyPostfix]
    public static void Postfix(CardReward __instance)
    {
        try
        {
            RunTracker.RefreshFresnelLensRewardAfterReroll(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CardRewardFresnelLensRerollPatch failed: {e.Message}");
        }
    }
}
