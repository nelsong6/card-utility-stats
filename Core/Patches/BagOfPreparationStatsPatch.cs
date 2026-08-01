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
/// Ring of the Snake has the same owner/turn gate and hand-draw modifier shape
/// as Bag of Preparation. Keep its observations in a separate relic aggregate.
/// </summary>
[HarmonyPatch(typeof(RingOfTheSnake), nameof(RingOfTheSnake.ModifyHandDraw))]
public static class RingOfTheSnakeModifyHandDrawPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        RingOfTheSnake __instance,
        Player player,
        decimal count,
        decimal __result)
    {
        try
        {
            var added = __result - count;
            if (added <= 0m) return;

            RunTracker.RecordRingOfTheSnakeActivation(
                __instance,
                player,
                (int)Math.Ceiling(added));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RingOfTheSnakeModifyHandDrawPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Captures the completed hand-draw modifier chain so the eventual draw result
/// can be compared with the same request without each opening-hand relic's
/// individual delta.
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
            RunTracker.FinalizeRingOfTheSnakeHandDraw(player, __result);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookModifyHandDrawBagOfPreparationPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Measures how many cards from the resolved first-turn hand draw exist only
/// because of each opening-hand relic's contribution. The surviving marginal
/// request is also retained so observed draw shortfalls can be counted as
/// blocked draws.
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
        out IReadOnlyList<OpeningHandDrawRelicObservation>? __state)
    {
        __state = null;

        try
        {
            var pending = new List<(OpeningHandDrawRelicKind Kind, decimal Counterfactual)>();
            if (RunTracker.TryConsumeBagOfPreparationDrawAttribution(
                    player,
                    fromHandDraw,
                    out var rawHandDrawWithoutBag))
            {
                pending.Add((OpeningHandDrawRelicKind.BagOfPreparation, rawHandDrawWithoutBag));
            }

            if (RunTracker.TryConsumeRingOfTheSnakeDrawAttribution(
                    player,
                    fromHandDraw,
                    out var rawHandDrawWithoutRing))
            {
                pending.Add((OpeningHandDrawRelicKind.RingOfTheSnake, rawHandDrawWithoutRing));
            }

            if (pending.Count == 0) return;

            var innateCount = PileType.Draw
                .GetPile(player)
                .Cards
                .Count(card =>
                    card.Keywords.Contains(CardKeyword.Innate)
                    && card.Enchantment?.ShouldStartAtBottomOfDrawPile != true);
            var cardsRequestedWithRelics =
                count > 0m
                    ? (int)Math.Ceiling(count)
                    : 0;
            __state = pending
                .Select(entry =>
                {
                    var counterfactualDraw = Math.Min(
                        Math.Max(entry.Counterfactual, innateCount),
                        CardPile.MaxCardsInHand);
                    var cardsRequestedWithoutRelic =
                        counterfactualDraw > 0m
                            ? (int)Math.Ceiling(counterfactualDraw)
                            : 0;
                    return new OpeningHandDrawRelicObservation(
                        entry.Kind,
                        cardsRequestedWithoutRelic,
                        Math.Max(
                            0,
                            cardsRequestedWithRelics - cardsRequestedWithoutRelic));
                })
                .ToArray();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BagOfPreparationCardPileDrawPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(
        IReadOnlyList<OpeningHandDrawRelicObservation>? __state,
        Task<IEnumerable<CardModel>> __result)
    {
        if (__state == null || __state.Count == 0 || __result == null) return;
        ObserveDrawResultAsync(__state, __result);
    }

    private static async void ObserveDrawResultAsync(
        IReadOnlyList<OpeningHandDrawRelicObservation> observations,
        Task<IEnumerable<CardModel>> drawTask)
    {
        try
        {
            var cards = await drawTask.ConfigureAwait(false);
            var totalCardsDrawn = cards?.Count(card => card != null) ?? 0;
            foreach (var observation in observations)
            {
                var cardsDrawnBecauseOfRelic = CalculateObservedContributionForTest(
                    observation.CardsRequestedWithoutRelic,
                    observation.MaximumRelicContribution,
                    totalCardsDrawn);
                if (observation.Kind == OpeningHandDrawRelicKind.BagOfPreparation)
                {
                    RunTracker.RecordBagOfPreparationDrawResult(
                        observation.MaximumRelicContribution,
                        cardsDrawnBecauseOfRelic);
                }
                else
                {
                    RunTracker.RecordRingOfTheSnakeDrawResult(
                        observation.MaximumRelicContribution,
                        cardsDrawnBecauseOfRelic);
                }
            }
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

public enum OpeningHandDrawRelicKind
{
    BagOfPreparation,
    RingOfTheSnake,
}

public sealed record OpeningHandDrawRelicObservation(
    OpeningHandDrawRelicKind Kind,
    int CardsRequestedWithoutRelic,
    int MaximumRelicContribution);
