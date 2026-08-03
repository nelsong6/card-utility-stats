using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Lead Paperweight owns a one-time, skippable two-card Colorless choice on
/// pickup. Keep an owner-specific window around the full callback so the
/// offered cards come from the native selector and the taken outcome is only
/// committed after the selected card actually reaches the permanent deck.
/// </summary>
[HarmonyPatch(typeof(LeadPaperweight), nameof(LeadPaperweight.AfterObtained))]
public static class LeadPaperweightAfterObtainedPatch
{
    [HarmonyPrefix]
    public static void Prefix(LeadPaperweight __instance, out Player? __state)
    {
        __state = null;

        try
        {
            RunTracker.BeginLeadPaperweightChoice(__instance, out __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"LeadPaperweightAfterObtainedPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(Player? __state, ref Task __result)
    {
        if (__state == null) return;

        try
        {
            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"LeadPaperweightAfterObtainedPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(Task inner, Player player)
    {
        var succeeded = false;
        try
        {
            if (inner != null)
                await inner;
            succeeded = true;
        }
        finally
        {
            RunTracker.CompleteLeadPaperweightChoice(player, succeeded);
        }
    }
}

[HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromChooseACardScreen))]
public static class LeadPaperweightChooseACardScreenPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        IReadOnlyList<CardModel> cards,
        Player player,
        bool canSkip,
        out Player? __state)
    {
        __state = null;

        try
        {
            if (RunTracker.CaptureLeadPaperweightOptions(player, cards, canSkip))
                __state = player;
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"LeadPaperweightChooseACardScreenPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(Player? __state, ref Task<CardModel> __result)
    {
        if (__state == null || __result == null) return;

        try
        {
            __result = ObserveSelectionAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"LeadPaperweightChooseACardScreenPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task<CardModel> ObserveSelectionAsync(
        Task<CardModel> inner,
        Player player)
    {
        var selectedCard = await inner;
        RunTracker.RecordLeadPaperweightSelection(player, selectedCard);
        return selectedCard;
    }
}
