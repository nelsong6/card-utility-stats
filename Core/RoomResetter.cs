using System;
using System.Diagnostics;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace SpireLens.Core;

/// <summary>
/// What a restart would replay right now. Exactly one of the two is non-null:
/// <see cref="RoomNoun"/> names the room a restart would replay ("combat",
/// "shop", "event"), or <see cref="BlockedReason"/> says why none can be.
/// </summary>
public readonly record struct RoomRestartAvailability(string? RoomNoun, string? BlockedReason)
{
    public bool CanRestart => BlockedReason == null;
}

/// <summary>
/// Restarts the room the player is currently in, from the state it began with.
///
/// This needs no snapshot of its own. Slay the Spire 2's run save is already a
/// room-boundary snapshot: <c>RunManager.EnterMapPointInternal</c> writes
/// <c>SaveRun(null)</c> on map-node entry — before the room type is even rolled —
/// and nothing is written while the player is inside a fight, a shop, or an
/// event. So for the duration of a room, <c>current_run.save</c> on disk IS that
/// room's opening state, RNG included; restarting is just replaying the save the
/// game already wrote. Because the RNG is restored too, the re-rolled room is
/// the same room, with the same encounter, the same merchant stock, or the same
/// event.
///
/// The two exceptions are the only places the game rewrites the save with a
/// pre-finished room: <c>CombatManager</c> at combat victory, and
/// <c>EventRoom.OnEventStateChanged</c> when an Ancient event finishes. Past
/// either point the save is the room's aftermath rather than its opening, so a
/// replay would undo nothing — <see cref="Describe"/> refuses instead.
///
/// The load sequence mirrors the main menu's Continue button
/// (<c>NMainMenu</c>: FromSerializable → SetUpSavedSingleplayer → LoadRun),
/// with <c>RunManager.CleanUp()</c> inserted first because
/// <c>SetUpSavedSingleplayer</c> throws when <c>RunManager.State</c> is non-null.
/// <c>CleanUp</c> is the same teardown Save and Quit / Abandon use: it sets
/// <c>ShouldSave = false</c> (so the abandoned attempt can never overwrite the
/// save), calls <c>CombatManager.Reset(graceful)</c>, and nulls <c>State</c>.
/// It deliberately does NOT call <c>RunManager.OnEnded</c>, so no run outcome is
/// stamped and no <c>RunEnded</c> fires.
///
/// SpireLens tracking needs no special handling for a restarted <i>combat</i>:
/// this is the Continue path, so <c>RunStarted</c> re-fires with the same
/// <c>_startTime</c> and <c>RunTracker.OnRunStarted</c> adopts the existing run
/// record — which already discards <c>_pendingCombat</c>, so the abandoned
/// half-fight's stats are not promoted and the replayed fight is counted once.
///
/// Shops and events are different, and deliberately so: their effects (gold
/// spent, cards bought, relics taken, HP traded) are committed to the run record
/// as they happen rather than buffered, so a restart rolls the game back but
/// leaves those entries behind as history. That is the same rollback the tracker
/// already tolerates when a crash or a save-and-quit rewinds the save past a
/// card acquisition: <c>AdoptRunLocked</c> re-binds instance numbers from the
/// live deck and drops the restores it can no longer match. Undoing the record
/// too would need a SpireLens-side room snapshot, which is exactly the machinery
/// this feature exists without.
/// </summary>
public static class RoomResetter
{
    // The game's own transitions default to 0.8s each, which is right for a
    // deliberate main-menu Continue and far too slow for a retry the player
    // wants to feel instant. These are not zero because the run scene is torn
    // down and rebuilt in between: an uncovered swap shows a frame or two of
    // half-destroyed scene. Short enough to read as a blink, long enough to
    // hide the rebuild.
    private const float FadeOutSeconds = 0.12f;
    private const float FadeInSeconds = 0.18f;

    private static bool _restartInProgress;

    /// <summary>
    /// Whether a restart can run right now and what it would replay. Read on
    /// every menu open, so the row explains itself rather than silently doing
    /// nothing.
    /// </summary>
    public static RoomRestartAvailability Describe()
    {
        try
        {
            if (_restartInProgress) return Blocked("already restarting");

            var run = RunManager.Instance;
            if (run == null || !run.IsInProgress) return Blocked("no run in progress");
            if (run.IsCleaningUp) return Blocked("run is shutting down");

            // SetUpSavedSingleplayer is singleplayer-only; the multiplayer
            // equivalent needs a LoadRunLobby we have no way to rebuild here.
            if (run.NetService == null || run.NetService.Type != NetGameType.Singleplayer)
                return Blocked("singleplayer only");

            if (SaveManager.Instance?.HasRunSave != true) return Blocked("no run save on disk");

            var state = run.State;

            // The base of the room stack, not the top: an event that started a
            // fight has the combat room on top, but the save replays the map
            // point, so what actually comes back is the event.
            var room = state?.BaseRoom;
            if (state == null || room == null) return Blocked("between rooms");

            bool combatRunning = CombatManager.Instance?.IsInProgress == true;

            switch (room)
            {
                case CombatRoom:
                    // Victory rewrites the save with the room marked
                    // pre-finished, so replaying it lands back on the reward
                    // screen instead of at the top of the fight.
                    return combatRunning ? Available("combat") : Blocked("this fight is already over");

                case MerchantRoom:
                    // Merchants never save. The whole visit is replayable.
                    return Available("shop");

                case EventRoom eventRoom:
                    // Ancient events are the one event kind that saves on
                    // completion (EventRoom.OnEventStateChanged).
                    if (eventRoom.IsPreFinished) return Blocked("this event is already over");

                    // An event option that started a fight leaves that fight's
                    // victory save behind once it is won.
                    if (FoughtAtCurrentMapPoint(state) && !combatRunning)
                        return Blocked("this event's fight is already over");

                    return Available("event");

                default:
                    return Blocked("not a combat, shop, or event");
            }
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"RoomResetter: availability check failed: {e}");
            return Blocked("unavailable");
        }
    }

    public static bool CanRestart => Describe().CanRestart;

    /// <summary>
    /// Fire-and-forget entry point. Returns false without touching the live run
    /// if a restart is not currently possible.
    /// </summary>
    public static bool Request(string source)
    {
        var availability = Describe();
        if (!availability.CanRestart)
        {
            CoreMain.Logger.Info(
                $"RoomResetter: restart refused ({availability.BlockedReason}, source={source})");
            return false;
        }

        _restartInProgress = true;
        TaskHelper.RunSafely(RestartAsync(availability.RoomNoun!, source));
        return true;
    }

    private static RoomRestartAvailability Available(string roomNoun) => new(roomNoun, null);

    private static RoomRestartAvailability Blocked(string reason) => new(null, reason);

    /// <summary>
    /// Whether a combat room has been entered at the map point the player is
    /// standing on. True for a plain fight, and for an event that pushed a
    /// combat room on top of itself.
    /// </summary>
    private static bool FoughtAtCurrentMapPoint(RunState state)
    {
        var here = state.CurrentMapPointHistoryEntry;
        if (here == null) return false;
        return here.HasRoomOfType(RoomType.Monster)
            || here.HasRoomOfType(RoomType.Elite)
            || here.HasRoomOfType(RoomType.Boss);
    }

    private static async Task RestartAsync(string roomNoun, string source)
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
                    $"RoomResetter: cannot read run save ({read.Status} {read.ErrorMessage}); live run left alone");
                return;
            }

            var save = read.SaveData;
            var runState = RunState.FromSerializable(save);

            var game = NGame.Instance;
            if (game == null)
            {
                CoreMain.Logger.Error("RoomResetter: NGame.Instance is null; live run left alone");
                return;
            }

            CoreMain.Logger.Info(
                $"RoomResetter: restarting {roomNoun} from run save (source={source}, " +
                $"floor={save.MapPointHistory?.Count}, pre_finished_room={save.PreFinishedRoom?.RoomType.ToString() ?? "none"})");

            // Per-phase timing, because the remaining cost after the fades is
            // the game's own reload work and it is worth knowing which part
            // dominates before trying to cut any of it.
            var total = Stopwatch.StartNew();
            var phase = Stopwatch.StartNew();

            NAudioManager.Instance?.StopMusic();
            await game.Transition.FadeOut(FadeOutSeconds);
            var fadeOutMs = phase.ElapsedMilliseconds; phase.Restart();

            // Frees RunManager.State so SetUpSavedSingleplayer will accept the
            // reloaded state, and suppresses any further save of the abandoned
            // attempt on the way out.
            RunManager.Instance.CleanUp();
            var cleanUpMs = phase.ElapsedMilliseconds; phase.Restart();

            // Note: this awaits SaveManager.IncrementNumReloads, which writes
            // the run save to disk before returning.
            await RunManager.Instance.SetUpSavedSingleplayer(runState, save);
            var setUpMs = phase.ElapsedMilliseconds; phase.Restart();

            game.ReactionContainer.InitializeNetworking(new NetSingleplayerGameService());

            // Asset preload, NRun scene rebuild, map load, room entry and the
            // normal room intro all happen inside here.
            await game.LoadRun(runState, save.PreFinishedRoom);
            var loadRunMs = phase.ElapsedMilliseconds; phase.Restart();

            await game.Transition.FadeIn(FadeInSeconds);
            var fadeInMs = phase.ElapsedMilliseconds;

            CoreMain.Logger.Info(
                $"RoomResetter: {roomNoun} restart complete in {total.ElapsedMilliseconds}ms " +
                $"(fade_out={fadeOutMs}ms, clean_up={cleanUpMs}ms, set_up_saved={setUpMs}ms, " +
                $"load_run={loadRunMs}ms, fade_in={fadeInMs}ms)");
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"RoomResetter: {roomNoun} restart failed: {e}");
        }
        finally
        {
            _restartInProgress = false;
        }
    }
}
