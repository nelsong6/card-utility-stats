using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace SpireLens.Core.Patches;

/// <summary>
/// Free Attack's late cost modifier is queried repeatedly for UI and
/// playability. Remember its marginal reduction for the exact card, but defer
/// counting until BeforeCardPlayed confirms that card consumed a charge.
/// </summary>
[HarmonyPatch(typeof(FreeAttackPower), nameof(FreeAttackPower.TryModifyEnergyCostInCombatLate))]
public static class FreeAttackPowerCostStatsPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        FreeAttackPower __instance,
        CardModel card,
        decimal originalCost,
        ref decimal modifiedCost,
        bool __result)
    {
        try
        {
            if (!__result) return;
            RunTracker.RememberFreeAttackEnergySavings(
                __instance,
                card,
                originalCost,
                modifiedCost);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"FreeAttackPowerCostStatsPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Observe Free Attack's owner-specific charge consumption. The native
/// callback awaits PowerCmd.Decrement, so wrapping its task lets the tracker
/// require an actual amount decrease before recording a use.
/// </summary>
[HarmonyPatch(typeof(FreeAttackPower), nameof(FreeAttackPower.BeforeCardPlayed))]
public static class FreeAttackPowerBeforeCardPlayedStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        FreeAttackPower __instance,
        CardPlay cardPlay,
        out object? __state)
    {
        __state = RunTracker.CaptureFreeAttackUse(__instance, cardPlay);
    }

    [HarmonyPostfix]
    public static void Postfix(object? __state, ref Task __result)
    {
        try
        {
            if (__state is not PendingFreeAttackUse observation || __result == null) return;
            __result = ObserveAsync(__result, observation);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"FreeAttackPowerBeforeCardPlayedStatsPatch failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(Task inner, PendingFreeAttackUse observation)
    {
        await inner.ConfigureAwait(false);
        RunTracker.RecordFreeAttackUse(observation);
    }
}
