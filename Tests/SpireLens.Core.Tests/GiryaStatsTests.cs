using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class GiryaStatsTests
{
    private const string RelicId = "RELIC.GIRYA";

    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildGiryaBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildGiryaBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void HarmonyTarget_MatchesGiryaCombatEntryCallback()
    {
        var method = typeof(Girya).GetMethod(
            nameof(Girya.AfterRoomEntered),
            new[] { typeof(AbstractRoom) });

        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);
        Assert.Equal("room", method.GetParameters().Single().Name);
    }

    [Fact]
    public void Aggregate_JsonRoundtripPreservesGiryaStats()
    {
        var run = new RunData();
        run.RelicAggregates[RelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, SerializerOptions);
        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.Contains("girya_strength_rate_added", json);
        Assert.Contains("girya_count_floor_total", json);
        Assert.Contains("girya_use_floor_distance_total", json);
        Assert.Contains("girya_last_observed_lift_count", json);
        Assert.DoesNotContain("girya_strength_added_this_combat", json);
        Assert.NotNull(restored);
        AssertAggregate(restored!.RelicAggregates[RelicId]);
    }

    [Fact]
    public void TrackingHelpersWeightCountsByFloorAndMeasureEachLiftGap()
    {
        var agg = new RelicAggregate();
        RunTracker.RecordRelicFloorAcquiredForTest(agg, 5);

        Assert.True(RunTracker.RecordGiryaFloorSampleForTest(agg, 5, 0));
        Assert.False(RunTracker.RecordGiryaFloorSampleForTest(agg, 5, 0));
        Assert.True(RunTracker.RecordGiryaFloorSampleForTest(agg, 6, 0));
        Assert.True(RunTracker.RecordGiryaFloorSampleForTest(agg, 7, 0));
        Assert.True(RunTracker.RecordGiryaLiftForTest(agg, 7, 1));
        Assert.False(RunTracker.RecordGiryaLiftForTest(agg, 7, 1));
        Assert.True(RunTracker.RecordGiryaFloorSampleForTest(agg, 8, 1));
        Assert.True(RunTracker.RecordGiryaFloorSampleForTest(agg, 9, 1));
        Assert.True(RunTracker.RecordGiryaFloorSampleForTest(agg, 10, 1));
        Assert.True(RunTracker.RecordGiryaLiftForTest(agg, 10, 2));
        Assert.True(RunTracker.RecordGiryaFloorSampleForTest(agg, 11, 2));
        Assert.True(RunTracker.RecordGiryaFloorSampleForTest(agg, 12, 2));
        Assert.True(RunTracker.RecordGiryaFloorSampleForTest(agg, 13, 2));
        Assert.True(RunTracker.RecordGiryaFloorSampleForTest(agg, 14, 2));
        Assert.True(RunTracker.RecordGiryaLiftForTest(agg, 14, 3));

        RunTracker.RecordGiryaStrengthGainForTest(agg, 1);
        RunTracker.RecordGiryaStrengthGainForTest(agg, 2);
        RunTracker.RecordGiryaStrengthCombatForTest(agg, 4);

        AssertAggregate(agg);
    }

    [Fact]
    public void MergeRelicAggregateIntoCombinesRatesAndKeepsLatestSnapshots()
    {
        var target = new RelicAggregate
        {
            Activations = 1,
            StrengthAdded = 1,
            GiryaStrengthRateAdded = 1,
            GiryaStrengthCombats = 1,
            GiryaCountFloorTotal = 3,
            GiryaFloorSamples = 4,
            GiryaLastFloorSampled = 8,
            GiryaUseFloorDistanceTotal = 2,
            GiryaUseFloorDistanceSamples = 1,
            GiryaLastUseFloor = 7,
            GiryaLastObservedLiftCount = 1,
        };
        var source = new RelicAggregate
        {
            Activations = 2,
            StrengthAdded = 5,
            GiryaStrengthRateAdded = 5,
            GiryaStrengthCombats = 3,
            GiryaCountFloorTotal = 8,
            GiryaFloorSamples = 6,
            GiryaLastFloorSampled = 14,
            GiryaUseFloorDistanceTotal = 7,
            GiryaUseFloorDistanceSamples = 2,
            GiryaLastUseFloor = 14,
            GiryaLastObservedLiftCount = 3,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(3, target.Activations);
        Assert.Equal(6m, target.StrengthAdded);
        Assert.Equal(6m, target.GiryaStrengthRateAdded);
        Assert.Equal(4, target.GiryaStrengthCombats);
        Assert.Equal(11, target.GiryaCountFloorTotal);
        Assert.Equal(10, target.GiryaFloorSamples);
        Assert.Equal(14, target.GiryaLastFloorSampled);
        Assert.Equal(9, target.GiryaUseFloorDistanceTotal);
        Assert.Equal(3, target.GiryaUseFloorDistanceSamples);
        Assert.Equal(14, target.GiryaLastUseFloor);
        Assert.Equal(3, target.GiryaLastObservedLiftCount);
    }

    [Fact]
    public void TooltipShowsStrengthFamilyAndGiryaPacingRows()
    {
        var agg = PopulatedAggregate();
        agg.GiryaStrengthAddedThisCombat = 2m;

        var body = BuildBody(agg);

        Assert.Contains("Times activated", body);
        Assert.Contains("Activated this combat", body);
        Assert.Contains("Total strength gained", body);
        Assert.Contains("Strength gained this combat", body);
        Assert.Contains("Avg strength gained per activation", body);
        Assert.Contains("Avg strength gained per combat", body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("average"), body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("charge"), body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("floor"), body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("activation"), body);
        Assert.Contains("Girya Lift count per sampled floor", body);
        Assert.Contains("consecutive successful Lifts", body);
        Assert.Contains("[b]1.1[/b]", body);
        Assert.Contains("[b]3[/b]", body);
    }

    [Fact]
    public void TooltipDispatchesForGirya()
    {
        var girya = (Girya)RuntimeHelpers.GetUninitializedObject(typeof(Girya));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            girya,
            PopulatedAggregate(),
            floorCount: null,
            bloodSoakedRoseCurseAgg: null,
            cursedPearlCurseAgg: null,
            neowsBonesCurseAggs: null,
            storybookBrightestFlameAgg: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Girya", title);
        Assert.Contains("Total strength gained", body);
        Assert.Contains("Lift", body);
    }

    [Fact]
    public void OlderAggregateWithoutGiryaFieldsDefaultsToZero()
    {
        var agg = JsonSerializer.Deserialize<RelicAggregate>(
            """{"activations":2,"strength_added":4}""",
            SerializerOptions);

        Assert.NotNull(agg);
        Assert.Equal(0m, agg!.GiryaStrengthRateAdded);
        Assert.Equal(0, agg.GiryaStrengthCombats);
        Assert.Equal(0, agg.GiryaFloorSamples);
        Assert.Equal(0, agg.GiryaUseFloorDistanceSamples);
        Assert.Null(agg.GiryaLastFloorSampled);
        Assert.Null(agg.GiryaLastUseFloor);
        Assert.Equal(0, agg.GiryaLastObservedLiftCount);
    }

    private static RelicAggregate PopulatedAggregate() => new()
    {
        FloorAcquired = 5,
        Activations = 3,
        StrengthAdded = 6,
        GiryaStrengthRateAdded = 6,
        GiryaStrengthCombats = 4,
        GiryaCountFloorTotal = 11,
        GiryaFloorSamples = 10,
        GiryaLastFloorSampled = 14,
        GiryaUseFloorDistanceTotal = 9,
        GiryaUseFloorDistanceSamples = 3,
        GiryaLastUseFloor = 14,
        GiryaLastObservedLiftCount = 3,
    };

    private static void AssertAggregate(RelicAggregate agg)
    {
        Assert.Equal(5, agg.FloorAcquired);
        Assert.Equal(3, agg.Activations);
        Assert.Equal(6m, agg.StrengthAdded);
        Assert.Equal(6m, agg.GiryaStrengthRateAdded);
        Assert.Equal(4, agg.GiryaStrengthCombats);
        Assert.Equal(11, agg.GiryaCountFloorTotal);
        Assert.Equal(10, agg.GiryaFloorSamples);
        Assert.Equal(14, agg.GiryaLastFloorSampled);
        Assert.Equal(9, agg.GiryaUseFloorDistanceTotal);
        Assert.Equal(3, agg.GiryaUseFloorDistanceSamples);
        Assert.Equal(14, agg.GiryaLastUseFloor);
        Assert.Equal(3, agg.GiryaLastObservedLiftCount);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildGiryaBodyBBCode returned null."));
}
