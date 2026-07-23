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
/// Gnarled Hammer selects permanent-deck cards asynchronously, then applies
/// Sharp to those same physical CardModel objects before AfterObtained
/// completes. Compare their Sharp amounts across that exact owner-specific
/// callback so only observed enchantment changes are recorded.
/// </summary>
[HarmonyPatch(typeof(GnarledHammer), nameof(GnarledHammer.AfterObtained))]
public static class GnarledHammerStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        GnarledHammer __instance,
        out GnarledHammerSharpState? __state)
    {
        __state = null;

        try
        {
            if (__instance?.Owner == null || !RunTracker.IsTrackedRelic(__instance))
                return;

            var sharpAmountsBefore = new Dictionary<CardModel, decimal?>(
                ReferenceEqualityComparer.Instance);
            foreach (var card in __instance.Owner.Deck.Cards)
            {
                if (card == null) continue;
                sharpAmountsBefore[card] = card.Enchantment is Sharp sharp
                    ? sharp.Amount
                    : null;
            }

            __state = new GnarledHammerSharpState(__instance, sharpAmountsBefore);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"GnarledHammerStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(
        ref Task __result,
        GnarledHammerSharpState? __state)
    {
        try
        {
            if (__result == null || __state == null) return;
            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"GnarledHammerStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(Task inner, GnarledHammerSharpState state)
    {
        await inner;

        try
        {
            var changedCards = state.SharpAmountsBefore
                .Where(pair =>
                    pair.Key.Enchantment is Sharp sharp
                    && (!pair.Value.HasValue || sharp.Amount != pair.Value.Value))
                .Select(pair => pair.Key)
                .ToList();

            RunTracker.RecordGnarledHammerSharpCards(state.Relic, changedCards);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"GnarledHammerStatsPatch.ObserveAsync failed: {e.Message}");
        }
    }
}

public sealed record GnarledHammerSharpState(
    GnarledHammer Relic,
    IReadOnlyDictionary<CardModel, decimal?> SharpAmountsBefore);
