using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Vambrace doubles the first card-sourced block packet(s) from one card each
/// combat. The game calls this hook only when Vambrace actually modified a
/// block amount, so record the observed extra block from the final amount.
/// </summary>
[HarmonyPatch(typeof(Vambrace), nameof(Vambrace.AfterModifyingBlockAmount))]
public static class VambraceAfterModifyingBlockAmountPatch
{
    [HarmonyPrefix]
    public static void Prefix(Vambrace __instance, decimal modifiedAmount, CardModel? cardSource)
    {
        try
        {
            if (__instance == null || !RunTracker.IsTrackedRelic(__instance)) return;
            if (modifiedAmount <= 0m || cardSource == null) return;

            RunTracker.RecordVambraceExtraBlockGained(modifiedAmount);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"VambraceAfterModifyingBlockAmountPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Count the activation at the same moment Vambrace marks its once-per-combat
/// trigger as spent, rather than once per block packet.
/// </summary>
[HarmonyPatch(typeof(Vambrace), nameof(Vambrace.AfterCardPlayed))]
public static class VambraceAfterCardPlayedPatch
{
    private static readonly FieldInfo? TriggeringCardField =
        AccessTools.Field(typeof(Vambrace), "_triggeringCard");

    private static readonly FieldInfo? BlockGainedThisCombatField =
        AccessTools.Field(typeof(Vambrace), "_blockGainedThisCombat");

    [HarmonyPrefix]
    public static void Prefix(Vambrace __instance, CardPlay cardPlay)
    {
        try
        {
            if (!ShouldRecordActivation(__instance, cardPlay)) return;

            RunTracker.RecordVambraceActivation();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"VambraceAfterCardPlayedPatch failed: {e.Message}");
        }
    }

    private static bool ShouldRecordActivation(Vambrace relic, CardPlay cardPlay)
    {
        if (relic?.Owner == null || cardPlay?.Card == null) return false;
        if (!RunTracker.IsTrackedRelic(relic)) return false;
        if (!ReferenceEquals(cardPlay.Card.Owner, relic.Owner)) return false;
        if (BlockGainedThisCombatField?.GetValue(relic) is not bool alreadyUsed || alreadyUsed) return false;

        var triggeringCard = TriggeringCardField?.GetValue(relic);
        return ReferenceEquals(cardPlay.Card, triggeringCard);
    }
}
