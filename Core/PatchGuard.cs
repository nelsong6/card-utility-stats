using System;
using System.Collections.Generic;

namespace SpireLens.Core;

/// <summary>
/// Wraps a Harmony patch body so an unhandled exception is caught, logged, and
/// rate-limited per call site — it never propagates back into game code.
///
/// Two problems this fixes across the ~60 patch classes:
///   1. Most patches swallowed exceptions into <see cref="CoreMain.LogDebug"/>,
///      which is a no-op unless the debug flag is on — so a patch that silently
///      broke after a Slay the Spire 2 update left no trace. PatchGuard logs at
///      always-on Error level instead, so a broken hook is visible by default.
///   2. A few sites (the combat-history master hook, per-frame turn hooks) fire
///      dozens of times a turn. A naive always-on log would flood godot.log if
///      one started throwing every call. PatchGuard logs the first failure per
///      site immediately, then at most once per <see cref="ThrottleWindow"/>,
///      folding the suppressed count into the next surfaced line.
/// </summary>
internal static class PatchGuard
{
    /// <summary>
    /// After a site's first Error, further Errors for that site are suppressed
    /// until this much time has elapsed. Internal + mutable so the convention
    /// test can shrink it; the default keeps a wedged hot hook to ~2 lines/min.
    /// </summary>
    internal static TimeSpan ThrottleWindow = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Injectable clock. Defaults to wall time; the convention test swaps it for
    /// a deterministic one so throttle behavior is exercised without sleeping.
    /// </summary>
    internal static Func<DateTime> Clock = () => DateTime.UtcNow;

    /// <summary>
    /// Injectable log sink. Defaults to the always-on Error logger; the test
    /// swaps it to count surfaced-vs-throttled lines without scraping stderr.
    /// </summary>
    internal static Action<string> LogSink = msg => CoreMain.Logger.Error(msg);

    private static readonly object _gate = new();
    private static readonly Dictionary<string, SiteState> _sites = new();

    private sealed class SiteState
    {
        public bool HasLogged;
        public DateTime LastLogged;
        public long SuppressedSinceLast;
    }

    /// <summary>
    /// Run a patch body, swallowing and rate-logging any exception so it never
    /// escapes into the game. <paramref name="site"/> identifies the call site
    /// for both throttling and the log line — pass a stable string (typically
    /// the patch class name).
    /// </summary>
    public static void Run(string site, Action body)
    {
        try
        {
            body();
        }
        catch (Exception e)
        {
            Report(site, e);
        }
    }

    private static void Report(string site, Exception e)
    {
        bool shouldLog;
        long suppressed = 0;

        lock (_gate)
        {
            if (!_sites.TryGetValue(site, out var state))
            {
                state = new SiteState();
                _sites[site] = state;
            }

            var now = Clock();
            if (!state.HasLogged || now - state.LastLogged >= ThrottleWindow)
            {
                shouldLog = true;
                suppressed = state.SuppressedSinceLast;
                state.SuppressedSinceLast = 0;
                state.LastLogged = now;
                state.HasLogged = true;
            }
            else
            {
                shouldLog = false;
                state.SuppressedSinceLast++;
            }
        }

        if (!shouldLog) return;

        var suffix = suppressed > 0 ? $" ({suppressed} similar suppressed)" : string.Empty;
        LogSink($"[PatchGuard] {site} threw: {e}{suffix}");
    }

    /// <summary>Test isolation for the per-site throttle state, clock, and sink.</summary>
    internal static void ResetForTest()
    {
        lock (_gate)
        {
            _sites.Clear();
            ThrottleWindow = TimeSpan.FromSeconds(30);
            Clock = () => DateTime.UtcNow;
            LogSink = msg => CoreMain.Logger.Error(msg);
        }
    }
}
