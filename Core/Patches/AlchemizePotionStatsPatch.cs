using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Models;

namespace SpireLens.Core.Patches;

public readonly record struct PotionProcurementStatsSource(
    CardModel? AlchemizeCard,
    bool IsPetrifiedToad);

/// <summary>
/// Observes the exact potion-procurement result used by Alchemize and Petrified
/// Toad. Their callers ignore this return value, so capture the pending source
/// before the async command and wrap the returned task to record the observed
/// success or failure before the action finishes.
/// </summary>
[HarmonyPatch(
    typeof(PotionCmd),
    nameof(PotionCmd.TryToProcure),
    new[] { typeof(PotionModel), typeof(Player), typeof(int) })]
public static class AlchemizePotionStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        PotionModel potion,
        Player player,
        out PotionProcurementStatsSource __state)
    {
        __state = new PotionProcurementStatsSource(
            RunTracker.CaptureAlchemizePotionSource(player),
            RunTracker.TryCapturePetrifiedToadPotionProcurement(player, potion));
    }

    [HarmonyPostfix]
    public static void Postfix(
        Player player,
        PotionProcurementStatsSource __state,
        ref Task<PotionProcureResult> __result)
    {
        try
        {
            if ((__state.AlchemizeCard == null && !__state.IsPetrifiedToad)
                || __result == null)
            {
                return;
            }
            __result = ObserveAsync(__result, __state, player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"AlchemizePotionStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task<PotionProcureResult> ObserveAsync(
        Task<PotionProcureResult> inner,
        PotionProcurementStatsSource source,
        Player player)
    {
        var result = await inner.ConfigureAwait(false);
        try
        {
            if (source.AlchemizeCard != null)
                RunTracker.RecordAlchemizePotionResult(source.AlchemizeCard, player, result);
            if (source.IsPetrifiedToad)
                RunTracker.RecordPetrifiedToadPotionResult(player, result);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"AlchemizePotionStatsPatch.ObserveAsync failed: {e.Message}");
        }

        return result;
    }
}
