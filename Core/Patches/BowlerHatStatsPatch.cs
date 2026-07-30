using System;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Measures Bowler Hat at the same centralized GainGold command the game
/// modifies. The completed owner-balance delta captures integer truncation and
/// gold prevention; subtracting the unmodified integer grant leaves only gold
/// that actually reached the player because of Bowler Hat.
/// </summary>
[HarmonyPatch(
    typeof(PlayerCmd),
    nameof(PlayerCmd.GainGold),
    new[] { typeof(decimal), typeof(Player), typeof(bool) })]
public static class BowlerHatGainGoldStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
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
            if (relic == null || !RunTracker.IsTrackedRelic(relic)) return;

            __state = new BowlerHatGoldGainState(
                relic,
                player,
                player.Gold,
                amount);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"BowlerHatGainGoldStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(
        ref Task __result,
        BowlerHatGoldGainState __state)
    {
        try
        {
            if (__result == null
                || __state.Relic == null
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
            RunTracker.RecordBowlerHatGoldGain(
                state.Relic!,
                state.Owner!,
                state.InitialGold,
                state.Owner!.Gold,
                state.UnmodifiedAmount);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"BowlerHatGainGoldStatsPatch.ObserveAsync failed: {e.Message}");
        }
    }

    public readonly record struct BowlerHatGoldGainState(
        BowlerHat? Relic,
        Player? Owner,
        int InitialGold,
        decimal UnmodifiedAmount);
}
