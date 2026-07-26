using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace SpireLens.Core.Patches;

/// <summary>
/// Counts the exact card-option modifications performed by the three egg
/// relics. Their reward and merchant hooks share this two-argument overload,
/// while direct cards added to the deck do not, so those non-offers stay out.
/// </summary>
[HarmonyPatch]
public static class EggRelicCardOfferStatsPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(
            typeof(CardCreationResult),
            nameof(CardCreationResult.ModifyCard),
            new[] { typeof(CardModel), typeof(RelicModel) });
    }

    [HarmonyPostfix]
    public static void Postfix(CardModel card, RelicModel modifyingRelic)
    {
        try
        {
            if (card == null) return;
            RunTracker.RecordEggUpgradedCardOffered(modifyingRelic, card.Rarity);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"EggRelicCardOfferStatsPatch failed: {e.Message}");
        }
    }
}
