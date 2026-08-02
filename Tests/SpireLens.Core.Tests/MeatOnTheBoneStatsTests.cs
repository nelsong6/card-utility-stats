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
            MeatOnTheBonePreTriggerHpRelativeToHalfTotal = -20,
            MeatOnTheBonePreTriggerHpRelativeToHalfSamples = 2,
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
        Assert.Contains("Average raw HP-point difference from 50%", body);
        Assert.Contains("[b]-10[/b]", body);
        Assert.Contains("Average HP percentage-point difference from 50%", body);
        Assert.Contains("[b]-11.25%[/b]", body);
    }

    [Fact]
    public void PreTriggerHpSnapshot_AccumulatesSignedDifferenceFromHalfAndPerTriggerPercentage()
    {
        var aggregate = new RelicAggregate();

        RunTracker.RecordMeatOnTheBonePreTriggerHpForTest(aggregate, currentHp: 30m, maxHp: 80m);
        RunTracker.RecordMeatOnTheBonePreTriggerHpForTest(aggregate, currentHp: 40m, maxHp: 100m);

        Assert.Equal(-20m, aggregate.MeatOnTheBonePreTriggerHpRelativeToHalfTotal);
        Assert.Equal(2, aggregate.MeatOnTheBonePreTriggerHpRelativeToHalfSamples);
        Assert.Equal(0m, aggregate.MeatOnTheBonePreTriggerHpBelowHalfTotal);
        Assert.Equal(0, aggregate.MeatOnTheBonePreTriggerHpBelowHalfSamples);
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

        Assert.Equal(-0.5m, aggregate.MeatOnTheBonePreTriggerHpRelativeToHalfTotal);
        Assert.Equal(1, aggregate.MeatOnTheBonePreTriggerHpRelativeToHalfSamples);
    }

    [Fact]
    public void PreTriggerHpSnapshot_IgnoresInvalidMaximumHp()
    {
        var aggregate = new RelicAggregate();

        RunTracker.RecordMeatOnTheBonePreTriggerHpForTest(aggregate, currentHp: 0m, maxHp: 0m);

        Assert.Equal(0m, aggregate.MeatOnTheBonePreTriggerHpRelativeToHalfTotal);
        Assert.Equal(0, aggregate.MeatOnTheBonePreTriggerHpRelativeToHalfSamples);
        Assert.Equal(0m, aggregate.MeatOnTheBonePreTriggerHpPercentTotal);
        Assert.Equal(0, aggregate.MeatOnTheBonePreTriggerHpSamples);
    }

    [Fact]
    public void PreTriggerHpStats_MergeAdditively()
    {
        var target = new RelicAggregate
        {
            MeatOnTheBonePreTriggerHpRelativeToHalfTotal = -10m,
            MeatOnTheBonePreTriggerHpRelativeToHalfSamples = 1,
            MeatOnTheBonePreTriggerHpPercentTotal = 37.5m,
            MeatOnTheBonePreTriggerHpSamples = 1,
        };
        var source = new RelicAggregate
        {
            MeatOnTheBonePreTriggerHpRelativeToHalfTotal = -10m,
            MeatOnTheBonePreTriggerHpRelativeToHalfSamples = 1,
            MeatOnTheBonePreTriggerHpPercentTotal = 40m,
            MeatOnTheBonePreTriggerHpSamples = 1,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(-20m, target.MeatOnTheBonePreTriggerHpRelativeToHalfTotal);
        Assert.Equal(2, target.MeatOnTheBonePreTriggerHpRelativeToHalfSamples);
        Assert.Equal(77.5m, target.MeatOnTheBonePreTriggerHpPercentTotal);
        Assert.Equal(2, target.MeatOnTheBonePreTriggerHpSamples);
    }

    [Fact]
    public void Tooltip_SignedDifference_ShowsPlusAboveHalf()
    {
        var relic = (MeatOnTheBone)RuntimeHelpers.GetUninitializedObject(typeof(MeatOnTheBone));
        var aggregate = new RelicAggregate
        {
            MeatOnTheBonePreTriggerHpRelativeToHalfTotal = 10m,
            MeatOnTheBonePreTriggerHpRelativeToHalfSamples = 2,
            MeatOnTheBonePreTriggerHpPercentTotal = 120m,
            MeatOnTheBonePreTriggerHpSamples = 2,
        };

        Assert.True(RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            aggregate,
            floorCount: null,
            out _,
            out var body));
        Assert.Contains("[b]+5[/b]", body);
        Assert.Contains("[b]+10%[/b]", body);
    }

    [Fact]
    public void Tooltip_SignedDifference_ProjectsLegacyBelowHalfSamplesAsNegative()
    {
        var relic = (MeatOnTheBone)RuntimeHelpers.GetUninitializedObject(typeof(MeatOnTheBone));
        var aggregate = new RelicAggregate
        {
            MeatOnTheBonePreTriggerHpBelowHalfTotal = 20m,
            MeatOnTheBonePreTriggerHpBelowHalfSamples = 2,
        };

        Assert.True(RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            aggregate,
            floorCount: null,
            out _,
            out var body));
        Assert.Contains("[b]-10[/b]", body);
    }
}
