using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Mirrors Daughter of the Wind's owner-Attack condition and arms only the
/// gain-block command issued by that exact relic callback.
/// </summary>
[HarmonyPatch(typeof(DaughterOfTheWind), nameof(DaughterOfTheWind.AfterCardPlayed))]
public static class DaughterOfTheWindAfterCardPlayedStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        DaughterOfTheWind __instance,
        CardPlay cardPlay,
        out Creature? __state)
    {
        __state = null;

        try
        {
            RunTracker.ArmDaughterOfTheWindBlockAttribution(
                __instance,
                cardPlay,
                out __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"DaughterOfTheWindAfterCardPlayedStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(Creature? __state, Task __result)
    {
        if (__state == null) return;

        try
        {
            if (__result == null || __result.IsCompleted)
            {
                RunTracker.DisarmDaughterOfTheWindBlockAttribution(__state);
                return;
            }

            __result.ContinueWith(
                _ => RunTracker.DisarmDaughterOfTheWindBlockAttribution(__state),
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"DaughterOfTheWindAfterCardPlayedStatsPatch.Postfix failed: {e.Message}");
        }
    }
}

/// <summary>
/// Records the post-modifier block amount returned by the exact GainBlock
/// command armed from Daughter of the Wind's callback.
/// </summary>
[HarmonyPatch(
    typeof(CreatureCmd),
    nameof(CreatureCmd.GainBlock),
    new[] { typeof(Creature), typeof(BlockVar), typeof(CardPlay), typeof(bool) })]
public static class DaughterOfTheWindCreatureGainBlockStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Creature creature, out bool __state)
    {
        __state = false;

        try
        {
            __state = RunTracker.TryConsumeDaughterOfTheWindBlockAttribution(creature);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"DaughterOfTheWindCreatureGainBlockStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(bool __state, Task<decimal> __result)
    {
        if (!__state || __result == null) return;
        ObserveBlockResultAsync(__result);
    }

    private static async void ObserveBlockResultAsync(Task<decimal> blockTask)
    {
        try
        {
            var gained = await blockTask.ConfigureAwait(false);
            RunTracker.RecordDaughterOfTheWindBlockGained(gained);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"DaughterOfTheWindCreatureGainBlockStatsPatch block observation failed: {e.Message}");
        }
    }
}

/// <summary>
/// Counts every distinct player turn while Daughter of the Wind is held so
/// turns with no owner Attack remain in the average denominator.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
public static class HookAfterPlayerTurnStartDaughterOfTheWindPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player)
    {
        try
        {
            RunTracker.RecordDaughterOfTheWindTurnStarted(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookAfterPlayerTurnStartDaughterOfTheWindPatch failed: {e.Message}");
        }
    }
}
