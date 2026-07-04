using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Creatures;
using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

/// <summary>
/// Pins the dedup contract behind the combat-ending killing-blow capture
/// (HookAfterDamageGivenPatch → RunTracker.RecordCombatEndingSuppressedDamage):
/// a DamageResult the game already emitted through CombatHistory must never be
/// recorded a second time, and a suppressed one must be claimable exactly once.
/// Observe() and the capture share one helper (TryMarkDamageResultObserved);
/// first observation wins, every later observation of the same object loses.
///
/// DamageResult instances are materialized without running the game
/// constructor (it requires a live Creature); the dedup set is
/// reference-keyed, so field state is irrelevant.
///
/// These tests clear and mutate the process-wide observed-result set without
/// restoring it, so they run in the serialized <c>RunTrackerState</c>
/// collection — otherwise a future class that also touches that static could
/// interleave and flake (#118).
/// </summary>
[Collection("RunTrackerState")]
public class CombatEndingDamageTests
{
    private static DamageResult UnconstructedResult() =>
        (DamageResult)RuntimeHelpers.GetUninitializedObject(typeof(DamageResult));

    [Fact]
    public void TryMark_UnseenResult_ClaimsExactlyOnce()
    {
        RunTracker.ClearObservedDamageResultsForTest();
        var result = UnconstructedResult();

        Assert.True(RunTracker.TryMarkDamageResultObserved(result));
        Assert.False(RunTracker.TryMarkDamageResultObserved(result));
    }

    [Fact]
    public void TryMark_ResultObservedThroughHistory_IsNotClaimableByCapture()
    {
        RunTracker.ClearObservedDamageResultsForTest();
        var result = UnconstructedResult();

        // Observe() marking the organic entry...
        Assert.True(RunTracker.TryMarkDamageResultObserved(result));
        // ...means the combat-ending capture loses the claim.
        Assert.False(RunTracker.TryMarkDamageResultObserved(result));
    }

    [Fact]
    public void TryMark_DistinctResults_AreIndependent()
    {
        RunTracker.ClearObservedDamageResultsForTest();
        var emitted = UnconstructedResult();
        var suppressed = UnconstructedResult();

        Assert.True(RunTracker.TryMarkDamageResultObserved(emitted));

        Assert.False(RunTracker.TryMarkDamageResultObserved(emitted));
        Assert.True(RunTracker.TryMarkDamageResultObserved(suppressed));
    }

    [Fact]
    public void TryMark_Null_IsNeverClaimable()
    {
        Assert.False(RunTracker.TryMarkDamageResultObserved(null));
    }
}
