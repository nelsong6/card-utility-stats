using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace SpireLens.Core.Patches;

/// <summary>
/// Free Skill's late modifier is queried repeatedly for UI and playability.
/// Remember the exact Skill's marginal reduction, then let the native
/// BeforeCardPlayed decrement confirm whether the charge was actually used.
/// </summary>
[HarmonyPatch(typeof(FreeSkillPower), nameof(FreeSkillPower.TryModifyEnergyCostInCombatLate))]
public static class FreeSkillPowerCostStatsPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        FreeSkillPower __instance,
        CardModel card,
        decimal originalCost,
        ref decimal modifiedCost,
        bool __result)
    {
        try
        {
            if (!__result) return;
            RunTracker.RememberFreeSkillEnergySavings(
                __instance,
                card,
                originalCost,
                modifiedCost);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"FreeSkillPowerCostStatsPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Observe Free Skill's owner-specific charge consumption after the native
/// async decrement completes and the power amount actually falls.
/// </summary>
[HarmonyPatch(typeof(FreeSkillPower), nameof(FreeSkillPower.BeforeCardPlayed))]
public static class FreeSkillPowerBeforeCardPlayedStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        FreeSkillPower __instance,
        CardPlay cardPlay,
        out object? __state)
    {
        __state = RunTracker.CaptureFreeSkillUse(__instance, cardPlay);
    }

    [HarmonyPostfix]
    public static void Postfix(object? __state, ref Task __result)
    {
        try
        {
            if (__state is not PendingFreeSkillUse observation || __result == null) return;
            __result = ObserveAsync(__result, observation);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"FreeSkillPowerBeforeCardPlayedStatsPatch failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(Task inner, PendingFreeSkillUse observation)
    {
        await inner.ConfigureAwait(false);
        RunTracker.RecordFreeSkillUse(observation);
    }
}
