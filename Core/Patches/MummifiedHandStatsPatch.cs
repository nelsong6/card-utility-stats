using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Opens one narrow observation scope around Mummified Hand's own
/// AfterCardPlayed callback. The game selects and discounts the card
/// synchronously before returning Task.CompletedTask, so the matching
/// SetToFreeThisTurn call can be observed without leaving a broad attribution
/// window open.
/// </summary>
[HarmonyPatch(typeof(MummifiedHand), nameof(MummifiedHand.AfterCardPlayed))]
public static class MummifiedHandAfterCardPlayedPatch
{
    [HarmonyPrefix]
    public static void Prefix(MummifiedHand __instance, CardPlay cardPlay, out MummifiedHandTriggerObservation? __state)
    {
        __state = null;

        try
        {
            if (CombatManager.Instance?.IsInProgress != true) return;
            if (__instance?.Owner == null || cardPlay?.Card == null) return;
            if (!ReferenceEquals(cardPlay.Card.Owner, __instance.Owner)) return;
            if (cardPlay.Card.Type != CardType.Power) return;
            if (!RunTracker.IsTrackedRelic(__instance)) return;

            __state = new MummifiedHandTriggerObservation(__instance, cardPlay);
            MummifiedHandObservationScope.Begin(__state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"MummifiedHandAfterCardPlayedPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(MummifiedHandTriggerObservation? __state)
    {
        try
        {
            if (__state == null) return;

            RunTracker.RecordMummifiedHandTrigger(
                __state.Relic,
                __state.CardPlay,
                __state.DiscountedCard,
                __state.DiscountedCardCostBefore,
                __state.DiscountedCardCostAfter);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"MummifiedHandAfterCardPlayedPatch.Postfix failed: {e.Message}");
        }
        finally
        {
            MummifiedHandObservationScope.End(__state);
        }
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(Exception? __exception, MummifiedHandTriggerObservation? __state)
    {
        MummifiedHandObservationScope.End(__state);
        return __exception;
    }
}

/// <summary>
/// Captures the exact card selected by Mummified Hand and its effective energy
/// cost immediately before and after the game applies its turn-long free cost.
/// Other callers of SetToFreeThisTurn are ignored because no observation scope
/// is active for them.
/// </summary>
[HarmonyPatch(typeof(CardModel), nameof(CardModel.SetToFreeThisTurn))]
public static class CardModelSetToFreeThisTurnMummifiedHandPatch
{
    [HarmonyPrefix]
    public static void Prefix(CardModel __instance, out MummifiedHandCardCostObservation? __state)
    {
        __state = null;

        try
        {
            var trigger = MummifiedHandObservationScope.Active;
            if (trigger == null) return;

            __state = new MummifiedHandCardCostObservation(
                trigger,
                __instance,
                EffectiveEnergyCost(__instance));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CardModelSetToFreeThisTurnMummifiedHandPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(CardModel __instance, MummifiedHandCardCostObservation? __state)
    {
        if (__state == null) return;

        try
        {
            __state.Trigger.DiscountedCard = __instance;
            __state.Trigger.DiscountedCardCostBefore = __state.CostBefore;
            __state.Trigger.DiscountedCardCostAfter = EffectiveEnergyCost(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CardModelSetToFreeThisTurnMummifiedHandPatch.Postfix failed: {e.Message}");
        }
    }

    private static decimal EffectiveEnergyCost(CardModel card)
    {
        if (card?.EnergyCost == null || card.EnergyCost.CostsX) return 0m;
        return Math.Max(0m, card.EnergyCost.GetWithModifiers(CostModifiers.All));
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
public static class HookAfterPlayerTurnStartMummifiedHandPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player)
    {
        try
        {
            RunTracker.RecordMummifiedHandTurnStarted(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookAfterPlayerTurnStartMummifiedHandPatch failed: {e.Message}");
        }
    }
}

public sealed class MummifiedHandTriggerObservation
{
    public MummifiedHandTriggerObservation(MummifiedHand relic, CardPlay cardPlay)
    {
        Relic = relic;
        CardPlay = cardPlay;
    }

    public MummifiedHand Relic { get; }
    public CardPlay CardPlay { get; }
    public CardModel? DiscountedCard { get; set; }
    public decimal DiscountedCardCostBefore { get; set; }
    public decimal DiscountedCardCostAfter { get; set; }
}

public sealed record MummifiedHandCardCostObservation(
    MummifiedHandTriggerObservation Trigger,
    CardModel Card,
    decimal CostBefore);

internal static class MummifiedHandObservationScope
{
    [ThreadStatic]
    private static MummifiedHandTriggerObservation? _active;

    internal static MummifiedHandTriggerObservation? Active => _active;

    internal static void Begin(MummifiedHandTriggerObservation observation)
    {
        _active = observation;
    }

    internal static void End(MummifiedHandTriggerObservation? observation)
    {
        if (observation != null && ReferenceEquals(_active, observation))
            _active = null;
    }
}
