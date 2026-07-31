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
    public void CloneAggregate_CopiesForgeOrbsTimesSummonedAndOstyStats()
    {
        var source = new CardAggregate
        {
            CombatsInDeck = 7,
            TimesSummonedToHand = 2,
            TotalForgeGenerated = 9m,
            TotalOrbsCreated = 6,
            TotalOstyHpAttackBonus = 21,
            TimesOstyHpAttackBonusApplied = 3,
            TimesOstySummoned = 2,
            TotalOstyHpSummoned = 18m,
            TimesReplayExtraPlanned = 5,
            TimesReplayExtraPlayed = 4,
            TimesReplayAttackNoDamage = 2,
        };
        source.OrbOutcomes["ORB.FROST"] = new CardOrbAggregate
        {
            OrbId = "ORB.FROST",
            Created = 6,
            PassiveActivations = 9,
            Evokes = 4,
            Fizzles = 1,
            BlockGained = 28,
        };
        source.ReplayExtraPlayPlannedReasons["power:POWER.BURST"] = new ReplayExtraPlayReasonAggregate
        {
            ReasonId = "power:POWER.BURST",
            DisplayName = "Burst",
            Count = 5,
        };
        source.ReplayExtraPlayReasons["power:POWER.BURST"] = new ReplayExtraPlayReasonAggregate
        {
            ReasonId = "power:POWER.BURST",
            DisplayName = "Burst",
            Count = 4,
        };
        source.ReplayAttackNoDamageReasons["power:POWER.BURST"] = new ReplayExtraPlayReasonAggregate
        {
            ReasonId = "power:POWER.BURST",
            DisplayName = "Burst",
            Count = 2,
        };

        var clone = (CardAggregate)(CloneAggregateMethod.Invoke(null, new object?[] { source })
            ?? throw new InvalidOperationException("CloneAggregate returned null."));

        Assert.Equal(2, clone.TimesSummonedToHand);
        Assert.Equal(7, clone.CombatsInDeck);
        Assert.Equal(9m, clone.TotalForgeGenerated);
        Assert.Equal(6, clone.TotalOrbsCreated);
        Assert.Equal(9, clone.OrbOutcomes["ORB.FROST"].PassiveActivations);
        Assert.Equal(4, clone.OrbOutcomes["ORB.FROST"].Evokes);
        Assert.Equal(1, clone.OrbOutcomes["ORB.FROST"].Fizzles);
        Assert.Equal(28, clone.OrbOutcomes["ORB.FROST"].BlockGained);
        Assert.Equal(21, clone.TotalOstyHpAttackBonus);
        Assert.Equal(3, clone.TimesOstyHpAttackBonusApplied);
        Assert.Equal(2, clone.TimesOstySummoned);
        Assert.Equal(18m, clone.TotalOstyHpSummoned);
        Assert.Equal(5, clone.TimesReplayExtraPlanned);
        Assert.Equal(4, clone.TimesReplayExtraPlayed);
        Assert.Equal(2, clone.TimesReplayAttackNoDamage);
        Assert.Equal(5, clone.ReplayExtraPlayPlannedReasons["power:POWER.BURST"].Count);
        Assert.Equal(4, clone.ReplayExtraPlayReasons["power:POWER.BURST"].Count);
        Assert.Equal(2, clone.ReplayAttackNoDamageReasons["power:POWER.BURST"].Count);
        Assert.Equal("Burst", clone.ReplayExtraPlayReasons["power:POWER.BURST"].DisplayName);
    }

    [Fact]
    public void MergeAggregateInto_AddsForgeOrbsTimesSummonedAndOstyStats()
    {
        var target = new CardAggregate
        {
            CombatsInDeck = 4,
            TimesSummonedToHand = 1,
            TotalForgeGenerated = 5m,
            TotalOrbsCreated = 2,
            TotalOstyHpAttackBonus = 8,
            TimesOstyHpAttackBonusApplied = 1,
            TimesOstySummoned = 1,
            TotalOstyHpSummoned = 10m,
            TimesReplayExtraPlanned = 2,
            TimesReplayExtraPlayed = 1,
            TimesReplayAttackNoDamage = 1,
        };
        target.OrbOutcomes["ORB.FROST"] = new CardOrbAggregate
        {
            OrbId = "ORB.FROST",
            Created = 2,
            PassiveActivations = 3,
            Evokes = 1,
            BlockGained = 8,
        };
        target.ReplayExtraPlayPlannedReasons["replay"] = new ReplayExtraPlayReasonAggregate
        {
            ReasonId = "replay",
            DisplayName = "Replay",
            Count = 2,
        };
        target.ReplayExtraPlayReasons["replay"] = new ReplayExtraPlayReasonAggregate
        {
            ReasonId = "replay",
            DisplayName = "Replay",
            Count = 1,
        };
        target.ReplayAttackNoDamageReasons["replay"] = new ReplayExtraPlayReasonAggregate
        {
            ReasonId = "replay",
            DisplayName = "Replay",
            Count = 1,
        };
        var source = new CardAggregate
        {
            CombatsInDeck = 7,
            TimesSummonedToHand = 2,
            TotalForgeGenerated = 4m,
            TotalOrbsCreated = 5,
            TotalOstyHpAttackBonus = 13,
            TimesOstyHpAttackBonusApplied = 2,
            TimesOstySummoned = 2,
            TotalOstyHpSummoned = 15m,
            TimesReplayExtraPlanned = 3,
            TimesReplayExtraPlayed = 3,
            TimesReplayAttackNoDamage = 2,
        };
        source.OrbOutcomes["ORB.FROST"] = new CardOrbAggregate
        {
            OrbId = "ORB.FROST",
            Created = 5,
            PassiveActivations = 7,
            Evokes = 3,
            Fizzles = 2,
            BlockGained = 20,
        };
        source.ReplayExtraPlayPlannedReasons["power:POWER.BURST"] = new ReplayExtraPlayReasonAggregate
        {
            ReasonId = "power:POWER.BURST",
            DisplayName = "Burst",
            Count = 3,
        };
        source.ReplayExtraPlayReasons["power:POWER.BURST"] = new ReplayExtraPlayReasonAggregate
        {
            ReasonId = "power:POWER.BURST",
            DisplayName = "Burst",
            Count = 3,
        };
        source.ReplayAttackNoDamageReasons["power:POWER.BURST"] = new ReplayExtraPlayReasonAggregate
        {
            ReasonId = "power:POWER.BURST",
            DisplayName = "Burst",
            Count = 2,
        };

        _ = MergeAggregateIntoMethod.Invoke(null, new object?[] { target, source });

        Assert.Equal(3, target.TimesSummonedToHand);
        Assert.Equal(11, target.CombatsInDeck);
        Assert.Equal(9m, target.TotalForgeGenerated);
        Assert.Equal(7, target.TotalOrbsCreated);
        Assert.Equal(7, target.OrbOutcomes["ORB.FROST"].Created);
        Assert.Equal(10, target.OrbOutcomes["ORB.FROST"].PassiveActivations);
        Assert.Equal(4, target.OrbOutcomes["ORB.FROST"].Evokes);
        Assert.Equal(2, target.OrbOutcomes["ORB.FROST"].Fizzles);
        Assert.Equal(28, target.OrbOutcomes["ORB.FROST"].BlockGained);
        Assert.Equal(21, target.TotalOstyHpAttackBonus);
        Assert.Equal(3, target.TimesOstyHpAttackBonusApplied);
        Assert.Equal(3, target.TimesOstySummoned);
        Assert.Equal(25m, target.TotalOstyHpSummoned);
        Assert.Equal(5, target.TimesReplayExtraPlanned);
        Assert.Equal(4, target.TimesReplayExtraPlayed);
        Assert.Equal(3, target.TimesReplayAttackNoDamage);
        Assert.Equal(2, target.ReplayExtraPlayPlannedReasons["replay"].Count);
        Assert.Equal(3, target.ReplayExtraPlayPlannedReasons["power:POWER.BURST"].Count);
        Assert.Equal(1, target.ReplayExtraPlayReasons["replay"].Count);
        Assert.Equal(3, target.ReplayExtraPlayReasons["power:POWER.BURST"].Count);
        Assert.Equal(1, target.ReplayAttackNoDamageReasons["replay"].Count);
        Assert.Equal(2, target.ReplayAttackNoDamageReasons["power:POWER.BURST"].Count);
    }
}
