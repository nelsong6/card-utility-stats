using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Nunchaku owns the attack counter and energy grant in AfterCardPlayed. The
/// prefix records the attack and arms an immediate energy-gain window only when
/// the live counter is one attack away from triggering.
/// </summary>
[HarmonyPatch(typeof(Nunchaku), nameof(Nunchaku.AfterCardPlayed))]
public static class NunchakuAfterCardPlayedPatch
{
    [HarmonyPrefix]
    public static void Prefix(Nunchaku __instance, PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            if (__instance == null || !RunTracker.IsTrackedRelic(__instance)) return;
            RunTracker.RecordNunchakuAttackPlayedAndArmEnergyAttribution(__instance, cardPlay);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"NunchakuAfterCardPlayedPatch failed: {e.Message}");
        }
    }
}
