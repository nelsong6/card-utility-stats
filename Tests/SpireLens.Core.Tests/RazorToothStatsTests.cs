using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class RazorToothStatsTests
{
    private const string RazorToothRelicId = "RELIC.RAZOR_TOOTH";

    private static readonly MethodInfo BuildRazorToothBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildRazorToothBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildRazorToothBodyBBCode not found.");

    private static readonly MethodInfo TargetMethod =
        typeof(RazorToothAfterCardPlayedPatch).GetMethod("TargetMethod", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Razor Tooth TargetMethod not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void Patch_TargetsRazorToothAfterCardPlayedWithExpectedParameters()
    {
        var target = TargetMethod.Invoke(null, null) as MethodBase;

        Assert.NotNull(target);
        Assert.Equal(typeof(RazorTooth), target!.DeclaringType);
        Assert.Equal(nameof(RazorTooth.AfterCardPlayed), target.Name);
        Assert.Equal(
            new[] { typeof(PlayerChoiceContext), typeof(CardPlay) },
            target.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void RelicAggregate_RazorToothCardsUpgraded_JsonRoundtripPreservesCount()
    {
        var run = new RunData();
        run.RelicAggregates[RazorToothRelicId] = new RelicAggregate { CardsUpgraded = 7 };

        var json = JsonSerializer.Serialize(run, SerializerOptions);
        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.Contains("cards_upgraded", json);
        Assert.NotNull(restored);
        Assert.Equal(7, restored!.RelicAggregates[RazorToothRelicId].CardsUpgraded);
    }

    [Fact]
    public void RunTracker_RazorTooth_CountsOnlyPositiveObservedUpgradeDeltas()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordRazorToothUpgradeForTest(agg, previousUpgradeLevel: 0, currentUpgradeLevel: 1);
        RunTracker.RecordRazorToothUpgradeForTest(agg, previousUpgradeLevel: 1, currentUpgradeLevel: 1);
        RunTracker.RecordRazorToothUpgradeForTest(agg, previousUpgradeLevel: 2, currentUpgradeLevel: 1);
        RunTracker.RecordRazorToothUpgradeForTest(agg, previousUpgradeLevel: 1, currentUpgradeLevel: 2);
        RunTracker.RecordRazorToothUpgradeForTest(agg, previousUpgradeLevel: 0, currentUpgradeLevel: 2);

        Assert.Equal(3, agg.CardsUpgraded);
    }

    [Fact]
    public void RelicTooltip_RazorTooth_ShowsCardsUpgraded()
    {
        var body = BuildBody(new RelicAggregate { CardsUpgraded = 4 });

        Assert.Contains("Cards upgraded", body);
        Assert.Contains("[b]4[/b]", body);
    }

    [Fact]
    public void RelicTooltip_RazorTooth_DispatchesForRazorToothModel()
    {
        var relic = (RazorTooth)RuntimeHelpers.GetUninitializedObject(typeof(RazorTooth));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate { CardsUpgraded = 4 },
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Razor Tooth", title);
        Assert.Contains("Cards upgraded", body);
        Assert.Contains("[b]4[/b]", body);
    }

    [Fact]
    public void RelicTooltip_RazorTooth_ShowsZeroBeforeAnyUpgrade()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Cards upgraded", body);
        Assert.Contains("[b]0[/b]", body);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildRazorToothBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildRazorToothBodyBBCode returned null."));
}
