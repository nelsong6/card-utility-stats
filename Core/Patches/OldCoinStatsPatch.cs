using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Observes Old Coin's actual completed gold grant, then consumes that grant
/// from the run's FIFO gold-provenance ledger at the game's centralized
/// LoseGold boundary.
/// </summary>
[HarmonyPatch(typeof(OldCoin), nameof(OldCoin.AfterObtained))]
public static class OldCoinAfterObtainedStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(OldCoin __instance, out OldCoinState __state)
    {
        __state = default;

        try
        {
            if (__instance == null || !RunTracker.IsTrackedRelic(__instance)) return;

            var owner = __instance.Owner;
            if (owner == null) return;

            __state = new OldCoinState(__instance, owner, owner.Gold);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"OldCoinAfterObtainedStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(ref Task __result, OldCoinState __state)
    {
        try
        {
            if (__result == null || __state.Relic == null || __state.Owner == null) return;
            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"OldCoinAfterObtainedStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(Task inner, OldCoinState state)
    {
        await inner.ConfigureAwait(false);

        try
        {
            RunTracker.RecordOldCoinObtained(
                state.Relic!,
                state.Owner!,
                state.InitialGold,
                state.Owner!.Gold);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"OldCoinAfterObtainedStatsPatch.ObserveAsync failed: {e.Message}");
        }
    }

    public readonly record struct OldCoinState(
        OldCoin? Relic,
        Player? Owner,
        int InitialGold);
}

[HarmonyPatch(typeof(PlayerCmd), nameof(PlayerCmd.LoseGold))]
public static class OldCoinGoldLossStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player, out int __state)
    {
        __state = player?.Gold ?? 0;
    }

    [HarmonyPostfix]
    public static void Postfix(
        Player player,
        GoldLossType goldLossType,
        int __state)
    {
        try
        {
            if (player == null || player.Gold >= __state) return;
            RunTracker.RecordGoldLoss(player, __state, player.Gold, goldLossType);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"OldCoinGoldLossStatsPatch.Postfix failed: {e.Message}");
        }
    }
}
