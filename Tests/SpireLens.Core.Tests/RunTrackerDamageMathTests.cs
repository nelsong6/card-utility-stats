using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

public class RunTrackerDamageMathTests
{
    [Fact]
    public void ComputeEnemyDamageTotals_UsesObservedHpLossForEffectiveDamage()
    {
        var totals = RunTracker.ComputeEnemyDamageTotals(
            blockedDamage: 0,
            unblockedDamage: 1,
            overkillDamage: 23);

        Assert.Equal(24, totals.IntendedDamage);
        Assert.Equal(1, totals.EffectiveDamage);
    }

    [Fact]
    public void ComputeEnemyDamageTotals_IncludesBlockedDamageInIntendedDamage()
    {
        var totals = RunTracker.ComputeEnemyDamageTotals(
            blockedDamage: 5,
            unblockedDamage: 7,
            overkillDamage: 0);

        Assert.Equal(12, totals.IntendedDamage);
        Assert.Equal(7, totals.EffectiveDamage);
    }

    // The lethal-overkill case is the shared convention behind the poison
    // killing-tick fix: a 10-poison tick on a 4-HP enemy reports unblocked=4
    // (HP actually lost) and overkill=6 (disjoint excess). Effective is the 4
    // HP removed, intended is the full 10. Poison attribution now routes
    // through this helper, so pinning it here covers TryRecordPoisonTickDamage,
    // RecordDamageFromCard, and enemy damage with one convention.
    [Fact]
    public void ComputeEnemyDamageTotals_LethalOverkill_EffectiveIsHpLost_IntendedIsFullTick()
    {
        var totals = RunTracker.ComputeEnemyDamageTotals(
            blockedDamage: 0,
            unblockedDamage: 4,
            overkillDamage: 6);

        Assert.Equal(10, totals.IntendedDamage);
        Assert.Equal(4, totals.EffectiveDamage);
    }
}
