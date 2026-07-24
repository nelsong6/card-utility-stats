using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace SpireLens.Core.Patches;

/// <summary>
/// Observes a generated or transformed Soul only after it has entered its
/// final combat pile. RunTracker attributes that arrival to the card play
/// still resolving at this hook.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardGeneratedForCombat))]
public static class HookAfterCardGeneratedForCombatSoulStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(CardModel card, Player? creator)
    {
        try
        {
            if (card is not Soul) return;
            RunTracker.RecordSoulAddedToCombatPile(card, creator);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"HookAfterCardGeneratedForCombatSoulStatsPatch failed: {e.Message}");
        }
    }
}
