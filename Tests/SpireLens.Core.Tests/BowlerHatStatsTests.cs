using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class BowlerHatStatsTests
{
    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildBowlerHatBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "BuildBowlerHatBodyBBCode not found.");

    [Fact]
    public void Patch_TargetsCentralGoldGainCommand()
    {
        var target = typeof(PlayerCmd).GetMethod(
            nameof(PlayerCmd.GainGold),
            [typeof(decimal), typeof(Player), typeof(bool)]);

        Assert.NotNull(target);
        Assert.Equal(typeof(System.Threading.Tasks.Task), target!.ReturnType);
    }

    [Theory]
    [InlineData(100, 112, 10, 2)]
    [InlineData(100, 103, 3, 0)]
    [InlineData(100, 104, 3.5, 1)]
    [InlineData(100, 100, 10, 0)]
    [InlineData(100, 125, 20, 5)]
    public void ExtraGold_UsesCompletedBalanceBeyondUnmodifiedIntegerGrant(
        int initialGold,
        int currentGold,
        double unmodifiedAmount,
        int expected)
    {
        Assert.Equal(
            expected,
            RunTracker.CalculateBowlerHatExtraGoldForTest(
                initialGold,
                currentGold,
                (decimal)unmodifiedAmount));
    }

    [Fact]
    public void Tracking_CountsOnlyBonusesThatReachTheBalance()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordBowlerHatGoldGainForTest(agg, 2);
        RunTracker.RecordBowlerHatGoldGainForTest(agg, 5);
        RunTracker.RecordBowlerHatGoldGainForTest(agg, 0);

        Assert.Equal(2, agg.Activations);
        Assert.Equal(7, agg.GoldGained);
    }

    [Fact]
    public void Tooltip_ShowsObservedBonusAndAverage()
    {
        var body = BuildBody(new RelicAggregate
        {
            Activations = 3,
            GoldGained = 7,
        });

        Assert.Contains("integer truncation", body);
        Assert.Contains("Extra gold gained", body);
        Assert.Contains("Avg extra gold/activation", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[b]7[/b]", body);
        Assert.Contains("[b]2.33[/b]", body);
    }

    [Fact]
    public void TooltipDispatch_RecognizesBowlerHat()
    {
        var relic = (BowlerHat)RuntimeHelpers.GetUninitializedObject(
            typeof(BowlerHat));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate(),
            floorCount: null,
            out var title,
            out _);

        Assert.True(recognized);
        Assert.Equal("Bowler Hat", title);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException(
                "BuildBowlerHatBodyBBCode returned null."));
}
