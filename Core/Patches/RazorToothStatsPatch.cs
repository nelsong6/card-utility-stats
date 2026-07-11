using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Counts cards Razor Tooth actually upgrades. Its owner-specific callback
/// calls CardCmd.Upgrade synchronously before returning Task.CompletedTask, so
/// comparing the same played card before and after captures successful upgrades
/// without inferring them from card type or eligibility.
/// </summary>
[HarmonyPatch]
public static class RazorToothAfterCardPlayedPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(
            typeof(RazorTooth),
            nameof(RazorTooth.AfterCardPlayed),
            new[] { typeof(PlayerChoiceContext), typeof(CardPlay) });
    }

    [HarmonyPrefix]
    public static void Prefix(RazorTooth __instance, CardPlay cardPlay, out UpgradeState __state)
    {
        __state = default;

        try
        {
            var card = cardPlay?.Card;
            if (__instance == null || card == null) return;
            if (!RunTracker.IsTrackedRelic(__instance)) return;

            __state = new UpgradeState(card, card.CurrentUpgradeLevel);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RazorToothAfterCardPlayedPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(UpgradeState __state)
    {
        try
        {
            if (__state.Card == null) return;
            RunTracker.RecordRazorToothUpgrade(
                __state.PreviousUpgradeLevel,
                __state.Card.CurrentUpgradeLevel);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RazorToothAfterCardPlayedPatch.Postfix failed: {e.Message}");
        }
    }

    public readonly record struct UpgradeState(CardModel? Card, int PreviousUpgradeLevel);
}
