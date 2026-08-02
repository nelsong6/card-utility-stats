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
            MeatOnTheBonePreTriggerHpBelowHalfTotal = 20,
            MeatOnTheBonePreTriggerHpBelowHalfSamples = 2,
            MeatOnTheBonePreTriggerHpPercentTotal = 77.5m,
            MeatOnTheBonePreTriggerHpSamples = 2,
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
        Assert.Contains("Average raw HP-point distance below 50%", body);
        Assert.Contains("[b]10[/b]", body);
        Assert.Contains("Average current HP at combat end", body);
        Assert.Contains("[b]38.75%[/b]", body);
    }

    [Fact]
    public void PreTriggerHpSnapshot_AccumulatesDistanceBelowHalfAndPerTriggerPercentage()
    {
        var aggregate = new RelicAggregate();

        RunTracker.RecordMeatOnTheBonePreTriggerHpForTest(aggregate, currentHp: 30m, maxHp: 80m);
        RunTracker.RecordMeatOnTheBonePreTriggerHpForTest(aggregate, currentHp: 40m, maxHp: 100m);

        Assert.Equal(20m, aggregate.MeatOnTheBonePreTriggerHpBelowHalfTotal);
        Assert.Equal(2, aggregate.MeatOnTheBonePreTriggerHpBelowHalfSamples);
        Assert.Equal(0m, aggregate.MeatOnTheBonePreTriggerHpMissingTotal);
        Assert.Equal(77.5m, aggregate.MeatOnTheBonePreTriggerHpPercentTotal);
        Assert.Equal(2, aggregate.MeatOnTheBonePreTriggerHpSamples);
    }

    [Fact]
    public void PreTriggerHpSnapshot_UsesExactHalfOfOddMaximumHp()
    {
        var aggregate = new RelicAggregate();

        RunTracker.RecordMeatOnTheBonePreTriggerHpForTest(
            aggregate,
            currentHp: 34m,
            maxHp: 69m);

        Assert.Equal(0.5m, aggregate.MeatOnTheBonePreTriggerHpBelowHalfTotal);
        Assert.Equal(1, aggregate.MeatOnTheBonePreTriggerHpBelowHalfSamples);
    }

    [Fact]
    public void PreTriggerHpSnapshot_IgnoresInvalidMaximumHp()
    {
        var aggregate = new RelicAggregate();

        RunTracker.RecordMeatOnTheBonePreTriggerHpForTest(aggregate, currentHp: 0m, maxHp: 0m);

        Assert.Equal(0m, aggregate.MeatOnTheBonePreTriggerHpBelowHalfTotal);
        Assert.Equal(0, aggregate.MeatOnTheBonePreTriggerHpBelowHalfSamples);
        Assert.Equal(0m, aggregate.MeatOnTheBonePreTriggerHpPercentTotal);
        Assert.Equal(0, aggregate.MeatOnTheBonePreTriggerHpSamples);
    }

    [Fact]
    public void PreTriggerHpStats_MergeAdditively()
    {
        var target = new RelicAggregate
        {
            MeatOnTheBonePreTriggerHpBelowHalfTotal = 10m,
            MeatOnTheBonePreTriggerHpBelowHalfSamples = 1,
            MeatOnTheBonePreTriggerHpPercentTotal = 37.5m,
            MeatOnTheBonePreTriggerHpSamples = 1,
        };
        var source = new RelicAggregate
        {
            MeatOnTheBonePreTriggerHpBelowHalfTotal = 10m,
            MeatOnTheBonePreTriggerHpBelowHalfSamples = 1,
            MeatOnTheBonePreTriggerHpPercentTotal = 40m,
            MeatOnTheBonePreTriggerHpSamples = 1,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(20m, target.MeatOnTheBonePreTriggerHpBelowHalfTotal);
        Assert.Equal(2, target.MeatOnTheBonePreTriggerHpBelowHalfSamples);
        Assert.Equal(77.5m, target.MeatOnTheBonePreTriggerHpPercentTotal);
        Assert.Equal(2, target.MeatOnTheBonePreTriggerHpSamples);
    }
}
