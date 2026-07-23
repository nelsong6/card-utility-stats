using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class DaughterOfTheWindStatsTests
{
    private const string DaughterOfTheWindRelicId = "RELIC.DAUGHTER_OF_THE_WIND";

    private static readonly MethodInfo BuildDaughterOfTheWindBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildDaughterOfTheWindBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildDaughterOfTheWindBodyBBCode not found.");

    [Fact]
    public void Patch_TargetsDaughterOfTheWindAfterCardPlayedWithExpectedParameters()
    {
        var target = typeof(DaughterOfTheWind).GetMethod(
            nameof(DaughterOfTheWind.AfterCardPlayed));

        Assert.NotNull(target);
        Assert.Equal(
            new[] { typeof(PlayerChoiceContext), typeof(CardPlay) },
            target!.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void RelicAggregate_DaughterOfTheWindFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.AdditionalBlockGained);
        Assert.Equal(0, agg.DaughterOfTheWindTurns);
        Assert.Equal(0, agg.DaughterOfTheWindCombats);
    }

    [Fact]
    public void RelicAggregate_DaughterOfTheWindFields_JsonRoundtripPreservesValues()
    {
        var run = new RunData();
        run.RelicAggregates[DaughterOfTheWindRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"additional_block_gained\"", json);
        Assert.Contains("\"daughter_of_the_wind_turns\"", json);
        Assert.Contains("\"daughter_of_the_wind_combats\"", json);
        Assert.NotNull(restored);
        AssertPopulatedAggregate(restored!.RelicAggregates[DaughterOfTheWindRelicId]);
    }

    [Fact]
    public void RunTracker_DaughterOfTheWindHelpers_AccumulateBlockAndHeldPeriods()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordDaughterOfTheWindBlockGainedForTest(agg, 10m);
        RunTracker.RecordDaughterOfTheWindBlockGainedForTest(agg, 14m);
        RunTracker.RecordDaughterOfTheWindTurnForTest(agg, 6);
        RunTracker.RecordDaughterOfTheWindCombatForTest(agg, 3);

        AssertPopulatedAggregate(agg);
    }

    [Fact]
    public void RunTracker_DaughterOfTheWindHelpers_IgnoreNegativeValuesAndCounts()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordDaughterOfTheWindBlockGainedForTest(agg, -5m);
        RunTracker.RecordDaughterOfTheWindBlockGainedForTest(agg, 0m);
        RunTracker.RecordDaughterOfTheWindTurnForTest(agg, -2);
        RunTracker.RecordDaughterOfTheWindCombatForTest(agg, -1);

        Assert.Equal(0, agg.AdditionalBlockGained);
        Assert.Equal(0, agg.DaughterOfTheWindTurns);
        Assert.Equal(0, agg.DaughterOfTheWindCombats);
    }

    [Fact]
    public void RelicAggregate_DaughterOfTheWindFields_Merge()
    {
        var target = PopulatedAggregate();

        RunTracker.MergeRelicAggregateInto(target, PopulatedAggregate());

        Assert.Equal(48, target.AdditionalBlockGained);
        Assert.Equal(12, target.DaughterOfTheWindTurns);
        Assert.Equal(6, target.DaughterOfTheWindCombats);
    }

    [Fact]
    public void RelicTooltip_DaughterOfTheWind_ShowsTotalAndHeldPeriodAverages()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("Total block gained", body);
        Assert.Contains("[b]24[/b]", body);
        Assert.Contains("Avg block gained per turn", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.Contains("Avg block gained per combat", body);
        Assert.Contains("[b]8[/b]", body);
    }

    [Fact]
    public void RelicTooltip_DaughterOfTheWind_ShowsZeroAveragesWithoutDenominators()
    {
        var body = BuildBody(new RelicAggregate { AdditionalBlockGained = 5 });

        Assert.Contains("Total block gained", body);
        Assert.Contains("[b]5[/b]", body);
        Assert.Contains("Avg block gained per turn", body);
        Assert.Contains("Avg block gained per combat", body);
        Assert.Equal(2, CountOccurrences(body, "[b]0[/b]"));
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void RelicTooltip_DaughterOfTheWind_DispatchesForModel()
    {
        var relic = (DaughterOfTheWind)RuntimeHelpers.GetUninitializedObject(
            typeof(DaughterOfTheWind));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Daughter of the Wind", title);
        Assert.Contains("Total block gained", body);
    }

    [Fact]
    public void RelicAggregate_OlderShapeWithoutDaughterOfTheWindFields_DefaultsToZero()
    {
        var agg = JsonSerializer.Deserialize<RelicAggregate>("{}", RunStorage.Options);

        Assert.NotNull(agg);
        Assert.Equal(0, agg!.AdditionalBlockGained);
        Assert.Equal(0, agg.DaughterOfTheWindTurns);
        Assert.Equal(0, agg.DaughterOfTheWindCombats);
    }

    private static RelicAggregate PopulatedAggregate()
    {
        var agg = new RelicAggregate();
        RunTracker.RecordDaughterOfTheWindBlockGainedForTest(agg, 10m);
        RunTracker.RecordDaughterOfTheWindBlockGainedForTest(agg, 14m);
        RunTracker.RecordDaughterOfTheWindTurnForTest(agg, 6);
        RunTracker.RecordDaughterOfTheWindCombatForTest(agg, 3);
        return agg;
    }

    private static void AssertPopulatedAggregate(RelicAggregate agg)
    {
        Assert.Equal(24, agg.AdditionalBlockGained);
        Assert.Equal(6, agg.DaughterOfTheWindTurns);
        Assert.Equal(3, agg.DaughterOfTheWindCombats);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildDaughterOfTheWindBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildDaughterOfTheWindBodyBBCode returned null."));

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
