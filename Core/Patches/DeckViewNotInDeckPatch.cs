using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens;

namespace SpireLens.Core.Patches;

/// <summary>
/// Switch the deck screen between its normal permanent-deck contents and
/// SpireLens's separate "cards not in deck" collection. The alternate view
/// contains removed physical cards plus pooled synthetic meta cards.
///
/// Why prefix not postfix: the grid's <c>SetCards</c> call uses <c>_cards</c>
/// directly as its source. Mutating before the body runs is simpler than
/// trying to re-trigger a render after the fact.
///
/// Removed cards still have valid <c>CardModel</c> refs because
/// <c>CardModel.RemoveFromState</c> only sets <c>HasBeenRemovedFromState</c>
/// (a flag) — it doesn't free the object. The grid renders them normally;
/// the hover tooltip fires via the existing <c>NCardHolder.CreateHoverTips</c>
/// patch and shows our stats including the "Removed floor X" lineage line
/// or the pooled-card banner for a synthetic meta card.
///
/// The mode and its "show all meta-cards" option both live-update through the
/// deck-view re-render wired up in <see cref="ViewStatsInjectorPatch"/>.
/// </summary>
[HarmonyPatch(typeof(NDeckViewScreen), nameof(NDeckViewScreen.DisplayCards))]
public static class DeckViewNotInDeckPatch
{
    [HarmonyPrefix]
    public static void Prefix(NDeckViewScreen __instance)
    {
        try
        {
            // Safe to reset: the grid's sort logic reads _sortingPriority
            // (a separate field on the screen), not the order of _cards.
            __instance._cards = __instance._pile.Cards.ToList();

            // The run-history viewer uses the same native deck screen, but its
            // pile is an exact reconstruction of that historical final deck.
            // Never append cards from the current/live RunTracker to it.
            if (RunHistoryDeckViewer.IsHistoricalDeckViewer(__instance))
            {
                // Still never append live-run cards here — but the archived
                // deck can be ordered, and DeckViewSpireLensSort reads its
                // numbers from the archived run for this screen.
                DeckViewSpireLensSort.Apply(__instance);
                return;
            }

            if (ViewStatsInjectorPatch.ShowCardsNotInDeckEnabled)
            {
                var notInDeckCards = RunTracker.GetCardsNotInDeckView(
                    ViewStatsInjectorPatch.ShowAllMetaCardsInNotInDeckView);
                __instance._cards = SelectCardsForView(
                    __instance._cards,
                    notInDeckCards,
                    showCardsNotInDeck: true);

                CoreMain.LogDebug(
                    $"DeckViewNotInDeck: displaying {__instance._cards.Count} cards not in deck");
            }

            // Which cards, then in what order. Both mutate _cards, so they
            // share this one prefix rather than racing as two — Harmony does
            // not guarantee an order between prefixes on the same method.
            DeckViewSpireLensSort.Apply(__instance);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"DeckViewNotInDeckPatch failed: {e.Message}");
        }
    }

    internal static List<T> SelectCardsForView<T>(
        IEnumerable<T> deckCards,
        IEnumerable<T> notInDeckCards,
        bool showCardsNotInDeck)
        where T : notnull
    {
        var source = showCardsNotInDeck ? notInDeckCards : deckCards;
        return source
            .Where(card => card != null)
            .Distinct()
            .ToList();
    }
}
