using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class RedSkullStatsTests
{
    private const string RedSkullRelicId = "RELIC.RED_SKULL";

    private static readonly MethodInfo BuildRedSkullBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildRedSkullBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildRedSkullBodyBBCode not found.");

    [Fact]
    public void RelicAggregate_RedSkullFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.RedSkullAttacksPlayedWhileActive);
        Assert.Equal(0, agg.RedSkullHitsWhileActive);
        Assert.Equal(0, agg.RedSkullActiveTurns);
        Assert.Equal(0, agg.RedSkullActiveCombats);
    }

    [Fact]
    public void RelicAggregate_RedSkullFields_RoundtripAndMerge()
    {
        var run = new RunData();
        run.RelicAggregates[RedSkullRelicId] = new RelicAggregate
        {
            RedSkullAttacksPlayedWhileActive = 4,
            RedSkullHitsWhileActive = 7,
            RedSkullActiveTurns = 2,
            RedSkullActiveCombats = 1,
        };

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);
        var target = restored!.RelicAggregates[RedSkullRelicId];
        var source = new RelicAggregate
        {
            RedSkullAttacksPlayedWhileActive = 5,
            RedSkullHitsWhileActive = 10,
            RedSkullActiveTurns = 3,
            RedSkullActiveCombats = 2,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        AssertPopulatedAggregate(target);
    }

    [Fact]
    public void RunTracker_RedSkullHelpers_AccumulateAndRejectNegativeCounts()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordRedSkullAttackPlayedForTest(agg, 9);
        RunTracker.RecordRedSkullAttackHitForTest(agg, 17);
        RunTracker.RecordRedSkullActiveTurnForTest(agg, 5);
        RunTracker.RecordRedSkullActiveCombatForTest(agg, 3);
        RunTracker.RecordRedSkullAttackPlayedForTest(agg, -1);
        RunTracker.RecordRedSkullAttackHitForTest(agg, -1);
        RunTracker.RecordRedSkullActiveTurnForTest(agg, -1);
        RunTracker.RecordRedSkullActiveCombatForTest(agg, -1);

        AssertPopulatedAggregate(agg);
    }

    [Fact]
    public void RelicTooltip_RedSkull_UsesActivePeriodDenominators()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("The number of Attack cards played while this relic was active.", body);
        Assert.Contains("Avg attacks played while active per turn", body);
        Assert.Contains("Avg attacks played while active per combat", body);
        Assert.Contains("Hits while active", body);
        Assert.Contains("Avg hits while active per turn", body);
        Assert.Contains("Avg hits while active per combat", body);
        Assert.Contains("[b]9[/b]", body);
        Assert.Contains("[b]1.8[/b]", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[b]17[/b]", body);
        Assert.Contains("[b]3.4[/b]", body);
        Assert.Contains("[b]5.67[/b]", body);
    }

    [Fact]
    public void RelicTooltip_RedSkull_UsesWidePanel()
    {
        var relic = (RedSkull)RuntimeHelpers.GetUninitializedObject(typeof(RedSkull));

        Assert.Equal(500f, RelicHoverShowPatch.GetPreferredStatsTooltipWidth(relic));
    }

    private static RelicAggregate PopulatedAggregate()
        => new()
        {
            RedSkullAttacksPlayedWhileActive = 9,
            RedSkullHitsWhileActive = 17,
            RedSkullActiveTurns = 5,
            RedSkullActiveCombats = 3,
        };

    private static void AssertPopulatedAggregate(RelicAggregate agg)
    {
        Assert.Equal(9, agg.RedSkullAttacksPlayedWhileActive);
        Assert.Equal(17, agg.RedSkullHitsWhileActive);
        Assert.Equal(5, agg.RedSkullActiveTurns);
        Assert.Equal(3, agg.RedSkullActiveCombats);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildRedSkullBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildRedSkullBodyBBCode returned null."));
}
