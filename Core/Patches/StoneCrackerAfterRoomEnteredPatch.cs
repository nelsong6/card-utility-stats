using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;

namespace SpireLens.Core.Patches;

/// <summary>
/// Observes the exact draw-pile combat cards Stone Cracker upgrades. Its
/// callback performs every upgrade synchronously before its first await, so
/// Harmony's postfix can compare upgrade levels without guessing from the
/// relic's selection intent.
/// </summary>
[HarmonyPatch(typeof(StoneCracker), nameof(StoneCracker.AfterRoomEntered))]
public static class StoneCrackerAfterRoomEnteredPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        StoneCracker __instance,
        AbstractRoom room,
        out ActivationState __state)
    {
        __state = default;

        try
        {
            if (__instance?.Owner == null) return;
            if (room is not CombatRoom) return;

            var upgradeLevels = new Dictionary<CardModel, int>(
                ReferenceEqualityComparer.Instance);
            foreach (var card in PileType.Draw.GetPile(__instance.Owner).Cards)
            {
                if (card != null)
                    upgradeLevels[card] = card.CurrentUpgradeLevel;
            }

            __state = new ActivationState(__instance, upgradeLevels);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"StoneCrackerAfterRoomEnteredPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(ActivationState __state)
    {
        try
        {
            if (__state.Relic == null || __state.UpgradeLevels == null) return;

            var upgradedCards = new List<CardModel>();
            foreach (var (card, previousUpgradeLevel) in __state.UpgradeLevels)
            {
                if (card.CurrentUpgradeLevel > previousUpgradeLevel)
                    upgradedCards.Add(card);
            }

            RunTracker.RecordStoneCrackerActivation(__state.Relic, upgradedCards);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"StoneCrackerAfterRoomEnteredPatch.Postfix failed: {e.Message}");
        }
    }

    public readonly record struct ActivationState(
        StoneCracker? Relic,
        IReadOnlyDictionary<CardModel, int>? UpgradeLevels);
}

/// <summary>
/// Supplies Stone Cracker's zero-inclusive held-turn denominator.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
public static class HookAfterPlayerTurnStartStoneCrackerStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player)
    {
        try
        {
            RunTracker.RecordStoneCrackerTurnStarted(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookAfterPlayerTurnStartStoneCrackerStatsPatch failed: {e.Message}");
        }
    }
}
