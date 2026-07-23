using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Observes Lucky Fysh at its owner-specific permanent-deck callback. The
/// callback awaits its exact gold command, so the completed owner balance
/// reveals the actual modified gain while the matched callback confirms one
/// card was successfully added to the permanent deck.
/// </summary>
[HarmonyPatch(typeof(LuckyFysh), nameof(LuckyFysh.AfterCardChangedPiles))]
public static class LuckyFyshStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        LuckyFysh __instance,
        CardModel card,
        out LuckyFyshState __state)
    {
        __state = default;

        try
        {
            if (__instance == null || !RunTracker.IsTrackedRelic(__instance)) return;
            if (card?.Pile?.Type != PileType.Deck) return;

            var owner = __instance.Owner;
            if (owner == null || card.Owner != owner) return;

            __state = new LuckyFyshState(__instance, owner, owner.Gold);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"LuckyFyshStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(ref Task __result, LuckyFyshState __state)
    {
        try
        {
            if (__result == null || __state.Relic == null || __state.Owner == null) return;
            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"LuckyFyshStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(Task inner, LuckyFyshState state)
    {
        await inner.ConfigureAwait(false);

        try
        {
            RunTracker.RecordLuckyFyshCardAdded(
                state.Relic!,
                state.Owner!,
                state.InitialGold,
                state.Owner!.Gold);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"LuckyFyshStatsPatch.ObserveAsync failed: {e.Message}");
        }
    }

    public readonly record struct LuckyFyshState(
        LuckyFysh? Relic,
        Player? Owner,
        int InitialGold);
}
