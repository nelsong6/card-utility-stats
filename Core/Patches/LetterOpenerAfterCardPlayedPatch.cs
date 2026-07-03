using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Letter Opener activates after every third Skill played in a turn. The game
/// does not source the emitted damage entries to the relic, so we record the
/// activation and attempted damage at the owner callback.
/// </summary>
[HarmonyPatch(typeof(LetterOpener), nameof(LetterOpener.AfterCardPlayed))]
public static class LetterOpenerAfterCardPlayedPatch
{
    [HarmonyPrefix]
    public static void Prefix(LetterOpener __instance, CardPlay cardPlay)
    {
        try
        {
            if (__instance == null || cardPlay?.Card == null) return;
            if (cardPlay.Card.Owner != __instance.Owner) return;
            int threshold = Math.Max(1, __instance.DynamicVars.Cards.IntValue);
            RunTracker.RecordLetterOpenerBeforeCardPlayed(
                cardPlay,
                __instance.SkillsPlayedThisTurn + 1,
                threshold);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"LetterOpenerAfterCardPlayedPatch failed: {e.Message}");
        }
    }
}
