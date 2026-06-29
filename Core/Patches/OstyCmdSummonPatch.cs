using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace SpireLens.Core.Patches;

/// <summary>
/// Captures successful Osty summons from the shared command path. The command
/// carries its source model and returns the game-observed amount, so this
/// avoids guessing from card text or play count.
/// </summary>
[HarmonyPatch(typeof(OstyCmd), nameof(OstyCmd.Summon))]
public static class OstyCmdSummonPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        ref Task<SummonResult> __result,
        PlayerChoiceContext choiceContext,
        Player summoner,
        decimal amount,
        AbstractModel source)
    {
        try
        {
            if (__result == null || summoner == null)
                return;

            __result = ObserveSummonAsync(__result, summoner, source);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"OstyCmdSummonPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task<SummonResult> ObserveSummonAsync(
        Task<SummonResult> inner,
        Player player,
        AbstractModel source)
    {
        var result = await inner.ConfigureAwait(false);
        try
        {
            if (result == null) return result!;

            if (source is MegaCrit.Sts2.Core.Models.CardModel sourceCard && result.Amount > 0m)
                RunTracker.RecordOstySummoned(player, sourceCard, result.Creature, result.Amount);
            else
                RunTracker.RecordRelicOstySummon(source, result.Amount);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"OstyCmdSummonPatch.ObserveSummonAsync failed: {e.Message}");
        }

        return result!;
    }
}
