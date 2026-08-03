using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
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
    {
        RunTracker.RecordJugglingPowerApplied(__instance);
        JugglingPowerDisplayAmountPatch.NotifyDisplayAmountChanged(__instance);
    }
}

/// <summary>
/// Juggling increments before its first await. Arm its exact third-Attack
/// copy window from the pre-increment counter, refresh the live counter after
/// the native callback starts, and keep attribution armed until every awaited
/// generated-card add has completed.
/// </summary>
[HarmonyPatch(typeof(JugglingPower), nameof(JugglingPower.BeforeCardPlayed))]
internal static class JugglingPowerBeforeCardPlayedStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        JugglingPower __instance,
        CardPlay cardPlay,
        out PendingJugglingCopyWindow? __state)
    {
        __state = RunTracker.ArmJugglingCopyAttribution(__instance, cardPlay);
    }

    [HarmonyPostfix]
    public static void Postfix(
        JugglingPower __instance,
        CardPlay cardPlay,
        PendingJugglingCopyWindow? __state,
        ref Task __result)
    {
        try
        {
            if (cardPlay?.Card != null
                && __instance?.Owner?.Player != null
                && ReferenceEquals(cardPlay.Card.Owner, __instance.Owner.Player)
                && cardPlay.Card.Type == CardType.Attack)
            {
                JugglingPowerDisplayAmountPatch.NotifyDisplayAmountChanged(__instance);
            }

            if (__state == null) return;
            if (__result == null)
            {
                RunTracker.DisarmJugglingCopyAttribution(__state);
                return;
            }

            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"JugglingPowerBeforeCardPlayedStatsPatch failed: {e.Message}");
            RunTracker.DisarmJugglingCopyAttribution(__state);
        }
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception? __exception,
        PendingJugglingCopyWindow? __state)
    {
        if (__exception != null)
            RunTracker.DisarmJugglingCopyAttribution(__state);
        return __exception;
    }

    private static async Task ObserveAsync(
        Task inner,
        PendingJugglingCopyWindow window)
    {
        try
        {
            await inner.ConfigureAwait(false);
        }
        finally
        {
            RunTracker.DisarmJugglingCopyAttribution(window);
        }
    }
}

/// <summary>
/// Observe the exact pile-add result of each generated Juggling clone. Failed
/// additions do not count; successful additions use the final card rarity
/// returned by the command.
/// </summary>
[HarmonyPatch(
    typeof(CardPileCmd),
    nameof(CardPileCmd.AddGeneratedCardToCombat),
    new[] { typeof(CardModel), typeof(PileType), typeof(Player), typeof(CardPilePosition) })]
internal static class JugglingGeneratedCardStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        CardModel card,
        Player creator,
        out PendingJugglingCopyWindow? __state)
    {
        __state = RunTracker.CaptureJugglingCopyAttempt(card, creator);
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingJugglingCopyWindow? __state,
        ref Task<CardPileAddResult> __result)
    {
        try
        {
            if (__state == null || __result == null) return;
            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"JugglingGeneratedCardStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task<CardPileAddResult> ObserveAsync(
        Task<CardPileAddResult> inner,
        PendingJugglingCopyWindow window)
    {
        var result = await inner.ConfigureAwait(false);
        try
        {
            RunTracker.RecordJugglingCopyResult(window, result);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"JugglingGeneratedCardStatsPatch.ObserveAsync failed: {e.Message}");
        }

        return result;
    }
}

/// <summary>
/// Count each distinct player turn that starts while Juggling is active,
/// including turns where it never reaches the third Attack.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
public static class HookAfterPlayerTurnStartJugglingStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player)
    {
        RunTracker.RecordJugglingPowerTurnStarted(player);
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
