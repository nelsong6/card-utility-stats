using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class SwordInTheStoneStatsTests
{
    private const string RelicId = "RELIC.SWORD_OF_STONE";

    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildSwordInTheStoneBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildSwordInTheStoneBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void HarmonyTargets_MatchEliteVictoryAndCombatEntryCallbacks()
    {
        var stoneMethod = typeof(SwordOfStone).GetMethod(
            nameof(SwordOfStone.AfterCombatVictory),
            new[] { typeof(CombatRoom) });
        var jadeMethod = typeof(SwordOfJade).GetMethod(
            nameof(SwordOfJade.AfterRoomEntered),
            new[] { typeof(AbstractRoom) });

        Assert.NotNull(stoneMethod);
        Assert.Equal(typeof(Task), stoneMethod!.ReturnType);
        Assert.Equal("room", stoneMethod.GetParameters().Single().Name);
        Assert.NotNull(jadeMethod);
        Assert.Equal(typeof(Task), jadeMethod!.ReturnType);
        Assert.Equal("room", jadeMethod.GetParameters().Single().Name);
    }

    [Fact]
    public void Aggregate_JsonRoundtripPreservesEliteHistoryAndStrength()
    {
        var run = new RunData();
        run.RelicAggregates[RelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, SerializerOptions);
        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.Contains("sword_in_the_stone_elites_slain", json);
        Assert.NotNull(restored);
        AssertAggregate(restored!.RelicAggregates[RelicId]);
    }

    [Fact]
    public void TrackingHelpersPreserveKillOrderAndObservedStrength()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordRelicFloorAcquiredForTest(agg, 8);
        RunTracker.RecordSwordOfStoneEliteSlainForTest(
            agg, 12, "ENCOUNTER.GREMLIN_NOB", "Gremlin Nob");
        RunTracker.RecordSwordOfStoneEliteSlainForTest(
            agg, 18, "ENCOUNTER.LAGAVULIN", "Lagavulin");
        RunTracker.RecordSwordOfStoneEliteSlainForTest(
            agg, 23, "ENCOUNTER.GREMLIN_LEADER", "Gremlin Leader");
        RunTracker.RecordSwordOfJadeStrengthGainForTest(agg, 3);
        RunTracker.RecordSwordOfJadeStrengthGainForTest(agg, 3);

        AssertAggregate(agg);
        Assert.Equal(
            5.5m,
            RelicHoverShowPatch.CalculateAverageFloorsPerElite(
                agg.SwordInTheStoneElitesSlain));
    }

    [Fact]
    public void MergeRelicAggregateIntoAppendsEliteHistoryAndStrength()
    {
        var target = new RelicAggregate
        {
            FloorAcquired = 8,
            Activations = 1,
            StrengthAdded = 3,
            SwordInTheStoneElitesSlain =
            {
                new SwordInTheStoneEliteSlainAggregate
                {
                    Floor = 12,
                    EncounterId = "ENCOUNTER.GREMLIN_NOB",
                    DisplayName = "Gremlin Nob",
                },
            },
        };
        var source = new RelicAggregate
        {
            Activations = 1,
            StrengthAdded = 3,
            SwordInTheStoneElitesSlain =
            {
                new SwordInTheStoneEliteSlainAggregate
                {
                    Floor = 18,
                    EncounterId = "ENCOUNTER.LAGAVULIN",
                    DisplayName = "Lagavulin",
                },
                new SwordInTheStoneEliteSlainAggregate
                {
                    Floor = 23,
                    EncounterId = "ENCOUNTER.GREMLIN_LEADER",
                    DisplayName = "Gremlin Leader",
                },
            },
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        AssertAggregate(target);
    }

    [Fact]
    public void TooltipShowsRequestedProgressionAndStrengthRows()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("Floor acquired", body);
        Assert.Contains("Elites slain", body);
        Assert.Contains("Avg floors per elite", body);
        Assert.Contains("Gremlin Nob", body);
        Assert.Contains("Lagavulin", body);
        Assert.Contains("Gremlin Leader", body);
        Assert.Contains("Strength activations", body);
        Assert.Contains("Strength gained", body);
        Assert.Contains("Avg strength gained per activation", body);
        Assert.Contains("[b]5.5[/b]", body);
    }

    [Fact]
    public void StoneAndJadeDispatchToTheSameAggregateIdentity()
    {
        var stone = (SwordOfStone)RuntimeHelpers.GetUninitializedObject(typeof(SwordOfStone));
        var jade = (SwordOfJade)RuntimeHelpers.GetUninitializedObject(typeof(SwordOfJade));

        Assert.Equal(RelicId, RelicHoverShowPatch.GetStatsAggregateId(stone));
        Assert.Equal(RelicId, RelicHoverShowPatch.GetStatsAggregateId(jade));
        AssertTooltipDispatch(stone, "Sword in the Stone");
        AssertTooltipDispatch(jade, "Sword of Jade");
    }

    private static RelicAggregate PopulatedAggregate() => new()
    {
        FloorAcquired = 8,
        Activations = 2,
        StrengthAdded = 6,
        SwordInTheStoneElitesSlain =
        {
            new SwordInTheStoneEliteSlainAggregate
            {
                Floor = 12,
                EncounterId = "ENCOUNTER.GREMLIN_NOB",
                DisplayName = "Gremlin Nob",
            },
            new SwordInTheStoneEliteSlainAggregate
            {
                Floor = 18,
                EncounterId = "ENCOUNTER.LAGAVULIN",
                DisplayName = "Lagavulin",
            },
            new SwordInTheStoneEliteSlainAggregate
            {
                Floor = 23,
                EncounterId = "ENCOUNTER.GREMLIN_LEADER",
                DisplayName = "Gremlin Leader",
            },
        },
    };

    private static void AssertAggregate(RelicAggregate agg)
    {
        Assert.Equal(8, agg.FloorAcquired);
        Assert.Equal(3, agg.SwordInTheStoneElitesSlain.Count);
        Assert.Equal("Gremlin Nob", agg.SwordInTheStoneElitesSlain[0].DisplayName);
        Assert.Equal("Gremlin Leader", agg.SwordInTheStoneElitesSlain[2].DisplayName);
        Assert.Equal(2, agg.Activations);
        Assert.Equal(6, agg.StrengthAdded);
    }

    private static void AssertTooltipDispatch(RelicModel relic, string expectedTitle)
    {
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
        Assert.Equal(expectedTitle, title);
        Assert.Contains("Avg floors per elite", body);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildBodyMethod.Invoke(null, new object?[] { agg, null })
            ?? throw new InvalidOperationException("BuildSwordInTheStoneBodyBBCode returned null."));
}
