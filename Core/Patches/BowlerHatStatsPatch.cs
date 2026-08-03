using System;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Measures every observed run-level gold gain and Bowler Hat's share at the
/// same centralized GainGold command the game modifies. The completed owner-
/// balance delta captures integer truncation and gold prevention.
/// </summary>
[HarmonyPatch(
    typeof(PlayerCmd),
    nameof(PlayerCmd.GainGold),
    new[] { typeof(decimal), typeof(Player), typeof(bool) })]
public static class BowlerHatGainGoldStatsPatch
{
    [HarmonyPrefix]
    private static void Prefix(
        decimal amount,
        Player player,
        out BowlerHatGoldGainState __state)
    {
        __state = default;

        try
        {
            if (amount <= 0m || player == null) return;

            var relic = player.Relics?
                .OfType<BowlerHat>()
                .FirstOrDefault(candidate => !candidate.IsMelted);
            if (relic != null && !RunTracker.IsTrackedRelic(relic))
                relic = null;

            __state = new BowlerHatGoldGainState(
                relic,
                player,
                player.Gold,
                amount,
                RunTracker.CaptureGoldObservationContext(player));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"BowlerHatGainGoldStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    private static void Postfix(
        ref Task __result,
        BowlerHatGoldGainState __state)
    {
        try
        {
            if (__result == null
                || __state.Owner == null)
            {
                return;
            }

            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"BowlerHatGainGoldStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(
        Task inner,
        BowlerHatGoldGainState state)
    {
        await inner.ConfigureAwait(false);

        try
        {
            RunTracker.RecordRunGoldGain(
                state.Owner!,
                state.InitialGold,
                state.Owner!.Gold,
                state.Context);
            if (state.Relic != null)
            {
                RunTracker.RecordBowlerHatGoldGain(
                    state.Relic,
                    state.Owner!,
                    state.InitialGold,
                    state.Owner!.Gold,
                    state.UnmodifiedAmount);
            }
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"BowlerHatGainGoldStatsPatch.ObserveAsync failed: {e.Message}");
        }
    }

    internal readonly record struct BowlerHatGoldGainState(
        BowlerHat? Relic,
        Player? Owner,
        int InitialGold,
        decimal UnmodifiedAmount,
        GoldObservationContext Context);
}
