using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Bookmark activates from its owner-owned flush callback and discounts one
/// retained card until played. Snapshot retained-card costs before the async
/// callback, then record the rarity of cards whose cost actually dropped.
/// </summary>
[HarmonyPatch(typeof(Bookmark), nameof(Bookmark.AfterFlush))]
public static class BookmarkAfterFlushPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        Bookmark __instance,
        PlayerChoiceContext choiceContext,
        Player player,
        IReadOnlyCollection<CardModel> flushedCards,
        IReadOnlyCollection<CardModel> retainedCards,
        out List<RetainedCardCostSnapshot>? __state)
    {
        __state = null;

        try
        {
            if (__instance?.Owner == null || player == null) return;
            if (!RunTracker.IsTrackedRelic(__instance)) return;
            if (!ReferenceEquals(player, __instance.Owner)) return;
            if (retainedCards == null || retainedCards.Count == 0) return;

            __state = retainedCards
                .Where(card => card != null)
                .Select(card => new RetainedCardCostSnapshot(
                    card,
                    card.Rarity,
                    GetEnergyCost(card),
                    GetStarCost(card)))
                .ToList();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BookmarkAfterFlushPatch.Prefix failed: {e.Message}");
            __state = null;
        }
    }

    [HarmonyPostfix]
    public static void Postfix(List<RetainedCardCostSnapshot>? __state, Task __result)
    {
        try
        {
            if (__state == null || __state.Count == 0) return;

            if (__result == null)
            {
                Complete(__state);
                return;
            }

            if (__result.IsCompleted)
            {
                if (!__result.IsCanceled && !__result.IsFaulted)
                    Complete(__state);
                return;
            }

            __result.ContinueWith(
                task =>
                {
                    if (!task.IsCanceled && !task.IsFaulted)
                        Complete(__state);
                },
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BookmarkAfterFlushPatch.Postfix failed: {e.Message}");
        }
    }

    private static void Complete(IReadOnlyCollection<RetainedCardCostSnapshot> snapshots)
    {
        try
        {
            var activatedRarities = snapshots
                .Where(snapshot =>
                    GetEnergyCost(snapshot.Card) < snapshot.EnergyCostBefore ||
                    GetStarCost(snapshot.Card) < snapshot.StarCostBefore)
                .Select(snapshot => snapshot.Rarity)
                .ToList();

            if (activatedRarities.Count == 0) return;
            RunTracker.RecordBookmarkActivations(activatedRarities);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BookmarkAfterFlushPatch.Complete failed: {e.Message}");
        }
    }

    private static int GetEnergyCost(CardModel card)
    {
        try
        {
            return Math.Max(0, card.EnergyCost.GetWithModifiers(CostModifiers.None));
        }
        catch
        {
            return 0;
        }
    }

    private static int GetStarCost(CardModel card)
    {
        try
        {
            return Math.Max(0, card.GetStarCostWithModifiers());
        }
        catch
        {
            return 0;
        }
    }

    public sealed record RetainedCardCostSnapshot(
        CardModel Card,
        CardRarity Rarity,
        int EnergyCostBefore,
        int StarCostBefore);
}
