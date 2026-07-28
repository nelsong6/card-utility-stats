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

        Assert.Equal(1, agg.PrayerWheelExtraRewardScreens);
        Assert.Equal(1, agg.PrayerWheelExtraRewardScreensRejected);
        Assert.Equal(2, agg.CommonCardsOffered);
        Assert.Equal(1, agg.UncommonCardsOffered);
        Assert.Equal(1, agg.RareCardsOffered);
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

        Assert.Contains("\"prayer_wheel_extra_reward_screens\":4", json);
        Assert.Contains(
            "\"prayer_wheel_extra_reward_screens_rejected\":1",
            json);
        Assert.Contains("\"common_cards_offered\":7", json);
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
    }

    [Fact]
    public void Tooltip_ShowsRequestedRowsAndRarityIcons()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("Extra reward screens", body);
        Assert.Contains("Times extra reward screen rejected", body);
        Assert.Contains("Commons offered", body);
        Assert.Contains("Uncommons offered", body);
        Assert.Contains("Rares offered", body);
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
    }

    private static RelicAggregate PopulatedAggregate()
        => new()
        {
            PrayerWheelExtraRewardScreens = 4,
            PrayerWheelExtraRewardScreensRejected = 1,
            CommonCardsOffered = 7,
            UncommonCardsOffered = 4,
            RareCardsOffered = 1,
        };

    private static void AssertPopulated(RelicAggregate agg)
    {
        Assert.Equal(4, agg.PrayerWheelExtraRewardScreens);
        Assert.Equal(1, agg.PrayerWheelExtraRewardScreensRejected);
        Assert.Equal(7, agg.CommonCardsOffered);
        Assert.Equal(4, agg.UncommonCardsOffered);
        Assert.Equal(1, agg.RareCardsOffered);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildBodyMethod.Invoke(null, [agg])
            ?? throw new InvalidOperationException(
                "BuildPrayerWheelBodyBBCode returned null."));
}
