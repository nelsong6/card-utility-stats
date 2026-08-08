using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Brackets Choices Paradox's owner-specific turn-start callback so the generic
/// card-selection command it awaits can be told apart from every other caller.
/// The relic generates its options and enters that command synchronously before
/// its first await, so the window is still open when the grid is armed and has
/// already closed by the time the callback's task is handed back.
/// </summary>
[HarmonyPatch(typeof(ChoicesParadox), nameof(ChoicesParadox.AfterPlayerTurnStart))]
internal static class ChoicesParadoxAfterPlayerTurnStartStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        ChoicesParadox __instance,
        Player player,
        out ChoicesParadoxSelectionWindow? __state)
    {
        __state = null;

        try
        {
            __state = RunTracker.BeginChoicesParadoxSelection(__instance, player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"ChoicesParadoxAfterPlayerTurnStartStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception? __exception,
        ChoicesParadoxSelectionWindow? __state)
    {
        RunTracker.EndChoicesParadoxSelection(__state);
        return __exception;
    }
}

/// <summary>
/// Observes the offered rarities on the armed grid and, once the selection
/// resolves, the rarity actually taken. The command materializes its result
/// before returning, so awaiting the same task alongside the relic is safe and
/// reads the real pick rather than the prompt's requested count.
/// </summary>
[HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromSimpleGrid))]
public static class CardSelectCmdChoicesParadoxFromSimpleGridPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        PlayerChoiceContext context,
        IReadOnlyList<CardModel> cardsIn,
        Player player,
        CardSelectorPrefs prefs,
        out bool __state)
    {
        __state = false;

        try
        {
            __state = RunTracker.RecordChoicesParadoxOffers(cardsIn, player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"CardSelectCmdChoicesParadoxFromSimpleGridPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(bool __state, Task<IEnumerable<CardModel>> __result)
    {
        if (!__state || __result == null) return;
        ObserveSelectionAsync(__result);
    }

    private static async void ObserveSelectionAsync(
        Task<IEnumerable<CardModel>> selectionTask)
    {
        try
        {
            var selectedCards = await selectionTask.ConfigureAwait(false);
            RunTracker.RecordChoicesParadoxTaken(selectedCards);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"CardSelectCmdChoicesParadoxFromSimpleGridPatch selection observation failed: {e.Message}");
        }
    }
}
