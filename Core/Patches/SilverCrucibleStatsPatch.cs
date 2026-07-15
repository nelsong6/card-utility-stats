using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;

namespace SpireLens.Core.Patches;

/// <summary>
/// Continued combat rooms rebuild card rewards from creation options rather
/// than serializing the offered cards. Bound ordered fallback strictly to the
/// synchronous Populate phase of that one reward-generation batch; outside
/// this window, restoration requires an exact saved card signature.
/// </summary>
[HarmonyPatch(typeof(RewardsSet), nameof(RewardsSet.GenerateWithoutOffering))]
public static class RewardsSetSilverCrucibleRestoreBatchPatch
{
    [HarmonyPrefix]
    public static void Prefix(RewardsSet __instance, out bool __state)
    {
        __state = false;

        try
        {
            __state = RunTracker.BeginSilverCrucibleRewardRestoreBatch(__instance.Player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RewardsSetSilverCrucibleRestoreBatchPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(bool __state)
    {
        if (!__state) return;

        try
        {
            RunTracker.EndSilverCrucibleRewardRestoreBatch();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RewardsSetSilverCrucibleRestoreBatchPatch.Postfix failed: {e.Message}");
        }
    }
}

/// <summary>
/// Detects the exact CardReward.Populate call that consumes each of Silver
/// Crucible's three saved uses. The relic increments TimesUsed synchronously
/// through the reward-modifier after-hook, so the observed after value is the
/// one-based reward number. Populate has also finished every modifier before
/// the postfix snapshots the final visible option order.
/// </summary>
[HarmonyPatch(typeof(CardReward), nameof(CardReward.Populate))]
public static class CardRewardSilverCruciblePopulatePatch
{
    [HarmonyPrefix]
    public static void Prefix(CardReward __instance, out PopulationState __state)
    {
        __state = default;

        try
        {
            var relic = __instance?.Player?.Relics?.OfType<SilverCrucible>().FirstOrDefault();
            if (relic == null) return;

            __state = new PopulationState(relic, relic.TimesUsed);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CardRewardSilverCruciblePopulatePatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(CardReward __instance, PopulationState __state)
    {
        try
        {
            var relic = __state.Relic;
            if (relic != null
                && relic.TimesUsed == __state.TimesUsed + 1
                && relic.TimesUsed is >= 1 and <= 3)
            {
                RunTracker.NoteSilverCrucibleRewardGenerated(__instance, relic.TimesUsed);
                return;
            }

            if (relic != null)
                RunTracker.RestoreSilverCrucibleRewardAfterPopulate(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CardRewardSilverCruciblePopulatePatch.Postfix failed: {e.Message}");
        }
    }

    public readonly record struct PopulationState(SilverCrucible? Relic, int TimesUsed);
}

/// <summary>
/// Refreshes the offered-card display snapshot immediately before the first
/// selection opens, then resolves only when the CardReward is actually
/// consumed. Returning false is the inner card-screen Skip and deliberately
/// leaves the outer reward pending.
/// </summary>
[HarmonyPatch]
public static class CardRewardSilverCrucibleOnSelectPatch
{
    private static MethodBase? TargetMethod()
        => AccessTools.Method(typeof(CardReward), "OnSelect");

    [HarmonyPrefix]
    public static void Prefix(CardReward __instance)
    {
        try
        {
            RunTracker.NoteSilverCrucibleRewardOpened(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CardRewardSilverCrucibleOnSelectPatch.Prefix failed: {e.Message}");
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
            if (await selectionTask)
                RunTracker.RecordSilverCrucibleRewardResolved(reward);
        }
        catch (Exception e)
        {
            RunTracker.PreserveSilverCrucibleRewardAfterFault(reward);
            CoreMain.LogDebug($"CardRewardSilverCrucibleOnSelectPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// The outer rewards-page skip is terminal for the CardReward. Its option
/// list is still intact, so every offered card is recorded as not taken.
/// </summary>
[HarmonyPatch(typeof(CardReward), nameof(CardReward.OnSkipped))]
public static class CardRewardSilverCrucibleOnSkippedPatch
{
    [HarmonyPostfix]
    public static void Postfix(CardReward __instance)
    {
        try
        {
            // If the outer reward was never opened, this refreshes any
            // same-page relic modifications before the intact list resolves.
            RunTracker.NoteSilverCrucibleRewardOpened(__instance);
            RunTracker.RecordSilverCrucibleRewardResolved(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CardRewardSilverCrucibleOnSkippedPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// A reroll explicitly records the current options as unpicked before clearing
/// them. Finalize that Silver use in the prefix; the nested Populate call will
/// arm the rerolled options as the next Silver use while charges remain.
/// </summary>
[HarmonyPatch(typeof(CardReward), nameof(CardReward.Reroll))]
public static class CardRewardSilverCrucibleRerollPatch
{
    [HarmonyPrefix]
    public static void Prefix(CardReward __instance)
    {
        try
        {
            RunTracker.RecordSilverCrucibleRewardResolved(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CardRewardSilverCrucibleRerollPatch failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(CardReward __instance)
    {
        try
        {
            // Reroll refreshes the already-open screen from inside the same
            // OnSelect task. Mark the newly generated Silver offer as opened
            // so resolution keeps its complete pre-selection snapshot.
            RunTracker.NoteSilverCrucibleRewardOpened(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CardRewardSilverCrucibleRerollPatch.Postfix failed: {e.Message}");
        }
    }
}
