using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

public class RuntimeOptionsTests
{
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
