using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Tuning Fork when its owner-owned Skill counter is about to cross
/// the relic threshold. The block amount itself is observed later by
/// Hook.AfterBlockGained, after the game applies the block command.
/// </summary>
[HarmonyPatch(typeof(TuningFork), nameof(TuningFork.AfterCardPlayed))]
public static class TuningForkAfterCardPlayedPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        TuningFork __instance,
        PlayerChoiceContext choiceContext,
        CardPlay cardPlay,
        out bool __state)
    {
        __state = false;

        try
        {
            if (!ShouldArm(__instance, cardPlay)) return;
            RunTracker.RecordTuningForkActivationAndArmBlockAttribution();
            __state = true;
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"TuningForkAfterCardPlayedPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(bool __state, Task __result)
    {
        try
        {
            if (!__state) return;
            if (__result == null)
            {
                RunTracker.DisarmTuningForkBlockAttribution();
                return;
            }

            __result.ContinueWith(
                _ => RunTracker.DisarmTuningForkBlockAttribution(),
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"TuningForkAfterCardPlayedPatch.Postfix failed: {e.Message}");
        }
    }

    private static bool ShouldArm(TuningFork relic, CardPlay cardPlay)
    {
        if (relic?.Owner == null || cardPlay?.Card == null) return false;
        if (!RunTracker.IsTrackedRelic(relic)) return false;
        if (!CombatManager.Instance.IsInProgress) return false;
        if (!ReferenceEquals(cardPlay.Card.Owner, relic.Owner)) return false;
        if (cardPlay.Card.Type != CardType.Skill) return false;

        return relic.SkillsPlayed + 1 >= relic.SkillsThreshold;
    }
}
