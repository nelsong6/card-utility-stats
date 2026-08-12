using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace SpireLens.Core;

/// <summary>
/// Restarts the combat the player is currently in, from the state it began with.
///
/// This needs no snapshot of its own. Slay the Spire 2's run save is already a
/// room-boundary snapshot: <c>RunManager.EnterMapPointInternal</c> writes
/// <c>SaveRun(null)</c> on map-node entry — before the room type is even rolled —
/// and the only other combat-path save is at victory, with the room marked
/// pre-finished. So for the whole duration of a fight, <c>current_run.save</c> on
/// disk IS that fight's opening state, RNG included. Restarting is just replaying
/// the save the game already wrote.
///
/// The load sequence mirrors the main menu's Continue button
/// (<c>NMainMenu</c>: FromSerializable → SetUpSavedSingleplayer → LoadRun),
/// with <c>RunManager.CleanUp()</c> inserted first because
/// <c>SetUpSavedSingleplayer</c> throws when <c>RunManager.State</c> is non-null.
/// <c>CleanUp</c> is the same teardown Save and Quit / Abandon use: it sets
/// <c>ShouldSave = false</c> (so the half-played fight can never overwrite the
/// save), calls <c>CombatManager.Reset(graceful)</c>, and nulls <c>State</c>.
/// It deliberately does NOT call <c>RunManager.OnEnded</c>, so no run outcome is
/// stamped and no <c>RunEnded</c> fires.
///
/// SpireLens tracking needs no special handling: this is the Continue path, so
/// <c>RunStarted</c> re-fires with the same <c>_startTime</c> and
/// <c>RunTracker.OnRunStarted</c> adopts the existing run record — which already
/// discards <c>_pendingCombat</c>, so the abandoned half-fight's stats are not
/// promoted and the replayed fight is counted once.
/// </summary>
public static class CombatResetter
{
    private static bool _restartInProgress;

    /// <summary>
    /// Null when a restart can run right now; otherwise a short player-facing
    /// reason why it cannot. Read on every menu open, so the row explains
    /// itself rather than silently doing nothing.
    /// </summary>
    public static string? BlockedReason()
    {
        try
        {
            if (_restartInProgress) return "already restarting";

            var run = RunManager.Instance;
            if (run == null || !run.IsInProgress) return "no run in progress";
            if (run.IsCleaningUp) return "run is shutting down";
            if (CombatManager.Instance?.IsInProgress != true) return "not in combat";

            // SetUpSavedSingleplayer is singleplayer-only; the multiplayer
            // equivalent needs a LoadRunLobby we have no way to rebuild here.
            if (run.NetService == null || run.NetService.Type != NetGameType.Singleplayer)
                return "singleplayer only";

            if (SaveManager.Instance?.HasRunSave != true) return "no run save on disk";

            return null;
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"CombatResetter: availability check failed: {e}");
            return "unavailable";
        }
    }

    public static bool CanRestart => BlockedReason() == null;

    /// <summary>
    /// Fire-and-forget entry point. Returns false without touching the live run
    /// if a restart is not currently possible.
    /// </summary>
    public static bool Request(string source)
    {
        var blocked = BlockedReason();
        if (blocked != null)
        {
            CoreMain.Logger.Info($"CombatResetter: restart refused ({blocked}, source={source})");
            return false;
        }

        _restartInProgress = true;
        TaskHelper.RunSafely(RestartAsync(source));
        return true;
    }

    private static async Task RestartAsync(string source)
    {
        try
        {
            // Read and deserialize BEFORE any teardown. A missing or corrupt
            // save must abort with the live run untouched, not after we have
            // already destroyed it.
            var read = SaveManager.Instance.LoadRunSave();
            if (!read.Success || read.SaveData == null)
            {
                CoreMain.Logger.Error(
                    $"CombatResetter: cannot read run save ({read.Status} {read.ErrorMessage}); live run left alone");
                return;
            }

            var save = read.SaveData;
            var runState = RunState.FromSerializable(save);

            var game = NGame.Instance;
            if (game == null)
            {
                CoreMain.Logger.Error("CombatResetter: NGame.Instance is null; live run left alone");
                return;
            }

            CoreMain.Logger.Info(
                $"CombatResetter: restarting combat from run save (source={source}, " +
                $"floor={save.MapPointHistory?.Count}, pre_finished_room={save.PreFinishedRoom?.RoomType.ToString() ?? "none"})");

            NAudioManager.Instance?.StopMusic();
            await game.Transition.FadeOut();

            // Frees RunManager.State so SetUpSavedSingleplayer will accept the
            // reloaded state, and suppresses any further save of the abandoned
            // fight on the way out.
            RunManager.Instance.CleanUp();

            await RunManager.Instance.SetUpSavedSingleplayer(runState, save);
            game.ReactionContainer.InitializeNetworking(new NetSingleplayerGameService());
            await game.LoadRun(runState, save.PreFinishedRoom);
            await game.Transition.FadeIn();

            CoreMain.Logger.Info("CombatResetter: combat restart complete");
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"CombatResetter: combat restart failed: {e}");
        }
        finally
        {
            _restartInProgress = false;
        }
    }
}
