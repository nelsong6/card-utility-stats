using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class PetrifiedToadStatsTests
{
    private const string PetrifiedToadRelicId = "RELIC.PETRIFIED_TOAD";

    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildPetrifiedToadBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildPetrifiedToadBodyBBCode not found.");

    [Fact]
    public void RelicAggregate_PetrifiedToadFields_JsonRoundtripPreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[PetrifiedToadRelicId] = new RelicAggregate
        {
            PetrifiedToadPotionsGiven = 4,
            PetrifiedToadPotionsBlockedByFullBelt = 3,
        };

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[PetrifiedToadRelicId];
        Assert.Equal(4, agg.PetrifiedToadPotionsGiven);
        Assert.Equal(3, agg.PetrifiedToadPotionsBlockedByFullBelt);
    }

    [Fact]
    public void PotionResults_CountSuccessAndOnlyTooFullFailures()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordPetrifiedToadPotionResultForTest(
            agg, success: true, PotionProcureFailureReason.None);
        RunTracker.RecordPetrifiedToadPotionResultForTest(
            agg, success: true, PotionProcureFailureReason.None);
        RunTracker.RecordPetrifiedToadPotionResultForTest(
            agg, success: false, PotionProcureFailureReason.TooFull);
        RunTracker.RecordPetrifiedToadPotionResultForTest(
            agg, success: false, PotionProcureFailureReason.TooFull);
        RunTracker.RecordPetrifiedToadPotionResultForTest(
            agg, success: false, PotionProcureFailureReason.NotAllowed);

        Assert.Equal(2, agg.PetrifiedToadPotionsGiven);
        Assert.Equal(2, agg.PetrifiedToadPotionsBlockedByFullBelt);
    }

    [Fact]
    public void MergeRelicAggregateInto_PetrifiedToadFields_Accumulate()
    {
        var target = new RelicAggregate
        {
            PetrifiedToadPotionsGiven = 2,
            PetrifiedToadPotionsBlockedByFullBelt = 1,
        };
        var source = new RelicAggregate
        {
            PetrifiedToadPotionsGiven = 3,
            PetrifiedToadPotionsBlockedByFullBelt = 4,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(5, target.PetrifiedToadPotionsGiven);
        Assert.Equal(5, target.PetrifiedToadPotionsBlockedByFullBelt);
    }

    [Fact]
    public void Tooltip_ShowsGivenAndFullBeltBlockedPotions()
    {
        var relic = (PetrifiedToad)RuntimeHelpers.GetUninitializedObject(
            typeof(PetrifiedToad));
        var agg = new RelicAggregate
        {
            PetrifiedToadPotionsGiven = 4,
            PetrifiedToadPotionsBlockedByFullBelt = 3,
        };

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            agg,
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Petrified Toad", title);
        Assert.Contains("Potions given", body);
        Assert.Contains("blocked by full belt", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Equal(BuildBody(agg), body);
    }

    [Fact]
    public void OlderShapeWithoutPetrifiedToadFields_DefaultsToZero()
    {
        var agg = JsonSerializer.Deserialize<RelicAggregate>("{}", RunStorage.Options);

        Assert.NotNull(agg);
        Assert.Equal(0, agg!.PetrifiedToadPotionsGiven);
        Assert.Equal(0, agg.PetrifiedToadPotionsBlockedByFullBelt);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildPetrifiedToadBodyBBCode returned null."));
}
