using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Snapshots the cards Pael's Claw actually enchanted after its pickup effect
/// has completed.
/// </summary>
[HarmonyPatch(typeof(PaelsClaw), nameof(PaelsClaw.AfterObtained))]
public static class PaelsClawAfterObtainedStatsPatch
{
    [HarmonyPostfix]
    public static void Postfix(PaelsClaw __instance, Task __result)
    {
        try
        {
            if (__instance?.Owner == null) return;

            if (__result == null)
            {
                RunTracker.RecordPaelsClawObtained(__instance);
                return;
            }

            if (__result.IsCompleted)
            {
                if (__result.IsCompletedSuccessfully)
                    RunTracker.RecordPaelsClawObtained(__instance);
                return;
            }

            __result.ContinueWith(
                task =>
                {
                    if (task.IsCompletedSuccessfully)
                        RunTracker.RecordPaelsClawObtained(__instance);
                },
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"PaelsClawAfterObtainedStatsPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Observes the permanent Goopy amount around Goopy's own post-play callback.
/// This records earned enhancement increments separately from finished card
/// plays because the game skips the post-play hook when combat has ended.
/// </summary>
[HarmonyPatch(typeof(Goopy), nameof(Goopy.AfterCardPlayed))]
public static class GoopyAfterCardPlayedPaelsClawStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        Goopy __instance,
        CardPlay cardPlay,
        out GoopyEnhancementState __state)
    {
        __state = default;

        try
        {
            if (__instance == null || !__instance.HasCard || cardPlay?.Card == null) return;
            if (!ReferenceEquals(__instance.Card, cardPlay.Card)) return;

            __state = new GoopyEnhancementState(
                cardPlay.Card,
                PermanentGoopyAmount(cardPlay.Card));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"GoopyAfterCardPlayedPaelsClawStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(GoopyEnhancementState __state, Task __result)
    {
        try
        {
            if (__state.Card == null) return;

            if (__result == null)
            {
                Observe(__state);
                return;
            }

            if (__result.IsCompleted)
            {
                if (__result.IsCompletedSuccessfully)
                    Observe(__state);
                return;
            }

            __result.ContinueWith(
                task =>
                {
                    if (task.IsCompletedSuccessfully)
                        Observe(__state);
                },
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"GoopyAfterCardPlayedPaelsClawStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static void Observe(GoopyEnhancementState state)
    {
        try
        {
            var card = state.Card;
            if (card == null) return;

            RunTracker.RecordPaelsClawGoopyEnhancement(
                card,
                state.StartingGoopyAmount,
                PermanentGoopyAmount(card));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"GoopyAfterCardPlayedPaelsClawStatsPatch.Observe failed: {e.Message}");
        }
    }

    internal static int PermanentGoopyAmount(CardModel card)
    {
        try
        {
            var permanentCard = card?.DeckVersion ?? card;
            return permanentCard?.Enchantment is Goopy goopy
                ? Math.Max(0, goopy.Amount)
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    public readonly record struct GoopyEnhancementState(
        CardModel? Card,
        int StartingGoopyAmount);
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
public static class HookAfterPlayerTurnStartPaelsClawPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player)
    {
        try
        {
            RunTracker.RecordPaelsClawTurnStarted(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookAfterPlayerTurnStartPaelsClawPatch failed: {e.Message}");
        }
    }
}
