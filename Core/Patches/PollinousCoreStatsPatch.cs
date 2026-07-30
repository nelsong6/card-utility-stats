using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Pollinous Core's positive hand-draw delta is its confirmed fourth-turn
/// activation. The eventual hand-draw result determines how much of that
/// requested contribution actually reached the hand.
/// </summary>
[HarmonyPatch(typeof(PollinousCore), nameof(PollinousCore.ModifyHandDraw))]
public static class PollinousCoreModifyHandDrawStatsPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        PollinousCore __instance,
        Player player,
        decimal count,
        decimal __result)
    {
        try
        {
            var added = __result - count;
            if (added <= 0m) return;

            RunTracker.RecordPollinousCoreActivation(
                __instance,
                player,
                (int)Math.Ceiling(added));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"PollinousCoreModifyHandDrawStatsPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Measures Pollinous Core's marginal contribution to the resolved hand draw.
/// This preserves observed outcomes when the hand cap, draw prevention, or a
/// short draw pile prevents one or both requested cards from landing.
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
public static class PollinousCoreCardPileDrawStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        decimal count,
        Player player,
        bool fromHandDraw,
        out PollinousCoreDrawObservation? __state)
    {
        __state = null;

        try
        {
            if (!RunTracker.TryConsumePollinousCoreDrawAttribution(
                    player,
                    fromHandDraw,
                    count,
                    out var handDrawWithoutPollinousCore,
                    out var maximumContribution))
            {
                return;
            }

            var innateCount = PileType.Draw
                .GetPile(player)
                .Cards
                .Count(card =>
                    card.Keywords.Contains(CardKeyword.Innate)
                    && card.Enchantment?.ShouldStartAtBottomOfDrawPile != true);
            var counterfactualDraw = Math.Min(
                Math.Max(handDrawWithoutPollinousCore, innateCount),
                CardPile.MaxCardsInHand);
            var cardsRequestedWithoutPollinousCore =
                counterfactualDraw > 0m
                    ? (int)Math.Ceiling(counterfactualDraw)
                    : 0;

            __state = new PollinousCoreDrawObservation(
                cardsRequestedWithoutPollinousCore,
                maximumContribution);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"PollinousCoreCardPileDrawStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(
        PollinousCoreDrawObservation? __state,
        Task<IEnumerable<CardModel>> __result)
    {
        if (__state == null || __result == null) return;
        ObserveDrawResultAsync(__state, __result);
    }

    private static async void ObserveDrawResultAsync(
        PollinousCoreDrawObservation observation,
        Task<IEnumerable<CardModel>> drawTask)
    {
        try
        {
            var cards = await drawTask.ConfigureAwait(false);
            var totalCardsDrawn = cards?.Count(card => card != null) ?? 0;
            var cardsDrawnBecauseOfPollinousCore =
                CalculateObservedContributionForTest(
                    observation.CardsRequestedWithoutPollinousCore,
                    observation.MaximumContribution,
                    totalCardsDrawn);

            RunTracker.RecordPollinousCoreDrawResult(
                observation.MaximumContribution,
                cardsDrawnBecauseOfPollinousCore);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"PollinousCoreCardPileDrawStatsPatch draw observation failed: {e.Message}");
        }
    }

    internal static int CalculateObservedContributionForTest(
        int cardsRequestedWithoutPollinousCore,
        int maximumContribution,
        int totalCardsDrawn)
    {
        return Math.Min(
            Math.Max(0, maximumContribution),
            Math.Max(
                0,
                totalCardsDrawn - Math.Max(0, cardsRequestedWithoutPollinousCore)));
    }
}

public sealed record PollinousCoreDrawObservation(
    int CardsRequestedWithoutPollinousCore,
    int MaximumContribution);

/// <summary>
/// Samples the relic's live 0-3 counter at the end of each owner turn.
/// </summary>
[HarmonyPatch]
public static class HookBeforeSideTurnEndPollinousCoreStatsPatch
{
    private static MethodBase? TargetMethod()
    {
        var hookType = Sts2CoreAssembly()?.GetType(
            "MegaCrit.Sts2.Core.Hooks.Hook",
            throwOnError: false);
        if (hookType == null) return null;

        return AccessTools.Method(hookType, "BeforeSideTurnEnd")
            ?? AccessTools.Method(hookType, "BeforeTurnEnd");
    }

    private static Assembly? Sts2CoreAssembly()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().Name == "sts2") return assembly;
        }

        return null;
    }

    private static bool Prepare() => TargetMethod() != null;

    [HarmonyPrefix]
    public static void Prefix(
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        try
        {
            if (side != CombatSide.Player) return;
            RunTracker.RecordPollinousCoreTurnEnded(participants);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"HookBeforeSideTurnEndPollinousCoreStatsPatch failed: {e.Message}");
        }
    }
}
