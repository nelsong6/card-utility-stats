using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Tracks Pael's Eye by observing which hand cards actually end in Exhaust
/// after its no-cards-played extra-turn callback resolves.
/// </summary>
[HarmonyPatch(typeof(PaelsEye), nameof(PaelsEye.BeforeSideTurnEndEarly))]
public static class PaelsEyeBeforeSideTurnEndEarlyStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        PaelsEye __instance,
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants,
        out PaelsEyeActivationSnapshot? __state)
    {
        __state = null;

        try
        {
            if (side != CombatSide.Player) return;
            if (__instance == null || !RunTracker.IsTrackedRelic(__instance)) return;

            var owner = __instance.Owner;
            var ownerCreature = owner?.Creature;
            if (owner == null || ownerCreature == null) return;
            if (participants == null || !participants.Contains(ownerCreature)) return;
            if (!__instance.ShouldTakeExtraTurn(owner)) return;

            var cards = owner.PlayerCombatState?.Hand?.Cards?
                .Where(card => card != null)
                .Select(card => new PaelsEyeCardSnapshot(
                    card,
                    card.Type,
                    card.IsBasicStrikeOrDefend))
                .ToList() ?? new List<PaelsEyeCardSnapshot>();

            RunTracker.NotePaelsEyeActivationStarted(owner);
            __state = new PaelsEyeActivationSnapshot(
                cards,
                owner.PlayerCombatState?.TurnNumber ?? 0);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"PaelsEyeBeforeSideTurnEndEarlyStatsPatch.Prefix failed: {e.Message}");
            __state = null;
        }
    }

    [HarmonyPostfix]
    public static void Postfix(PaelsEyeActivationSnapshot? __state, Task __result)
    {
        try
        {
            if (__state == null) return;

            if (__result == null)
            {
                Complete(__state);
                return;
            }

            if (__result.IsCompleted)
            {
                if (!__result.IsCanceled && !__result.IsFaulted)
                    Complete(__state);
                return;
            }

            __result.ContinueWith(
                task =>
                {
                    if (!task.IsCanceled && !task.IsFaulted)
                        Complete(__state);
                },
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"PaelsEyeBeforeSideTurnEndEarlyStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static void Complete(PaelsEyeActivationSnapshot snapshot)
    {
        try
        {
            var total = 0;
            var strikesAndDefends = 0;
            var statuses = 0;
            var curses = 0;

            foreach (var cardSnapshot in snapshot.Cards)
            {
                if (cardSnapshot.Card?.Pile?.Type != PileType.Exhaust) continue;

                total += 1;
                if (cardSnapshot.IsBasicStrikeOrDefend)
                    strikesAndDefends += 1;

                switch (cardSnapshot.Type)
                {
                    case CardType.Status:
                        statuses += 1;
                        break;
                    case CardType.Curse:
                        curses += 1;
                        break;
                }
            }

            RunTracker.RecordPaelsEyeActivation(
                total,
                strikesAndDefends,
                statuses,
                curses,
                snapshot.TurnNumber);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"PaelsEyeBeforeSideTurnEndEarlyStatsPatch.Complete failed: {e.Message}");
        }
    }

    public sealed record PaelsEyeActivationSnapshot(
        IReadOnlyList<PaelsEyeCardSnapshot> Cards,
        int TurnNumber);

    public sealed record PaelsEyeCardSnapshot(
        CardModel Card,
        CardType Type,
        bool IsBasicStrikeOrDefend);
}
