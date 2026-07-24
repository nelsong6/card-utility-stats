using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;

namespace SpireLens.Core.Patches;

/// <summary>
/// Observes Book of Five Rings at its owner-specific permanent-deck callback.
/// The game's saved CardsAdded counter determines the exact fifth-card trigger,
/// while SpireLens's shared healing ledger measures the completed heal.
/// </summary>
[HarmonyPatch(typeof(BookOfFiveRings), nameof(BookOfFiveRings.AfterCardChangedPiles))]
public static class BookOfFiveRingsStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        BookOfFiveRings __instance,
        CardModel card,
        out BookOfFiveRingsState __state)
    {
        __state = default;

        try
        {
            if (__instance?.Owner?.Creature == null) return;
            if (!RunTracker.IsTrackedRelic(__instance)) return;
            if (__instance.Owner.Creature.IsDead) return;
            if (card?.Owner != __instance.Owner || card.Pile?.Type != PileType.Deck) return;

            var cardsPerTrigger = __instance.DynamicVars?.Cards?.IntValue ?? 0;
            var triggered = cardsPerTrigger > 0
                && (__instance.CardsAdded + 1) % cardsPerTrigger == 0;
            var attemptedHealing = triggered
                ? __instance.DynamicVars?.Heal?.BaseValue ?? 0m
                : 0m;

            RunTracker.RecordBookOfFiveRingsCardAdded(
                __instance,
                triggered,
                attemptedHealing);

            if (triggered)
                __state = new BookOfFiveRingsState(__instance.Owner.Creature, true);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BookOfFiveRingsStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(ref Task __result, BookOfFiveRingsState __state)
    {
        try
        {
            if (!__state.Triggered || __state.HealedCreature == null) return;

            if (__result == null)
            {
                RunTracker.FinalizeRelicHealing(
                    __state.HealedCreature,
                    "RELIC.BOOK_OF_FIVE_RINGS");
                return;
            }

            __result = ObserveAsync(__result, __state.HealedCreature);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BookOfFiveRingsStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(Task inner, Creature healedCreature)
    {
        try
        {
            await inner;
        }
        finally
        {
            RunTracker.FinalizeRelicHealing(
                healedCreature,
                "RELIC.BOOK_OF_FIVE_RINGS");
        }
    }

    public readonly record struct BookOfFiveRingsState(
        Creature? HealedCreature,
        bool Triggered);
}

/// <summary>
/// Counts completed outer card-reward skips while Book of Five Rings is held.
/// The inner card-selection Skip does not call this reward boundary.
/// </summary>
[HarmonyPatch(typeof(CardReward), nameof(CardReward.OnSkipped))]
public static class CardRewardBookOfFiveRingsOnSkippedPatch
{
    [HarmonyPostfix]
    public static void Postfix(CardReward __instance)
    {
        try
        {
            RunTracker.RecordBookOfFiveRingsCardRewardSkipped(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CardRewardBookOfFiveRingsOnSkippedPatch failed: {e.Message}");
        }
    }
}
