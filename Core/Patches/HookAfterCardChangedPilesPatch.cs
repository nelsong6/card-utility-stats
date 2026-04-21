using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;

namespace CardUtilityStats.Core.Patches;

/// <summary>
/// Tracks cards that are inserted directly into hand rather than drawn.
/// We hook <c>Hook.AfterCardChangedPiles</c> because it runs after the game
/// has already resolved full-hand fallback, so the card's current pile is
/// the truth of where it really ended up. Filtering to
/// <c>oldPile == None</c> keeps normal draw / discard-to-hand / pile-shift
/// movement out of this stat and leaves us with direct generation into hand.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardChangedPiles))]
public static class HookAfterCardChangedPilesPatch
{
    [HarmonyPrefix]
    public static void Prefix(CardModel card, PileType oldPile, AbstractModel? source)
    {
        try
        {
            if (card == null) return;
            RunTracker.RecordCardAddedToHand(card, oldPile, source);
        }
        catch (System.Exception e)
        {
            CoreMain.Logger.Error($"HookAfterCardChangedPilesPatch failed: {e.Message}");
        }
    }
}
