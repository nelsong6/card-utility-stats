using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Tri-Boomerang selects permanent-deck cards asynchronously, then applies
/// Instinct to those same objects before AfterObtained completes. Compare
/// their Instinct amounts across that exact owner-specific callback so only
/// observed enchantment changes enter the persistent card ledger.
/// </summary>
[HarmonyPatch(typeof(TriBoomerang), nameof(TriBoomerang.AfterObtained))]
public static class TriBoomerangStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        TriBoomerang __instance,
        out TriBoomerangInstinctState? __state)
    {
        __state = null;

        try
        {
            if (__instance?.Owner == null || !RunTracker.IsTrackedRelic(__instance))
                return;

            var instinctAmountsBefore = new Dictionary<CardModel, decimal?>(
                ReferenceEqualityComparer.Instance);
            foreach (var card in __instance.Owner.Deck.Cards)
            {
                if (card == null) continue;
                instinctAmountsBefore[card] = card.Enchantment is Instinct instinct
                    ? instinct.Amount
                    : null;
            }

            __state = new TriBoomerangInstinctState(
                __instance,
                instinctAmountsBefore);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"TriBoomerangStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(
        ref Task __result,
        TriBoomerangInstinctState? __state)
    {
        try
        {
            if (__result == null || __state == null) return;
            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"TriBoomerangStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(
        Task inner,
        TriBoomerangInstinctState state)
    {
        await inner;

        try
        {
            var changedCards = state.InstinctAmountsBefore
                .Where(pair =>
                    pair.Key.Enchantment is Instinct instinct
                    && (!pair.Value.HasValue
                        || instinct.Amount != pair.Value.Value))
                .Select(pair => pair.Key)
                .ToList();

            RunTracker.RecordTriBoomerangInstinctCards(
                state.Relic,
                changedCards);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"TriBoomerangStatsPatch.ObserveAsync failed: {e.Message}");
        }
    }
}

public sealed record TriBoomerangInstinctState(
    TriBoomerang Relic,
    IReadOnlyDictionary<CardModel, decimal?> InstinctAmountsBefore);
