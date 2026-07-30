using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Counts cards at Joss Paper's owner-specific exhaust callback. The callback
/// runs only after the card has actually exhausted and includes Ethereal cards
/// even though the relic defers their threshold resolution until turn end.
/// </summary>
[HarmonyPatch(
    typeof(JossPaper),
    nameof(JossPaper.AfterCardExhausted),
    new[] { typeof(PlayerChoiceContext), typeof(CardModel), typeof(bool) })]
public static class JossPaperAfterCardExhaustedStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(JossPaper __instance, CardModel card)
    {
        try
        {
            RunTracker.RecordJossPaperCardExhausted(__instance, card);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"JossPaperAfterCardExhaustedStatsPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Samples the live counter only after Joss Paper has folded its deferred
/// Ethereal exhausts into the counter and completed any resulting draws.
/// </summary>
[HarmonyPatch(typeof(JossPaper), nameof(JossPaper.AfterSideTurnEnd))]
public static class JossPaperAfterSideTurnEndStatsPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        JossPaper __instance,
        CombatSide side,
        ref Task __result)
    {
        if (side != CombatSide.Player || __result == null) return;
        __result = ObserveTurnEndAsync(__instance, __result);
    }

    private static async Task ObserveTurnEndAsync(
        JossPaper relic,
        Task originalTask)
    {
        await originalTask.ConfigureAwait(false);

        try
        {
            RunTracker.RecordJossPaperTurnEnded(relic);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"JossPaperAfterSideTurnEndStatsPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Arms observed draw attribution after Joss Paper's counter has already been
/// incremented (or received its deferred Ethereal batch) and immediately
/// before the relic issues its draw command.
/// </summary>
[HarmonyPatch]
public static class JossPaperDrawIfThresholdMetStatsPatch
{
    private static MethodBase? TargetMethod()
        => AccessTools.Method(
            typeof(JossPaper),
            "DrawIfThresholdMet",
            [typeof(PlayerChoiceContext)]);

    private static bool Prepare() => TargetMethod() != null;

    [HarmonyPrefix]
    public static void Prefix(
        JossPaper __instance,
        out PendingJossPaperDraw? __state)
    {
        __state = null;

        try
        {
            var threshold = Math.Max(
                1,
                (int)Math.Ceiling(
                    __instance.DynamicVars["ExhaustAmount"].BaseValue));
            var activations = CalculateActivationCountForTest(
                __instance.CardsExhausted,
                threshold);
            if (activations <= 0) return;

            __state = RunTracker.ArmJossPaperDrawAttribution(
                __instance,
                activations);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"JossPaperDrawIfThresholdMetStatsPatch failed: {e.Message}");
        }
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception? __exception,
        PendingJossPaperDraw? __state)
    {
        if (__exception != null)
            RunTracker.DisarmJossPaperDrawAttribution(__state);
        return __exception;
    }

    internal static int CalculateActivationCountForTest(
        int cardsExhausted,
        int threshold)
    {
        if (cardsExhausted <= 0 || threshold <= 0) return 0;
        return cardsExhausted / threshold;
    }
}

/// <summary>
/// Observes the exact card collection returned by Joss Paper's direct draw.
/// A full hand, No Draw, or an exhausted draw pile therefore becomes a blocked
/// draw instead of being silently counted as value.
/// </summary>
[HarmonyPatch(
    typeof(CardPileCmd),
    nameof(CardPileCmd.Draw),
    new[]
    {
        typeof(PlayerChoiceContext),
        typeof(decimal),
        typeof(Player),
        typeof(bool),
    })]
public static class JossPaperCardPileDrawStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        Player player,
        bool fromHandDraw,
        decimal count,
        out PendingJossPaperDraw? __state)
    {
        __state = null;

        try
        {
            RunTracker.TryConsumeJossPaperDrawAttribution(
                player,
                fromHandDraw,
                count,
                out __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"JossPaperCardPileDrawStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingJossPaperDraw? __state,
        Task<IEnumerable<CardModel>> __result)
    {
        if (__state == null || __result == null) return;
        ObserveDrawResultAsync(__state, __result);
    }

    private static async void ObserveDrawResultAsync(
        PendingJossPaperDraw pending,
        Task<IEnumerable<CardModel>> drawTask)
    {
        try
        {
            var cards = await drawTask.ConfigureAwait(false);
            var cardsDrawn = cards?.Count(card => card != null) ?? 0;
            RunTracker.RecordJossPaperDrawResult(pending, cardsDrawn);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"JossPaperCardPileDrawStatsPatch draw observation failed: {e.Message}");
        }
    }
}
