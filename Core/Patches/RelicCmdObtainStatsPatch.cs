using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Stamps obtain-time stats for relics whose baseline depends on state at the
/// pickup boundary. The command path catches reward, shop, event, and direct
/// relic grants.
/// </summary>
[HarmonyPatch]
public static class RelicCmdObtainStatsPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(
            typeof(RelicCmd),
            nameof(RelicCmd.Obtain),
            new[] { typeof(RelicModel), typeof(Player), typeof(int) });
    }

    [HarmonyPrefix]
    public static void Prefix(RelicModel relic, Player player, out ChosenCheeseObtainState __state)
    {
        __state = default;

        try
        {
            if (!RunTracker.IsChosenCheeseStatsRelic(relic)) return;

            var creature = player?.Creature;
            if (creature == null) return;

            __state = new ChosenCheeseObtainState(player, creature.MaxHp);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RelicCmdObtainStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(
        RelicModel relic,
        Player player,
        Task<RelicModel> __result,
        ChosenCheeseObtainState __state)
    {
        try
        {
            if (!IsTrackedObtainStatsRelic(relic)) return;

            if (__result == null)
            {
                RecordObtainStats(relic, player, __state);
                return;
            }

            if (__result.IsCompleted)
            {
                if (!__result.IsCanceled && !__result.IsFaulted)
                    RecordObtainStats(__result.Result ?? relic, player, __state);
                return;
            }

            __result.ContinueWith(
                task =>
                {
                    if (!task.IsCanceled && !task.IsFaulted)
                        RecordObtainStats(task.Result ?? relic, player, __state);
                },
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RelicCmdObtainStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static bool IsTrackedObtainStatsRelic(RelicModel relic)
    {
        return RunTracker.IsStrikeDummyStatsRelic(relic)
            || RunTracker.IsMiniatureCannonStatsRelic(relic)
            || RunTracker.IsLizardTailStatsRelic(relic)
            || RunTracker.IsChosenCheeseStatsRelic(relic)
            || relic is BookOfFiveRings;
    }

    private static void RecordObtainStats(RelicModel relic, Player player, ChosenCheeseObtainState chosenCheeseState)
    {
        RunTracker.RecordStrikeDummyObtained(relic, player);
        RunTracker.RecordMiniatureCannonObtained(relic, player);
        RunTracker.RecordLizardTailObtained(relic, player);
        if (relic is BookOfFiveRings bookOfFiveRings)
            RunTracker.RecordBookOfFiveRingsObtained(bookOfFiveRings, player);

        if (chosenCheeseState.Player != null)
            RunTracker.RecordChosenCheeseObtained(relic, player, chosenCheeseState.StartingMaxHp);
    }

    public readonly record struct ChosenCheeseObtainState(Player? Player, decimal StartingMaxHp);
}
