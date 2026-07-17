using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Mirrors Intimidating Helmet's owner and play-time EnergyValue condition,
/// counts the qualifying card play, and arms its immediately following block
/// command for observed-result attribution.
/// </summary>
[HarmonyPatch(typeof(IntimidatingHelmet), nameof(IntimidatingHelmet.BeforeCardPlayed))]
public static class IntimidatingHelmetBeforeCardPlayedStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        IntimidatingHelmet __instance,
        CardPlay cardPlay,
        out Creature? __state)
    {
        __state = null;

        try
        {
            RunTracker.RecordIntimidatingHelmetCardPlayedAndArmBlockAttribution(
                __instance,
                cardPlay,
                out __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"IntimidatingHelmetBeforeCardPlayedStatsPatch.Prefix failed: {e.Message}");
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
                RunTracker.DisarmIntimidatingHelmetBlockAttribution(__state);
                return;
            }

            __result.ContinueWith(
                _ => RunTracker.DisarmIntimidatingHelmetBlockAttribution(__state),
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"IntimidatingHelmetBeforeCardPlayedStatsPatch.Postfix failed: {e.Message}");
        }
    }
}

/// <summary>
/// Consumes only the gain-block command immediately armed by Intimidating
/// Helmet and records the command's returned post-modifier block amount.
/// </summary>
[HarmonyPatch(
    typeof(CreatureCmd),
    nameof(CreatureCmd.GainBlock),
    new[] { typeof(Creature), typeof(BlockVar), typeof(CardPlay), typeof(bool) })]
public static class IntimidatingHelmetCreatureGainBlockStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Creature creature, out bool __state)
    {
        __state = false;

        try
        {
            __state = RunTracker.TryConsumeIntimidatingHelmetBlockAttribution(creature);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"IntimidatingHelmetCreatureGainBlockStatsPatch.Prefix failed: {e.Message}");
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
            RunTracker.RecordIntimidatingHelmetBlockGained(gained);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"IntimidatingHelmetCreatureGainBlockStatsPatch block observation failed: {e.Message}");
        }
    }
}

/// <summary>
/// Counts every distinct player turn while Intimidating Helmet is held so
/// zero-trigger turns remain part of the average-block denominator.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
public static class HookAfterPlayerTurnStartIntimidatingHelmetPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player)
    {
        try
        {
            RunTracker.RecordIntimidatingHelmetTurnStarted(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookAfterPlayerTurnStartIntimidatingHelmetPatch failed: {e.Message}");
        }
    }
}
