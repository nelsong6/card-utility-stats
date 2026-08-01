using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class PotionSlotRelicStatsTests
{
    private static readonly MethodInfo BuildPotionSlotRelicBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildPotionSlotRelicBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildPotionSlotRelicBodyBBCode not found.");

    [Fact]
    public void CombatStartSamples_IncludeZeroPotionCombats()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordPotionSlotRelicCombatStartForTest(agg, 0);
        RunTracker.RecordPotionSlotRelicCombatStartForTest(agg, 2);
        RunTracker.RecordPotionSlotRelicCombatStartForTest(agg, 3);

        Assert.Equal(5, agg.CombatStartPotionCountTotal);
        Assert.Equal(3, agg.CombatStartPotionCountSamples);
    }

    [Fact]
    public void MergeRelicAggregateInto_AccumulatesCombatStartPotionSamples()
    {
        var target = new RelicAggregate
        {
            CombatStartPotionCountTotal = 2,
            CombatStartPotionCountSamples = 1,
        };
        var source = new RelicAggregate
        {
            CombatStartPotionCountTotal = 5,
            CombatStartPotionCountSamples = 3,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(7, target.CombatStartPotionCountTotal);
        Assert.Equal(4, target.CombatStartPotionCountSamples);
    }

    [Fact]
    public void Tooltip_ShowsOnlyRequestedZeroInclusiveAverage()
    {
        var body = BuildBody(new RelicAggregate
        {
            CombatStartPotionCountTotal = 5,
            CombatStartPotionCountSamples = 3,
        });

        Assert.Contains("held at combat start", body);
        Assert.Contains("[b]1.67[/b]", body);
        Assert.DoesNotContain("Combats held", body);
    }

    [Fact]
    public void Tooltip_DispatchesForEveryPotionSlotRelic()
    {
        var relics = new (RelicModel Relic, string Title)[]
        {
            (Uninitialized<PotionBelt>(), "Potion Belt"),
            (Uninitialized<AlchemicalCoffer>(), "Alchemical Coffer"),
            (Uninitialized<PhialHolster>(), "Phial Holster"),
        };

        foreach (var (relic, expectedTitle) in relics)
        {
            var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
                relic,
                new RelicAggregate
                {
                    CombatStartPotionCountTotal = 7,
                    CombatStartPotionCountSamples = 4,
                },
                floorCount: null,
                out var title,
                out var body);

            Assert.True(recognized);
            Assert.Equal(expectedTitle, title);
            Assert.Contains("[b]1.75[/b]", body);
        }
    }

    private static T Uninitialized<T>() where T : class
        => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildPotionSlotRelicBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildPotionSlotRelicBodyBBCode returned null."));
}
