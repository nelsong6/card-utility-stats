using System.Reflection;
using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

public class RunTrackerAggregateTests
{
    private static readonly MethodInfo CloneAggregateMethod =
        typeof(RunTracker).GetMethod("CloneAggregate", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("CloneAggregate not found.");

    private static readonly MethodInfo MergeAggregateIntoMethod =
        typeof(RunTracker).GetMethod("MergeAggregateInto", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("MergeAggregateInto not found.");

    [Fact]
    public void CloneAggregate_CopiesForgeGeneratedTimesSummonedAndOstyStats()
    {
        var source = new CardAggregate
        {
            TimesSummonedToHand = 2,
            TotalForgeGenerated = 9m,
            TotalOstyHpAttackBonus = 21,
            TimesOstyHpAttackBonusApplied = 3,
            TimesOstySummoned = 2,
            TotalOstyHpSummoned = 18m,
            TimesReplayExtraPlayed = 4,
        };
        source.ReplayExtraPlayReasons["power:POWER.BURST"] = new ReplayExtraPlayReasonAggregate
        {
            ReasonId = "power:POWER.BURST",
            DisplayName = "Burst",
            Count = 4,
        };

        var clone = (CardAggregate)(CloneAggregateMethod.Invoke(null, new object?[] { source })
            ?? throw new InvalidOperationException("CloneAggregate returned null."));

        Assert.Equal(2, clone.TimesSummonedToHand);
        Assert.Equal(9m, clone.TotalForgeGenerated);
        Assert.Equal(21, clone.TotalOstyHpAttackBonus);
        Assert.Equal(3, clone.TimesOstyHpAttackBonusApplied);
        Assert.Equal(2, clone.TimesOstySummoned);
        Assert.Equal(18m, clone.TotalOstyHpSummoned);
        Assert.Equal(4, clone.TimesReplayExtraPlayed);
        Assert.Equal(4, clone.ReplayExtraPlayReasons["power:POWER.BURST"].Count);
        Assert.Equal("Burst", clone.ReplayExtraPlayReasons["power:POWER.BURST"].DisplayName);
    }

    [Fact]
    public void MergeAggregateInto_AddsForgeGeneratedTimesSummonedAndOstyStats()
    {
        var target = new CardAggregate
        {
            TimesSummonedToHand = 1,
            TotalForgeGenerated = 5m,
            TotalOstyHpAttackBonus = 8,
            TimesOstyHpAttackBonusApplied = 1,
            TimesOstySummoned = 1,
            TotalOstyHpSummoned = 10m,
            TimesReplayExtraPlayed = 1,
        };
        target.ReplayExtraPlayReasons["replay"] = new ReplayExtraPlayReasonAggregate
        {
            ReasonId = "replay",
            DisplayName = "Replay",
            Count = 1,
        };
        var source = new CardAggregate
        {
            TimesSummonedToHand = 2,
            TotalForgeGenerated = 4m,
            TotalOstyHpAttackBonus = 13,
            TimesOstyHpAttackBonusApplied = 2,
            TimesOstySummoned = 2,
            TotalOstyHpSummoned = 15m,
            TimesReplayExtraPlayed = 3,
        };
        source.ReplayExtraPlayReasons["power:POWER.BURST"] = new ReplayExtraPlayReasonAggregate
        {
            ReasonId = "power:POWER.BURST",
            DisplayName = "Burst",
            Count = 3,
        };

        _ = MergeAggregateIntoMethod.Invoke(null, new object?[] { target, source });

        Assert.Equal(3, target.TimesSummonedToHand);
        Assert.Equal(9m, target.TotalForgeGenerated);
        Assert.Equal(21, target.TotalOstyHpAttackBonus);
        Assert.Equal(3, target.TimesOstyHpAttackBonusApplied);
        Assert.Equal(3, target.TimesOstySummoned);
        Assert.Equal(25m, target.TotalOstyHpSummoned);
        Assert.Equal(4, target.TimesReplayExtraPlayed);
        Assert.Equal(1, target.ReplayExtraPlayReasons["replay"].Count);
        Assert.Equal(3, target.ReplayExtraPlayReasons["power:POWER.BURST"].Count);
    }
}
