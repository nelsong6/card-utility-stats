using System.Text.Json;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class RuntimeOptionsTests
{
    [Fact]
    public void EnemyHoverStats_DefaultOff()
    {
        Assert.False(new RuntimeOptions().ShowEnemyStatsOnHover);
        Assert.False(new Prefs().ShowEnemyStatsTicked);
    }

    [Fact]
    public void EnemyHoverStats_OlderRuntimeSnapshotDefaultsOff()
    {
        var options = JsonSerializer.Deserialize<RuntimeOptions>("{}");

        Assert.NotNull(options);
        Assert.False(options!.ShowEnemyStatsOnHover);
    }

    [Fact]
    public void EnemyHoverStats_ExplicitRuntimeSnapshotCanEnableIt()
    {
        var options = JsonSerializer.Deserialize<RuntimeOptions>(
            """{"ShowEnemyStatsOnHover":true}""");

        Assert.NotNull(options);
        Assert.True(options!.ShowEnemyStatsOnHover);
    }

    [Theory]
    [InlineData(false, null, true, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, null, false, false)]
    [InlineData(true, null, true, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, true)]
    public void EnemyHoverStats_RequiresGeneralStatsAndEnemyOptIn(
        bool viewStatsEnabled,
        bool? injectedEnemyToggleState,
        bool persistedEnemyPreference,
        bool expected)
    {
        Assert.Equal(
            expected,
            EnemyHoverShowPatch.ResolveEnemyStatsEnabled(
                viewStatsEnabled,
                injectedEnemyToggleState,
                persistedEnemyPreference));
    }

    [Fact]
    public void DisableCardStatsDuringCombat_OnlyPausesActiveCombatCardTracking()
    {
        Assert.True(RunTracker.ShouldTrackCardStatsDuringCombatForTest(
            disableCardStatsDuringCombat: false,
            combatActive: false));
        Assert.True(RunTracker.ShouldTrackCardStatsDuringCombatForTest(
            disableCardStatsDuringCombat: false,
            combatActive: true));
        Assert.True(RunTracker.ShouldTrackCardStatsDuringCombatForTest(
            disableCardStatsDuringCombat: true,
            combatActive: false));
        Assert.False(RunTracker.ShouldTrackCardStatsDuringCombatForTest(
            disableCardStatsDuringCombat: true,
            combatActive: true));
    }
}
