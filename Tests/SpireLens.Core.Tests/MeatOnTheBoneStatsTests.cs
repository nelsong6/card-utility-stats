using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class MeatOnTheBoneStatsTests
{
    [Theory]
    [InlineData(34, 69, true)]
    [InlineData(35, 69, false)]
    [InlineData(50, 100, true)]
    [InlineData(51, 100, false)]
    public void ActivationThreshold_MatchesGameIntegerCutoff(
        decimal currentHp,
        decimal maxHp,
        bool expected)
    {
        Assert.Equal(
            expected,
            MeatOnTheBoneAfterCombatVictoryEarlyPatch.ShouldActivate(
                currentHp,
                maxHp,
                thresholdPercent: 50m));
    }

    [Fact]
    public void Tooltip_ShowsActivationAndObservedHealingStats()
    {
        var relic = (MeatOnTheBone)RuntimeHelpers.GetUninitializedObject(typeof(MeatOnTheBone));
        var aggregate = new RelicAggregate
        {
            Activations = 2,
            TotalHealingAttempted = 24,
            TotalHealingRestored = 19,
            TotalHealingLost = 5,
        };

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            aggregate,
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Meat on the Bone", title);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("HP healed", body);
        Assert.Contains("[b]19[/b]", body);
        Assert.Contains("healing lost", body);
        Assert.Contains("[b]5[/b]", body);
    }
}
