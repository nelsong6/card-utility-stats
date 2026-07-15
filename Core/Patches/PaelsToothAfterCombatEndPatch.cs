using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records the physical card Pael's Tooth actually returns after combat. The
/// game's native CardTitles list contains only cards still held by the relic,
/// so returned cards otherwise disappear from its tooltip history.
///
/// The owner-specific callback is awaited sequentially by the combat-end hook.
/// Wrapping its task keeps this observation ahead of SpireLens' CombatEnded
/// promotion. A raw deck-reference diff captures the final card after Tooth's
/// upgrade and any deck-add replacement modifiers; a removed SerializableCard
/// alone is not sufficient because Tooth removes it even when deck insertion
/// is blocked.
/// </summary>
[HarmonyPatch(typeof(PaelsTooth), nameof(PaelsTooth.AfterCombatEnd))]
public static class PaelsToothAfterCombatEndPatch
{
    [HarmonyPrefix]
    public static void Prefix(PaelsTooth __instance, out ReturnState __state)
    {
        __state = default;

        try
        {
            if (__instance == null || !RunTracker.IsTrackedRelic(__instance)) return;
            if (__instance.Owner?.Creature?.IsDead != false) return;
            if (__instance.SerializableCards?.Count <= 0) return;

            var deckCards = new HashSet<CardModel>(
                __instance.Owner.Deck.Cards,
                ReferenceEqualityComparer.Instance);
            __state = new ReturnState(__instance, deckCards);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"PaelsToothAfterCombatEndPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(ReturnState __state, ref Task __result)
    {
        try
        {
            if (__state.Relic == null || __state.DeckCards == null || __result == null) return;
            __result = ObserveReturnAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"PaelsToothAfterCombatEndPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task ObserveReturnAsync(Task original, ReturnState state)
    {
        await original;

        try
        {
            var returnedCards = state.Relic!.Owner.Deck.Cards
                .Where(card => card != null && !state.DeckCards!.Contains(card))
                .ToList();
            if (returnedCards.Count == 0) return;

            if (returnedCards.Count != 1)
            {
                CoreMain.LogDebug(
                    $"PaelsToothAfterCombatEndPatch observed {returnedCards.Count} new deck cards; " +
                    "return attribution skipped as ambiguous.");
                return;
            }

            RunTracker.RecordPaelsToothCardReturned(returnedCards[0]);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"PaelsToothAfterCombatEndPatch.ObserveReturnAsync failed: {e.Message}");
        }
    }

    public readonly record struct ReturnState(
        PaelsTooth? Relic,
        HashSet<CardModel>? DeckCards);
}
