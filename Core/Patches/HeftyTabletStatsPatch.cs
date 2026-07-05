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
/// Hefty Tablet grants a rare-card choice on pickup, with a real skip option.
/// Arm that owner-specific choice so the shared choose-card command can report
/// the selected card or null when skipped.
/// </summary>
[HarmonyPatch(typeof(HeftyTablet), nameof(HeftyTablet.AfterObtained))]
public static class HeftyTabletAfterObtainedPatch
{
    [HarmonyPrefix]
    public static void Prefix(HeftyTablet __instance, out Player? __state)
    {
        __state = null;

        try
        {
            if (__instance?.Owner == null) return;
            if (!RunTracker.IsTrackedRelic(__instance)) return;

            __state = __instance.Owner;
            RunTracker.ArmHeftyTabletChoice(__instance.Owner);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HeftyTabletAfterObtainedPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(Player? __state, Task __result)
    {
        if (__state == null) return;

        try
        {
            if (__result == null)
            {
                RunTracker.DisarmHeftyTabletChoice(__state);
                return;
            }

            __result.ContinueWith(
                _ => RunTracker.DisarmHeftyTabletChoice(__state),
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HeftyTabletAfterObtainedPatch.Postfix failed: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromChooseACardScreen))]
public static class HeftyTabletChooseACardScreenPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        IReadOnlyList<CardModel> cards,
        Player player,
        bool canSkip,
        out bool __state)
    {
        __state = false;

        try
        {
            __state = RunTracker.TryConsumeHeftyTabletChoiceScreen(player, cards, canSkip);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HeftyTabletChooseACardScreenPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(bool __state, Task<CardModel> __result)
    {
        if (!__state || __result == null) return;
        ObserveSelectionAsync(__result);
    }

    private static async void ObserveSelectionAsync(Task<CardModel> selectionTask)
    {
        try
        {
            var selectedCard = await selectionTask.ConfigureAwait(false);
            RunTracker.RecordHeftyTabletChoice(selectedCard);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HeftyTabletChooseACardScreenPatch selection observation failed: {e.Message}");
        }
    }
}
