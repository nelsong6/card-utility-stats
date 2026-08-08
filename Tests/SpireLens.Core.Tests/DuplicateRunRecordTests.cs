using System;
using System.Collections.Generic;
using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

/// <summary>
/// Pins the two halves of the duplicate-run-record defect: a finished game must
/// never get a second record written for it, and when a duplicate already
/// exists on disk the run history screen must still resolve the real run.
/// </summary>
public class DuplicateRunRecordTests
{
    private const long GameStartTime = 1786054674L;

    // ---- Half one: stop writing the duplicate ----

    [Fact]
    public void SaveGuard_SuppressesEmptyRecordResurrectedAfterTheRunEnded()
    {
        var ended = Ended("win", floor: 49);
        var resurrected = InProgress();

        Assert.True(RunTracker.IsResurrectedEndedRun(ended, resurrected));
    }

    [Theory]
    [InlineData("win")]
    [InlineData("loss")]
    [InlineData("abandoned")]
    public void SaveGuard_AppliesToEveryTerminalOutcome(string outcome)
    {
        Assert.True(
            RunTracker.IsResurrectedEndedRun(Ended(outcome, floor: 12), InProgress()));
    }

    [Fact]
    public void SaveGuard_AllowsTheEndedRunToWriteItself()
    {
        var ended = Ended("win", floor: 49);

        // OnRunEnded assigns _lastEndedRun = _currentRun, so the guard must not
        // block the very write that records the outcome.
        Assert.False(RunTracker.IsResurrectedEndedRun(ended, ended));
    }

    [Fact]
    public void SaveGuard_AllowsAGenuinelyNewRunWithADifferentStartTime()
    {
        var ended = Ended("win", floor: 49);
        var next = InProgress();
        next.GameStartTime = GameStartTime + 1;

        Assert.False(RunTracker.IsResurrectedEndedRun(ended, next));
    }

    [Fact]
    public void SaveGuard_AllowsLazyMintWhenNoRunHasEndedYet()
    {
        // The legitimate case the lazy mint exists for: mod hot-loaded mid-run,
        // nothing has ended, data must not be dropped.
        Assert.False(RunTracker.IsResurrectedEndedRun(null, InProgress()));
        Assert.False(
            RunTracker.IsResurrectedEndedRun(InProgress(), InProgress()));
    }

    [Fact]
    public void SaveGuard_IgnoresRecordsWithNoStartTimeToMatchOn()
    {
        var noStartTime = InProgress();
        noStartTime.GameStartTime = null;

        Assert.False(
            RunTracker.IsResurrectedEndedRun(Ended("win", floor: 3), noStartTime));
    }

    [Fact]
    public void SaveGuard_DoesNotSuppressADistinctAlreadyFinishedRecord()
    {
        var ended = Ended("win", floor: 49);
        var otherFinished = Ended("loss", floor: 49);

        Assert.False(RunTracker.IsResurrectedEndedRun(ended, otherFinished));
    }

    // ---- Half two: resolve the real run when a duplicate already exists ----

    [Fact]
    public void HistoryLookup_PrefersTheFinishedRecordOverANewerInProgressDuplicate()
    {
        // The observed failure: the stray duplicate is written *after* the real
        // record, so newest-mtime alone picks the empty one.
        var real = Candidate("real.json", finished: true, floor: 49, minutes: 0);
        var stray = Candidate("stray.json", finished: false, floor: -1, minutes: 5);

        var best = RunStorage.SelectBestRunFileCandidate(new[] { stray, real });

        Assert.Equal("real.json", best!.Path);
    }

    [Fact]
    public void HistoryLookup_FallsBackToFloorWhenNeitherRecordFinished()
    {
        var played = Candidate("played.json", finished: false, floor: 31, minutes: 0);
        var empty = Candidate("empty.json", finished: false, floor: -1, minutes: 9);

        var best = RunStorage.SelectBestRunFileCandidate(new[] { empty, played });

        Assert.Equal("played.json", best!.Path);
    }

    [Fact]
    public void HistoryLookup_UsesWriteTimeOnlyToBreakRemainingTies()
    {
        var older = Candidate("older.json", finished: true, floor: 20, minutes: 0);
        var newer = Candidate("newer.json", finished: true, floor: 20, minutes: 4);

        var best = RunStorage.SelectBestRunFileCandidate(new[] { older, newer });

        Assert.Equal("newer.json", best!.Path);
    }

    [Fact]
    public void HistoryLookup_ReturnsTheSoleCandidateUnchanged()
    {
        var only = Candidate("only.json", finished: false, floor: -1, minutes: 0);

        Assert.Equal("only.json", RunStorage.SelectBestRunFileCandidate(new[] { only })!.Path);
    }

    [Fact]
    public void HistoryLookup_ReturnsNullWithNoCandidates()
    {
        Assert.Null(
            RunStorage.SelectBestRunFileCandidate(Array.Empty<RunStorage.RunFileCandidate>()));
        Assert.Null(RunStorage.SelectBestRunFileCandidate(null!));
    }

    // ---- Half three: a run is only created by run start, or mid-run hot-load ----

    [Fact]
    public void RunCreation_IsRefusedOnceThatGameRunHasFinished()
    {
        // Every write path funnels through this one gate, so a single false
        // closes the whole ~38-call-site resurrection class at once.
        Assert.True(
            RunTracker.WouldResurrectEndedRun(Ended("win", floor: 49), GameStartTime));
    }

    [Theory]
    [InlineData("win")]
    [InlineData("loss")]
    [InlineData("abandoned")]
    public void RunCreation_IsRefusedForEveryTerminalOutcome(string outcome)
    {
        Assert.True(
            RunTracker.WouldResurrectEndedRun(Ended(outcome, floor: 8), GameStartTime));
    }

    [Fact]
    public void RunCreation_StillWorksWhenTheModIsHotLoadedMidRun()
    {
        // The one case the lazy path exists for: no run record yet, nothing has
        // ended, data must not be dropped.
        Assert.False(RunTracker.WouldResurrectEndedRun(null, GameStartTime));
    }

    [Fact]
    public void RunCreation_AllowsTheNextGameRunAfterOneFinished()
    {
        Assert.False(
            RunTracker.WouldResurrectEndedRun(Ended("win", floor: 49), GameStartTime + 1));
    }

    [Fact]
    public void RunCreation_IsAllowedWhenThePreviousRunNeverFinished()
    {
        Assert.False(RunTracker.WouldResurrectEndedRun(InProgress(), GameStartTime));
    }

    [Fact]
    public void RunCreation_IsAllowedWhenNoLiveGameRunIsReadable()
    {
        // _startTime unreadable reads as 0; that is "unknown", not "matches",
        // so it must not block the legitimate mid-run hot-load mint.
        Assert.False(
            RunTracker.WouldResurrectEndedRun(Ended("win", floor: 49), liveGameStartTime: 0));
    }

    private static RunData Ended(string outcome, int floor)
        => new()
        {
            RunId = $"ended-{outcome}",
            GameStartTime = GameStartTime,
            Outcome = outcome,
            FloorReached = floor,
        };

    private static RunData InProgress()
        => new()
        {
            RunId = "resurrected",
            GameStartTime = GameStartTime,
            Outcome = RunTracker.InProgressOutcome,
        };

    private static RunStorage.RunFileCandidate Candidate(
        string path,
        bool finished,
        int floor,
        int minutes)
        => new(
            path,
            finished,
            floor,
            new DateTime(2026, 8, 8, 10, 0, 0, DateTimeKind.Utc).AddMinutes(minutes));
}
