using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class EmberTeaStatsTests
{
    private const string EmberTeaRelicId = "RELIC.EMBER_TEA";

    private static readonly MethodInfo BuildEmberTeaBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildEmberTeaBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildEmberTeaBodyBBCode not found.");

    [Fact]
    public void Patch_TargetsEmberTeaAfterRoomEnteredWithExpectedParameter()
    {
        var target = typeof(EmberTea).GetMethod(nameof(EmberTea.AfterRoomEntered));

        Assert.NotNull(target);
        Assert.Equal(
            new[] { typeof(AbstractRoom) },
            target!.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void RelicAggregate_EmberTeaFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.EmberTeaAttacksPlayedWhileActive);
        Assert.Equal(0, agg.EmberTeaHitsWhileActive);
        Assert.Equal(0, agg.EmberTeaActiveTurns);
        Assert.Equal(0, agg.EmberTeaActiveCombats);
    }

    [Fact]
    public void RelicAggregate_EmberTeaFields_JsonRoundtripPreservesValues()
    {
        var run = new RunData();
        run.RelicAggregates[EmberTeaRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"ember_tea_attacks_played_while_active\"", json);
        Assert.Contains("\"ember_tea_hits_while_active\"", json);
        Assert.Contains("\"ember_tea_active_turns\"", json);
        Assert.Contains("\"ember_tea_active_combats\"", json);
        Assert.NotNull(restored);

        AssertPopulatedAggregate(restored!.RelicAggregates[EmberTeaRelicId]);
    }

    [Fact]
    public void RunTracker_EmberTeaHelpers_AccumulateAndClamp()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordEmberTeaAttackPlayedForTest(agg, 14);
        RunTracker.RecordEmberTeaAttackHitForTest(agg, 22);
        RunTracker.RecordEmberTeaActiveTurnForTest(agg, 6);
        RunTracker.RecordEmberTeaActiveCombatForTest(agg, 2);
        RunTracker.RecordEmberTeaAttackPlayedForTest(agg, -1);
        RunTracker.RecordEmberTeaAttackHitForTest(agg, -1);
        RunTracker.RecordEmberTeaActiveTurnForTest(agg, -1);
        RunTracker.RecordEmberTeaActiveCombatForTest(agg, -1);

        AssertPopulatedAggregate(agg);
    }

    [Fact]
    public void RelicAggregate_EmberTeaFields_Merge()
    {
        var target = new RelicAggregate
        {
            EmberTeaAttacksPlayedWhileActive = 5,
            EmberTeaHitsWhileActive = 8,
            EmberTeaActiveTurns = 2,
            EmberTeaActiveCombats = 1,
        };
        var source = new RelicAggregate
        {
            EmberTeaAttacksPlayedWhileActive = 9,
            EmberTeaHitsWhileActive = 14,
            EmberTeaActiveTurns = 4,
            EmberTeaActiveCombats = 1,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        AssertPopulatedAggregate(target);
    }

    [Fact]
    public void RelicTooltip_EmberTea_ShowsActiveTotalsAndAveragesInRequestedOrder()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("Attacks played while active", body);
        Assert.Contains("Avg attacks played per turn while active", body);
        Assert.Contains("Avg attacks played per combat while active", body);
        Assert.Contains("Hits while active", body);
        Assert.Contains("Avg hits per turn while active", body);
        Assert.Contains("Avg hits per combat while active", body);
        Assert.Contains("[b]14[/b]", body);
        Assert.Contains("[b]2.33[/b]", body);
        Assert.Contains("[b]7[/b]", body);
        Assert.Contains("[b]22[/b]", body);
        Assert.Contains("[b]3.67[/b]", body);
        Assert.Contains("[b]11[/b]", body);

        Assert.True(
            body.IndexOf("Hits while active", StringComparison.Ordinal)
            < body.IndexOf("Avg hits per turn while active", StringComparison.Ordinal));
    }

    [Fact]
    public void RelicTooltip_EmberTea_ShowsZeroAveragesWithoutActiveDenominators()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Attacks played while active", body);
        Assert.Contains("Hits while active", body);
        Assert.Equal(6, CountOccurrences(body, "[b]0[/b]"));
    }

    [Fact]
    public void RelicTooltip_EmberTea_UsesWidePanel()
    {
        var relic = (EmberTea)RuntimeHelpers.GetUninitializedObject(typeof(EmberTea));

        Assert.Equal(500f, RelicHoverShowPatch.GetPreferredStatsTooltipWidth(relic));
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void RelicTooltip_EmberTea_DispatchesForModel()
    {
        var relic = (EmberTea)RuntimeHelpers.GetUninitializedObject(typeof(EmberTea));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            PopulatedAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Ember Tea", title);
        Assert.Contains("Hits while active", body);
    }

    private static RelicAggregate PopulatedAggregate()
        => new()
        {
            EmberTeaAttacksPlayedWhileActive = 14,
            EmberTeaHitsWhileActive = 22,
            EmberTeaActiveTurns = 6,
            EmberTeaActiveCombats = 2,
        };

    private static void AssertPopulatedAggregate(RelicAggregate agg)
    {
        Assert.Equal(14, agg.EmberTeaAttacksPlayedWhileActive);
        Assert.Equal(22, agg.EmberTeaHitsWhileActive);
        Assert.Equal(6, agg.EmberTeaActiveTurns);
        Assert.Equal(2, agg.EmberTeaActiveCombats);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildEmberTeaBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildEmberTeaBodyBBCode returned null."));

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
