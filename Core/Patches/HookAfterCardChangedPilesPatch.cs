using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;

namespace CardUtilityStats.Core.Patches;

/// <summary>
/// Observe the post-mutation pile-change hook so redirected draw attempts can
/// still be counted on the source card. When the hand is full, the game can
/// move the would-be drawn card somewhere other than Hand without producing a
/// Hook.ShouldDraw=false veto.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardChangedPiles))]
public static class HookAfterCardChangedPilesPatch
{
    [HarmonyPrefix]
    public static void Prefix(CardModel card, PileType oldPile)
    {
        try
        {
            if (card == null) return;
            RunTracker.RecordCardChangedPiles(card, oldPile);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"HookAfterCardChangedPilesPatch failed: {e.Message}");
        }
    }
}
