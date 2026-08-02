using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Crossbow selects and discounts its generated Attack synchronously before
/// awaiting the combat-pile add. Keep a thread-local scope open for only that
/// synchronous portion of the owner callback.
/// </summary>
[HarmonyPatch(typeof(Crossbow), nameof(Crossbow.AfterSideTurnStart))]
internal static class CrossbowAfterSideTurnStartStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        Crossbow __instance,
        IReadOnlyList<Creature> participants,
        out CrossbowTurnObservation? __state)
    {
        __state = null;

        try
        {
            if (!RunTracker.RecordCrossbowTurnStarted(__instance, participants)) return;

            __state = new CrossbowTurnObservation(__instance.Owner);
            CrossbowObservationScope.Begin(__state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CrossbowAfterSideTurnStartStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(CrossbowTurnObservation? __state)
    {
        CrossbowObservationScope.End(__state);
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception? __exception,
        CrossbowTurnObservation? __state)
    {
        CrossbowObservationScope.End(__state);
        return __exception;
    }
}

/// <summary>
/// Measure Crossbow's effective energy discount around the exact
/// SetToFreeThisTurn mutation. Other callers are ignored unless the owner
/// callback scope above is active.
/// </summary>
[HarmonyPatch(typeof(CardModel), nameof(CardModel.SetToFreeThisTurn))]
internal static class CardModelSetToFreeThisTurnCrossbowStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        CardModel __instance,
        out CrossbowCardDiscountObservation? __state)
    {
        __state = null;

        try
        {
            var turn = CrossbowObservationScope.Active;
            if (turn == null
                || __instance == null
                || __instance.Type != CardType.Attack
                || !ReferenceEquals(__instance.Owner, turn.Player))
            {
                return;
            }

            __state = new CrossbowCardDiscountObservation(
                turn,
                __instance,
                EffectiveEnergyCost(__instance));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CardModelSetToFreeThisTurnCrossbowStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(CrossbowCardDiscountObservation? __state)
    {
        if (__state == null) return;

        try
        {
            var costAfter = EffectiveEnergyCost(__state.Card);
            __state.Turn.DiscountsByCard[__state.Card] = Math.Max(
                0m,
                __state.CostBefore - costAfter);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CardModelSetToFreeThisTurnCrossbowStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static decimal EffectiveEnergyCost(CardModel card)
    {
        if (card?.EnergyCost == null || card.EnergyCost.CostsX) return 0m;
        return Math.Max(0m, card.EnergyCost.GetWithModifiers(CostModifiers.All));
    }
}

/// <summary>
/// Count only Crossbow Attacks confirmed by the generated-card command. The
/// command result is authoritative for successful entry and final rarity.
/// </summary>
[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.AddGeneratedCardsToCombat))]
internal static class CrossbowGeneratedAttacksStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        IEnumerable<CardModel> cards,
        Player creator,
        out CrossbowGeneratedAttackBatch? __state)
    {
        __state = null;

        try
        {
            __state = CrossbowObservationScope.CaptureBatch(cards, creator);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CrossbowGeneratedAttacksStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(
        CrossbowGeneratedAttackBatch? __state,
        ref Task<IReadOnlyList<CardPileAddResult>> __result)
    {
        try
        {
            if (__state == null || __result == null) return;
            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CrossbowGeneratedAttacksStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task<IReadOnlyList<CardPileAddResult>> ObserveAsync(
        Task<IReadOnlyList<CardPileAddResult>> inner,
        CrossbowGeneratedAttackBatch batch)
    {
        var results = await inner.ConfigureAwait(false);
        try
        {
            foreach (var result in results)
            {
                if (!result.success
                    || result.cardAdded == null
                    || result.cardAdded.Type != CardType.Attack)
                {
                    continue;
                }

                var candidate = batch.Candidates.FirstOrDefault(
                    item => ReferenceEquals(item.Card, result.cardAdded));
                if (candidate == null && batch.Candidates.Count == 1)
                    candidate = batch.Candidates[0];
                if (candidate == null) continue;

                RunTracker.RecordCrossbowAttackGained(
                    batch.Player,
                    result.cardAdded,
                    candidate.Discount);
            }
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CrossbowGeneratedAttacksStatsPatch.ObserveAsync failed: {e.Message}");
        }

        return results;
    }
}

internal sealed class CrossbowTurnObservation
{
    public CrossbowTurnObservation(Player player)
    {
        Player = player;
    }

    public Player Player { get; }
    public Dictionary<CardModel, decimal> DiscountsByCard { get; }
        = new(ReferenceEqualityComparer.Instance);
}

internal sealed record CrossbowCardDiscountObservation(
    CrossbowTurnObservation Turn,
    CardModel Card,
    decimal CostBefore);

internal sealed record CrossbowAttackCandidate(CardModel Card, decimal Discount);

internal sealed record CrossbowGeneratedAttackBatch(
    Player Player,
    IReadOnlyList<CrossbowAttackCandidate> Candidates);

internal static class CrossbowObservationScope
{
    [ThreadStatic]
    private static CrossbowTurnObservation? _active;

    internal static CrossbowTurnObservation? Active => _active;

    internal static void Begin(CrossbowTurnObservation observation)
    {
        _active = observation;
    }

    internal static void End(CrossbowTurnObservation? observation)
    {
        if (observation != null && ReferenceEquals(_active, observation))
            _active = null;
    }

    internal static CrossbowGeneratedAttackBatch? CaptureBatch(
        IEnumerable<CardModel>? cards,
        Player? creator)
    {
        var turn = _active;
        if (turn == null || creator == null || !ReferenceEquals(turn.Player, creator))
            return null;

        var candidates = (cards ?? Array.Empty<CardModel>())
            .Where(card => card != null && turn.DiscountsByCard.ContainsKey(card))
            .Select(card => new CrossbowAttackCandidate(
                card,
                turn.DiscountsByCard[card]))
            .ToList();
        return candidates.Count == 0
            ? null
            : new CrossbowGeneratedAttackBatch(turn.Player, candidates);
    }
}
