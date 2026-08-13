using System;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace SpireLens.Core;

/// <summary>
/// Samples the same pause-aware clock used by the stock run timer and assigns
/// each elapsed whole second to the gameplay surface that owned it. This is a
/// Core-owned timer rather than a Harmony patch so hot reload can replace it
/// without leaving an old callback in the game's long-lived assembly.
/// </summary>
internal static class RunTimeStatsTracker
{
    private const string TimerNodeName = "SpireLensRunTimeStatsSampler";
    private static Godot.Timer? _timer;
    private static Action? _timeoutHandler;

    public static void Initialize()
    {
        Shutdown();

        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree) return;

            var timer = new Godot.Timer
            {
                Name = TimerNodeName,
                WaitTime = 1d,
                OneShot = false,
                Autostart = true,
                ProcessMode = Node.ProcessModeEnum.Always,
            };
            Action handler = SampleNow;
            timer.Timeout += handler;
            _timer = timer;
            _timeoutHandler = handler;
            AttachSamplerDeferred(timer);
            SampleNow();
        }
        catch (Exception exception)
        {
            CoreMain.Logger.Error(
                $"RunTimeStatsTracker initialization failed: {exception}");
        }
    }

    /// <summary>
    /// Attaches the sampler on the next idle frame instead of inline. On a cold
    /// start <see cref="CoreMain.Initialize"/> runs from inside
    /// <c>NGame._EnterTree</c>, and Godot refuses <c>add_child</c> on a node
    /// that is still setting up its own children. That refusal is a printed
    /// engine error, not a managed exception, so the direct call left the
    /// sampler parentless — silently never ticking for the rest of the process
    /// — while initialization reported success. A hot reload never reproduced
    /// it, because by then <c>_EnterTree</c> is long finished. See "Core Loads
    /// Before The Scene Tree Accepts New Children" in
    /// docs/sts2-runtime-primer.md.
    /// </summary>
    private static void AttachSamplerDeferred(Godot.Timer timer)
        => Callable.From(() => AttachSampler(timer)).CallDeferred();

    private static void AttachSampler(Godot.Timer timer)
    {
        try
        {
            // A Shutdown between the deferral and this frame (rapid reload)
            // clears _timer; attaching that orphan would resurrect a sampler
            // the previous Core already tore down.
            if (!IsLive(timer)
                || !ReferenceEquals(timer, _timer)
                || timer.GetParent() != null
                || Engine.GetMainLoop() is not SceneTree tree)
            {
                return;
            }

            tree.Root.AddChild(timer);
            CoreMain.Logger.Info(
                "Run time sampler attached (1s tick, deferred add_child).");
        }
        catch (Exception exception)
        {
            CoreMain.Logger.Error(
                $"RunTimeStatsTracker attach failed: {exception}");
        }
    }

    public static void Shutdown()
    {
        try
        {
            SampleNow();
            RunTracker.FlushRunTimeStats();
        }
        catch (Exception exception)
        {
            CoreMain.LogDebug(
                $"RunTimeStatsTracker final sample skipped: {exception.Message}");
        }

        var timer = _timer;
        var handler = _timeoutHandler;
        _timer = null;
        _timeoutHandler = null;
        if (!IsLive(timer)) return;

        try
        {
            if (handler != null)
                timer!.Timeout -= handler;
            timer!.Stop();
            timer.GetParent()?.RemoveChild(timer);
            timer.QueueFree();
        }
        catch (Exception exception)
        {
            CoreMain.LogDebug(
                $"RunTimeStatsTracker teardown skipped: {exception.Message}");
        }
    }

    internal static void SampleNow()
    {
        try
        {
            RunTimerStatsTooltip.EnsureLiveTarget();
            if (RunManager.Instance?.State == null) return;

            RunTracker.SampleRunTimeStats(
                ClassifyCurrentSurface(),
                Math.Max(0L, RunManager.Instance.RunTime));
            RunTimerStatsTooltip.RefreshVisibleLiveTooltip();
        }
        catch (Exception exception)
        {
            CoreMain.LogDebug(
                $"RunTimeStatsTracker sample failed: {exception.Message}");
        }
    }

    internal static RunTimeCategory ClassifyCurrentSurface()
    {
        try
        {
            if (CombatManager.Instance?.IsInProgress == true)
                return RunTimeCategory.Combat;

            if (Engine.GetMainLoop() is SceneTree tree
                && HasRewardScreen(tree.Root))
            {
                // The rewards screen remains in the overlay stack while a
                // concrete card/relic choice is open, so the nested selection
                // remains part of reward-screen time as the player expects.
                return RunTimeCategory.RewardScreen;
            }

            if (NMapScreen.Instance?.IsOpen == true)
                return RunTimeCategory.Map;

            if (RunManager.Instance?.State?.CurrentRoom?.RoomType
                == RoomType.Event)
            {
                return RunTimeCategory.Event;
            }
        }
        catch (Exception exception)
        {
            CoreMain.LogDebug(
                $"RunTimeStatsTracker classification failed: {exception.Message}");
        }

        return RunTimeCategory.None;
    }

    private static bool HasRewardScreen(Node node)
    {
        if (node is NRewardsScreen && IsLive(node)) return true;

        foreach (var child in node.GetChildren())
        {
            if (HasRewardScreen(child)) return true;
        }

        return false;
    }

    private static bool IsLive(GodotObject? value)
        => value != null && GodotObject.IsInstanceValid(value);
}
