using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace SpireLens.Core.Patches;

/// <summary>
/// Juggling already maintains an exact, turn-local Attack counter in its
/// internal data. Surface that counter through the power's native amount label
/// without changing Amount, which remains the number of Juggling stacks and
/// still controls how many Attack copies the power creates.
/// </summary>
[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.DisplayAmount), MethodType.Getter)]
public static class JugglingPowerDisplayAmountPatch
{
    [HarmonyPostfix]
    public static void Postfix(PowerModel __instance, ref int __result)
    {
        try
        {
            if (__instance is not JugglingPower juggling || !juggling.IsMutable) return;
            __result = NormalizeAttackCount(
                juggling.GetInternalData<JugglingPower.Data>().attacksPlayedThisTurn);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"JugglingPowerDisplayAmountPatch failed: {e.Message}");
        }
    }

    internal static int NormalizeAttackCount(int attacksPlayedThisTurn)
        => Math.Max(0, attacksPlayedThisTurn);

    internal static void NotifyDisplayAmountChanged(JugglingPower juggling)
    {
        try
        {
            if (juggling?.IsMutable != true) return;
            juggling.InvokeDisplayAmountChanged();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"Juggling display refresh failed: {e.Message}");
        }
    }
}

/// <summary>
/// AfterApplied seeds Juggling's counter from Attacks already played this turn.
/// Refresh the amount label after that seed is in place.
/// </summary>
[HarmonyPatch(typeof(JugglingPower), nameof(JugglingPower.AfterApplied))]
public static class JugglingPowerAfterAppliedStatsPatch
{
    [HarmonyPostfix]
    public static void Postfix(JugglingPower __instance)
        => JugglingPowerDisplayAmountPatch.NotifyDisplayAmountChanged(__instance);
}

/// <summary>
/// Juggling increments before its first await, so its returned Task need not
/// finish before the native amount label can show the new Attack count.
/// </summary>
[HarmonyPatch(typeof(JugglingPower), nameof(JugglingPower.AfterCardPlayed))]
public static class JugglingPowerAfterCardPlayedStatsPatch
{
    [HarmonyPostfix]
    public static void Postfix(JugglingPower __instance, CardPlay cardPlay)
    {
        try
        {
            if (cardPlay?.Card == null || __instance?.Owner?.Player == null) return;
            if (!ReferenceEquals(cardPlay.Card.Owner, __instance.Owner.Player)) return;
            if (cardPlay.Card.Type != CardType.Attack) return;

            JugglingPowerDisplayAmountPatch.NotifyDisplayAmountChanged(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"JugglingPowerAfterCardPlayedStatsPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Refresh the amount label after Juggling resets its counter for its owner's
/// completed side turn.
/// </summary>
[HarmonyPatch(typeof(JugglingPower), nameof(JugglingPower.AfterSideTurnEnd))]
public static class JugglingPowerAfterSideTurnEndStatsPatch
{
    [HarmonyPostfix]
    public static void Postfix(JugglingPower __instance, IEnumerable<Creature> participants)
    {
        try
        {
            if (__instance?.Owner == null || participants == null) return;
            foreach (var participant in participants)
            {
                if (!ReferenceEquals(participant, __instance.Owner)) continue;
                JugglingPowerDisplayAmountPatch.NotifyDisplayAmountChanged(__instance);
                return;
            }
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"JugglingPowerAfterSideTurnEndStatsPatch failed: {e.Message}");
        }
    }
}
