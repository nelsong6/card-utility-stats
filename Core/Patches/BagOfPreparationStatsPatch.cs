using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Bag of Preparation contributes to the owner's first-turn hand draw through
/// its own modifier. The positive input/output delta is the relic's draw
/// contribution and one confirmed activation.
/// </summary>
[HarmonyPatch(typeof(BagOfPreparation), nameof(BagOfPreparation.ModifyHandDraw))]
public static class BagOfPreparationModifyHandDrawPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        BagOfPreparation __instance,
        Player player,
        decimal count,
        decimal __result)
    {
        try
        {
            var added = __result - count;
            if (added <= 0m) return;

            RunTracker.RecordBagOfPreparationActivation(
                __instance,
                player,
                (int)Math.Ceiling(added));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BagOfPreparationModifyHandDrawPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Captures the completed hand-draw modifier chain so the eventual draw result
/// can be compared with the same request without Bag of Preparation's delta.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.ModifyHandDraw))]
public static class HookModifyHandDrawBagOfPreparationPatch
{
    [HarmonyPostfix]
    public static void Postfix(Player player, decimal __result)
    {
        try
        {
            RunTracker.FinalizeBagOfPreparationHandDraw(player, __result);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookModifyHandDrawBagOfPreparationPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Measures how many cards from the resolved first-turn hand draw exist only
/// because of Bag of Preparation's contribution. Draw prevention, a short
/// deck, the hand cap, and an already-larger Innate hand can reduce this below
/// the relic's requested two cards.
/// </summary>
[HarmonyPatch(
    typeof(CardPileCmd),
    nameof(CardPileCmd.Draw),
    new[] { typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool) })]
public static class BagOfPreparationCardPileDrawPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        decimal count,
        Player player,
        bool fromHandDraw,
        out BagOfPreparationDrawObservation? __state)
    {
        __state = null;

        try
        {
            if (!RunTracker.TryConsumeBagOfPreparationDrawAttribution(
                    player,
                    fromHandDraw,
                    out var rawHandDrawWithoutBag))
            {
                return;
            }

            var innateCount = PileType.Draw
                .GetPile(player)
                .Cards
                .Count(card =>
                    card.Keywords.Contains(CardKeyword.Innate)
                    && card.Enchantment?.ShouldStartAtBottomOfDrawPile != true);
            var counterfactualDraw = Math.Min(
                Math.Max(rawHandDrawWithoutBag, innateCount),
                CardPile.MaxCardsInHand);
            var cardsRequestedWithoutBag =
                counterfactualDraw > 0m
                    ? (int)Math.Ceiling(counterfactualDraw)
                    : 0;
            var cardsRequestedWithBag =
                count > 0m
                    ? (int)Math.Ceiling(count)
                    : 0;
            var maximumBagContribution =
                Math.Max(0, cardsRequestedWithBag - cardsRequestedWithoutBag);

            __state = new BagOfPreparationDrawObservation(
                cardsRequestedWithoutBag,
                maximumBagContribution);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BagOfPreparationCardPileDrawPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(
        BagOfPreparationDrawObservation? __state,
        Task<IEnumerable<CardModel>> __result)
    {
        if (__state == null || __result == null) return;
        ObserveDrawResultAsync(__state, __result);
    }

    private static async void ObserveDrawResultAsync(
        BagOfPreparationDrawObservation observation,
        Task<IEnumerable<CardModel>> drawTask)
    {
        try
        {
            var cards = await drawTask.ConfigureAwait(false);
            var totalCardsDrawn = cards?.Count(card => card != null) ?? 0;
            var cardsDrawnBecauseOfBag = CalculateObservedContributionForTest(
                observation.CardsRequestedWithoutBag,
                observation.MaximumBagContribution,
                totalCardsDrawn);
            RunTracker.RecordBagOfPreparationCardsDrawn(cardsDrawnBecauseOfBag);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BagOfPreparationCardPileDrawPatch draw observation failed: {e.Message}");
        }
    }

    internal static int CalculateObservedContributionForTest(
        int cardsRequestedWithoutBag,
        int maximumBagContribution,
        int totalCardsDrawn)
    {
        return Math.Min(
            Math.Max(0, maximumBagContribution),
            Math.Max(0, totalCardsDrawn - Math.Max(0, cardsRequestedWithoutBag)));
    }
}

public sealed record BagOfPreparationDrawObservation(
    int CardsRequestedWithoutBag,
    int MaximumBagContribution);
