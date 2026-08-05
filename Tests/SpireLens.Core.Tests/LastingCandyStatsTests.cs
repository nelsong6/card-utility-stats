using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class LastingCandyStatsTests
{
    private const string RelicId = "RELIC.LASTING_CANDY";

    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildLastingCandyBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildLastingCandyBodyBBCode not found.");

    [Fact]
    public void NativeHook_AppendsPowerThroughCardRewardModifier()
    {
        var method = typeof(LastingCandy).GetMethod(
            nameof(LastingCandy.TryModifyCardRewardOptions));

        Assert.NotNull(method);
        Assert.Equal(typeof(bool), method!.ReturnType);
        Assert.Equal(3, method.GetParameters().Length);
        Assert.Equal("rewardOptions", method.GetParameters()[1].Name);
    }

    [Fact]
    public void RelicAggregate_FieldsDefaultToZero()
    {
        AssertAggregate(new RelicAggregate(), expectedScale: 0);
    }

    [Fact]
    public void RelicAggregate_JsonRoundtripPreservesLastingCandyStats()
    {
        var run = new RunData();
        run.RelicAggregates[RelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"lasting_candy_powers_offered\"", json);
        Assert.Contains("\"lasting_candy_powers_taken\"", json);
        Assert.Contains("\"lasting_candy_powers_rejected\"", json);
        Assert.Contains("\"lasting_candy_uncommon_powers_offered\"", json);
        Assert.Contains("\"lasting_candy_rare_powers_rejected\"", json);
        Assert.NotNull(restored);
        AssertAggregate(restored!.RelicAggregates[RelicId], expectedScale: 1);
    }

    [Fact]
    public void TrackingHelperRecordsTotalsAndRarityOutcomes()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordLastingCandyPowerOutcomeForTest(
            agg, CardRarity.Uncommon, taken: true);
        RunTracker.RecordLastingCandyPowerOutcomeForTest(
            agg, CardRarity.Uncommon, taken: false);
        RunTracker.RecordLastingCandyPowerOutcomeForTest(
            agg, CardRarity.Uncommon, taken: false);
        RunTracker.RecordLastingCandyPowerOutcomeForTest(
            agg, CardRarity.Rare, taken: true);
        RunTracker.RecordLastingCandyPowerOutcomeForTest(
            agg, CardRarity.Rare, taken: false);

        AssertAggregate(agg, expectedScale: 1);
    }

    [Fact]
    public void MergeRelicAggregateIntoAddsEveryLastingCandyBucket()
    {
        var target = PopulatedAggregate();

        RunTracker.MergeRelicAggregateInto(target, PopulatedAggregate());

        AssertAggregate(target, expectedScale: 2);
    }

    [Fact]
    public void TooltipShowsAllRequestedPowerOutcomeRowsWithIcons()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("power"), body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("power_uncommon"), body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("power_rare"), body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("offered"), body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("taken"), body);
        Assert.Contains(RenderNotTaken(), body);
        Assert.Contains("Powers offered", body);
        Assert.Contains("Powers taken", body);
        Assert.Contains("Powers rejected", body);
        Assert.Contains("Uncommon Powers offered", body);
        Assert.Contains("Rare Lasting Candy Powers rejected", body);
        Assert.Contains("[b]5[/b]", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[b]2[/b]", body);
    }

    [Fact]
    public void TooltipDispatchesForLastingCandy()
    {
        var relic = (LastingCandy)RuntimeHelpers.GetUninitializedObject(
            typeof(LastingCandy));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            PopulatedAggregate(),
            floorCount: null,
            bloodSoakedRoseCurseAgg: null,
            cursedPearlCurseAgg: null,
            neowsBonesCurseAggs: null,
            storybookBrightestFlameAgg: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Lasting Candy", title);
        Assert.Contains("Powers offered", body);
    }

    private static RelicAggregate PopulatedAggregate() => new()
    {
        LastingCandyPowersOffered = 5,
        LastingCandyPowersTaken = 2,
        LastingCandyPowersRejected = 3,
        LastingCandyUncommonPowersOffered = 3,
        LastingCandyUncommonPowersTaken = 1,
        LastingCandyUncommonPowersRejected = 2,
        LastingCandyRarePowersOffered = 2,
        LastingCandyRarePowersTaken = 1,
        LastingCandyRarePowersRejected = 1,
    };

    private static void AssertAggregate(RelicAggregate agg, int expectedScale)
    {
        Assert.Equal(5 * expectedScale, agg.LastingCandyPowersOffered);
        Assert.Equal(2 * expectedScale, agg.LastingCandyPowersTaken);
        Assert.Equal(3 * expectedScale, agg.LastingCandyPowersRejected);
        Assert.Equal(3 * expectedScale, agg.LastingCandyUncommonPowersOffered);
        Assert.Equal(1 * expectedScale, agg.LastingCandyUncommonPowersTaken);
        Assert.Equal(2 * expectedScale, agg.LastingCandyUncommonPowersRejected);
        Assert.Equal(2 * expectedScale, agg.LastingCandyRarePowersOffered);
        Assert.Equal(1 * expectedScale, agg.LastingCandyRarePowersTaken);
        Assert.Equal(1 * expectedScale, agg.LastingCandyRarePowersRejected);
    }

    private static string RenderNotTaken()
        => $"not {StatConceptGlossary.RenderHintedGlyph("taken")}";

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildLastingCandyBodyBBCode returned null."));
}
