using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class PrayerWheelStatsTests
{
    private const string RelicId = "RELIC.PRAYER_WHEEL";

    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildPrayerWheelBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "BuildPrayerWheelBodyBBCode not found.");

    [Fact]
    public void TrackingMath_CountsScreensRejectionsAndOfferedRarities()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordPrayerWheelExtraRewardScreenForTest(agg);
        RunTracker.RecordPrayerWheelOffersForTest(
            agg,
            [
                CardRarity.Common,
                CardRarity.Common,
                CardRarity.Uncommon,
                CardRarity.Rare,
                CardRarity.Basic,
            ]);
        RunTracker.RecordPrayerWheelRewardRejectedForTest(agg);
        RunTracker.RecordPrayerWheelTakenForTest(
            agg,
            CardRarity.Common,
            2);
        RunTracker.RecordPrayerWheelTakenForTest(
            agg,
            CardRarity.Uncommon);
        RunTracker.RecordPrayerWheelTakenForTest(
            agg,
            CardRarity.Rare);

        Assert.Equal(1, agg.PrayerWheelExtraRewardScreens);
        Assert.Equal(1, agg.PrayerWheelExtraRewardScreensRejected);
        Assert.Equal(2, agg.CommonCardsOffered);
        Assert.Equal(1, agg.UncommonCardsOffered);
        Assert.Equal(1, agg.RareCardsOffered);
        Assert.Equal(2, agg.CommonCardsTaken);
        Assert.Equal(1, agg.UncommonCardsTaken);
        Assert.Equal(1, agg.RareCardsTaken);
    }

    [Fact]
    public void RelicAggregate_JsonRoundtripPreservesPrayerWheelFields()
    {
        var run = new RunData();
        run.RelicAggregates[RelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(
            json,
            RunStorage.Options);

        Assert.NotNull(restored);
        AssertPopulated(restored!.RelicAggregates[RelicId]);
    }

    [Fact]
    public void MergeRelicAggregateInto_AccumulatesPrayerWheelFields()
    {
        var target = PopulatedAggregate();

        RunTracker.MergeRelicAggregateInto(target, PopulatedAggregate());

        Assert.Equal(8, target.PrayerWheelExtraRewardScreens);
        Assert.Equal(2, target.PrayerWheelExtraRewardScreensRejected);
        Assert.Equal(14, target.CommonCardsOffered);
        Assert.Equal(8, target.UncommonCardsOffered);
        Assert.Equal(2, target.RareCardsOffered);
        Assert.Equal(6, target.CommonCardsTaken);
        Assert.Equal(2, target.UncommonCardsTaken);
        Assert.Equal(2, target.RareCardsTaken);
    }

    [Fact]
    public void Tooltip_ShowsRequestedRowsAndRarityIcons()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("Extra reward screens", body);
        Assert.Contains("Times extra reward screen rejected", body);
        Assert.Contains(
            StatConceptGlossary.RenderHintedGlyph("offered"),
            body);
        Assert.Contains(
            StatConceptGlossary.RenderHintedGlyph("taken"),
            body);
        Assert.Contains("Commons offered/taken", body);
        Assert.Contains("Uncommons offered/taken", body);
        Assert.Contains("Rares offered/taken", body);
        Assert.Contains("[b]7/3[/b]", body);
        Assert.Contains("[b]4/1[/b]", body);
        Assert.Contains("[b]1/1[/b]", body);
        Assert.Contains("color=#87CEEB", body);
        Assert.Contains("color=#EFC850", body);
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void TooltipDispatch_RecognizesPrayerWheel()
    {
        var relic = (PrayerWheel)
            RuntimeHelpers.GetUninitializedObject(typeof(PrayerWheel));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Prayer Wheel", title);
        Assert.Contains("Extra reward screens", body);
    }

    [Fact]
    public void OlderShape_DefaultsPrayerWheelFieldsToZero()
    {
        var agg = JsonSerializer.Deserialize<RelicAggregate>(
            "{}",
            RunStorage.Options);

        Assert.NotNull(agg);
        Assert.Equal(0, agg!.PrayerWheelExtraRewardScreens);
        Assert.Equal(0, agg.PrayerWheelExtraRewardScreensRejected);
        Assert.Equal(0, agg.CommonCardsOffered);
        Assert.Equal(0, agg.CommonCardsTaken);
    }

    private static RelicAggregate PopulatedAggregate()
        => new()
        {
            PrayerWheelExtraRewardScreens = 4,
            PrayerWheelExtraRewardScreensRejected = 1,
            CommonCardsOffered = 7,
            UncommonCardsOffered = 4,
            RareCardsOffered = 1,
            CommonCardsTaken = 3,
            UncommonCardsTaken = 1,
            RareCardsTaken = 1,
        };

    private static void AssertPopulated(RelicAggregate agg)
    {
        Assert.Equal(4, agg.PrayerWheelExtraRewardScreens);
        Assert.Equal(1, agg.PrayerWheelExtraRewardScreensRejected);
        Assert.Equal(7, agg.CommonCardsOffered);
        Assert.Equal(4, agg.UncommonCardsOffered);
        Assert.Equal(1, agg.RareCardsOffered);
        Assert.Equal(3, agg.CommonCardsTaken);
        Assert.Equal(1, agg.UncommonCardsTaken);
        Assert.Equal(1, agg.RareCardsTaken);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildBodyMethod.Invoke(null, [agg])
            ?? throw new InvalidOperationException(
                "BuildPrayerWheelBodyBBCode returned null."));
}
