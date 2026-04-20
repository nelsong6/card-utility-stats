using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;

namespace CardUtilityStats.CardUtilityStatsCode.Patches;

/// <summary>
/// Hooks the game's run-end finalization to stamp our Outcome + EndedAt.
///
/// <c>RunManager.OnEnded(bool isVictory)</c> is the canonical run-end path
/// in the game. It:
///   - is called exactly once per run (guarded by _runHistoryWasUploaded)
///   - receives isVictory (true iff cleared final act boss)
///   - runs after IsAbandoned has been set (abandon flows set it before
///     calling OnEnded transitively)
///
/// Postfix runs after the game's own finalization — safe place to capture
/// the outcome. We translate (isVictory, IsAbandoned) into the canonical
/// three-way outcome tag.
/// </summary>
[HarmonyPatch(typeof(RunManager), nameof(RunManager.OnEnded))]
public static class RunManagerOnEndedPatch
{
    [HarmonyPostfix]
    public static void Postfix(bool isVictory)
    {
        // IsAbandoned takes precedence: a user who abandons mid-fight is neither
        // "won" nor "lost" in the usual sense. The game's own run history saves
        // the abandoned flag separately for the same reason.
        string outcome;
        if (RunManager.Instance.IsAbandoned) outcome = "abandoned";
        else if (isVictory) outcome = "win";
        else outcome = "loss";

        RunTracker.OnRunEnded(outcome);
    }
}
