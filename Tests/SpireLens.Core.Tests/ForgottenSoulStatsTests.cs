using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class ForgottenSoulStatsTests
{
    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildForgottenSoulBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildForgottenSoulBodyBBCode not found.");

    [Fact]
    public void ActivationRecording_DoesNotRequireDamageOutcome()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordForgottenSoulActivationForTest(agg, count: 2);

        Assert.Equal(2, agg.Activations);
        Assert.Equal(0, agg.TotalTargets);
        Assert.Equal(0, agg.TotalDamageDealt);
    }

    [Fact]
    public void DamageRecording_UsesResolvedSingleTargetOutcome()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordForgottenSoulDamageForTest(
            agg,
            new[]
            {
                (BlockedDamage: 3, UnblockedDamage: 5, OverkillDamage: 2, WasTargetKilled: true),
            });

        Assert.Equal(10, agg.TotalDamageAttempted);
        Assert.Equal(5, agg.TotalDamageDealt);
        Assert.Equal(3, agg.TotalDamageBlocked);
        Assert.Equal(2, agg.TotalDamageOverkill);
        Assert.Equal(1, agg.TotalTargets);
        Assert.Equal(1, agg.Kills);
    }

    [Fact]
    public void Merge_PreservesHeldRateDenominators()
    {
        var target = new RelicAggregate
        {
            ForgottenSoulTurns = 2,
            ForgottenSoulCombats = 1,
        };
        var source = new RelicAggregate
        {
            ForgottenSoulTurns = 3,
            ForgottenSoulCombats = 2,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(5, target.ForgottenSoulTurns);
        Assert.Equal(3, target.ForgottenSoulCombats);
    }

    [Fact]
    public void Tooltip_ShowsRequestedOutcomesAndHeldPeriodAverages()
    {
        var agg = new RelicAggregate
        {
            Activations = 4,
            TotalDamageDealt = 12,
            TotalDamageBlocked = 3,
            Kills = 2,
            TotalTargets = 4,
            ForgottenSoulTurns = 6,
            ForgottenSoulCombats = 2,
        };

        var body = (string)(BuildBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildForgottenSoulBodyBBCode returned null."));

        Assert.Contains("same-owner cards exhausted", body);
        Assert.Contains("Damage dealt", body);
        Assert.Contains("Damage blocked", body);
        Assert.Contains("Kills", body);
        Assert.Contains("Targets hit", body);
        Assert.Contains("Avg damage dealt per turn", body);
        Assert.Contains("Avg damage dealt per combat", body);
        Assert.Contains("[b]12[/b]", body);
        Assert.Contains("[b]6[/b]", body);
        Assert.Contains("[b]2[/b]", body);
    }

    [Fact]
    public void TooltipDispatch_RecognizesForgottenSoul()
    {
        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            (ForgottenSoul)RuntimeHelpers.GetUninitializedObject(typeof(ForgottenSoul)),
            new RelicAggregate { Activations = 1 },
            floorCount: null,
            bloodSoakedRoseCurseAgg: null,
            cursedPearlCurseAgg: null,
            neowsBonesCurseAggs: null,
            storybookBrightestFlameAgg: null,
            out var title,
            out _);

        Assert.True(recognized);
        Assert.Equal("Forgotten Soul", title);
    }
}
