using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class LostWispStatsTests
{
    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildLostWispBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildLostWispBodyBBCode not found.");

    [Fact]
    public void DamageRecording_UsesResolvedMultiTargetOutcomes()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordLostWispDamageForTest(
            agg,
            new[]
            {
                (BlockedDamage: 2, UnblockedDamage: 5, OverkillDamage: 0, WasTargetKilled: false),
                (BlockedDamage: 0, UnblockedDamage: 3, OverkillDamage: 4, WasTargetKilled: true),
            });

        Assert.Equal(14, agg.TotalDamageAttempted);
        Assert.Equal(8, agg.TotalDamageDealt);
        Assert.Equal(2, agg.TotalDamageBlocked);
        Assert.Equal(4, agg.TotalDamageOverkill);
        Assert.Equal(2, agg.TotalTargets);
        Assert.Equal(1, agg.Kills);
    }

    [Fact]
    public void Tooltip_UsesPowerPlaysAsDamageAverageDenominator()
    {
        var agg = new RelicAggregate
        {
            Activations = 3,
            TotalDamageAttempted = 18,
            TotalDamageDealt = 12,
            TotalDamageBlocked = 2,
            TotalDamageOverkill = 4,
            TotalTargets = 5,
            Kills = 1,
        };

        var body = (string)(BuildBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildLostWispBodyBBCode returned null."));

        Assert.Contains("Power cards played", body);
        Assert.Contains("Damage attempted", body);
        Assert.Contains("Damage dealt", body);
        Assert.Contains("Damage blocked", body);
        Assert.Contains("Overkill", body);
        Assert.Contains("Kills", body);
        Assert.Contains("Targets hit", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[b]4[/b]", body);
    }

    [Fact]
    public void TooltipDispatch_RecognizesLostWisp()
    {
        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            (LostWisp)RuntimeHelpers.GetUninitializedObject(typeof(LostWisp)),
            new RelicAggregate { Activations = 1 },
            floorCount: null,
            bloodSoakedRoseCurseAgg: null,
            cursedPearlCurseAgg: null,
            neowsBonesCurseAggs: null,
            storybookBrightestFlameAgg: null,
            out var title,
            out _);

        Assert.True(recognized);
        Assert.Equal("Lost Wisp", title);
    }
}
