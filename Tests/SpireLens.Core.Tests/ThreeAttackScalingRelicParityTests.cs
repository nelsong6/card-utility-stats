using System;
using System.Reflection;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public sealed class ThreeAttackScalingRelicParityTests
{
    private static readonly MethodInfo BuildKunaiBodyMethod = GetBuilder(
        "BuildKunaiBodyBBCodeWithLiveActivations");
    private static readonly MethodInfo BuildShurikenBodyMethod = GetBuilder(
        "BuildShurikenBodyBBCodeWithLiveActivations");

    [Fact]
    public void KunaiAndShuriken_TooltipsStayInSyncExceptScalingAttribute()
    {
        var kunai = new RelicAggregate
        {
            KunaiAttacksPlayed = 17,
            Activations = 5,
            KunaiDexterityGained = 5,
            ThreeAttackScalingRateActivations = 4,
            ThreeAttackScalingTurns = 6,
            ThreeAttackScalingCombats = 2,
            KunaiTurnsEndedAt1Charge = 2,
            KunaiTurnsEndedAt2Charges = 2,
            KunaiTurnEndChargeTotal = 6,
            KunaiTurnEndChargeCount = 6,
        };
        var shuriken = new RelicAggregate
        {
            ShurikenAttacksPlayed = 17,
            Activations = 5,
            StrengthAdded = 5,
            ThreeAttackScalingRateActivations = 4,
            ThreeAttackScalingTurns = 6,
            ThreeAttackScalingCombats = 2,
            ShurikenTurnsEndedAt1Charge = 2,
            ShurikenTurnsEndedAt2Charges = 2,
            ShurikenTurnEndChargeTotal = 6,
            ShurikenTurnEndChargeCount = 6,
        };
        var liveCounts = new RelicLiveActivationCounts(ThisTurn: 2, ThisCombat: 4);

        var kunaiBody = BuildBody(BuildKunaiBodyMethod, kunai, liveCounts);
        var shurikenBody = BuildBody(BuildShurikenBodyMethod, shuriken, liveCounts);

        Assert.Equal(
            NormalizeScalingAttribute(kunaiBody),
            NormalizeScalingAttribute(shurikenBody));
        Assert.Contains("Avg activations per turn", kunaiBody);
        Assert.Contains("Avg activations per combat", kunaiBody);
        Assert.Contains("Turns ended at 0 charges", kunaiBody);
        Assert.Contains("[b]0.67[/b]", kunaiBody);
        Assert.Contains("[b]2[/b]", kunaiBody);
    }

    [Fact]
    public void KunaiAndShuriken_ActivationHelpersShareTheSameRateWindow()
    {
        var kunai = new RelicAggregate();
        var shuriken = new RelicAggregate();

        RunTracker.RecordKunaiActivationForTest(kunai, dexterityGained: 1);
        RunTracker.RecordShurikenActivationForTest(shuriken, strengthGained: 1);
        RunTracker.RecordThreeAttackScalingTurnForTest(kunai, count: 3);
        RunTracker.RecordThreeAttackScalingTurnForTest(shuriken, count: 3);
        RunTracker.RecordThreeAttackScalingCombatForTest(kunai, count: 2);
        RunTracker.RecordThreeAttackScalingCombatForTest(shuriken, count: 2);

        Assert.Equal(
            kunai.ThreeAttackScalingRateActivations,
            shuriken.ThreeAttackScalingRateActivations);
        Assert.Equal(kunai.ThreeAttackScalingTurns, shuriken.ThreeAttackScalingTurns);
        Assert.Equal(kunai.ThreeAttackScalingCombats, shuriken.ThreeAttackScalingCombats);
        Assert.Equal(1, kunai.ThreeAttackScalingRateActivations);
        Assert.Equal(3, kunai.ThreeAttackScalingTurns);
        Assert.Equal(2, kunai.ThreeAttackScalingCombats);
    }

    [Fact]
    public void SharedRateWindow_MergesAdditively()
    {
        var target = new RelicAggregate
        {
            ThreeAttackScalingRateActivations = 2,
            ThreeAttackScalingTurns = 4,
            ThreeAttackScalingCombats = 1,
        };
        var source = new RelicAggregate
        {
            ThreeAttackScalingRateActivations = 3,
            ThreeAttackScalingTurns = 5,
            ThreeAttackScalingCombats = 2,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(5, target.ThreeAttackScalingRateActivations);
        Assert.Equal(9, target.ThreeAttackScalingTurns);
        Assert.Equal(3, target.ThreeAttackScalingCombats);
    }

    private static string BuildBody(
        MethodInfo builder,
        RelicAggregate aggregate,
        RelicLiveActivationCounts liveCounts)
        => (string)(builder.Invoke(null, new object?[] { aggregate, liveCounts })
            ?? throw new InvalidOperationException($"{builder.Name} returned null."));

    private static MethodInfo GetBuilder(string name)
        => typeof(RelicHoverShowPatch).GetMethod(
               name,
               BindingFlags.NonPublic | BindingFlags.Static)
           ?? throw new InvalidOperationException($"{name} not found.");

    private static string NormalizeScalingAttribute(string body)
        => body
            .Replace("Dexterity", "Attribute", StringComparison.Ordinal)
            .Replace("dexterity", "attribute", StringComparison.Ordinal)
            .Replace("Strength", "Attribute", StringComparison.Ordinal)
            .Replace("strength", "attribute", StringComparison.Ordinal);
}
