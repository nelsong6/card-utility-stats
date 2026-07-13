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
    [Trait("Category", "RequiresLiveGame")]
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
    public void RelicAggregate_RazorToothFields_JsonRoundtripPreservesValues()
    {
        var run = new RunData();
        run.RelicAggregates[RazorToothRelicId] = new RelicAggregate
        {
            CardsUpgraded = 7,
            RazorToothCombats = 3,
            RazorToothTurns = 4,
            RazorToothUpgradedCardPlays = 5,
            RazorToothUpgradedCardDraws = 2,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);
        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.Contains("cards_upgraded", json);
        Assert.Contains("razor_tooth_combats", json);
        Assert.Contains("razor_tooth_turns", json);
        Assert.Contains("razor_tooth_upgraded_card_plays", json);
        Assert.Contains("razor_tooth_upgraded_card_draws", json);
        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[RazorToothRelicId];
        Assert.Equal(7, agg.CardsUpgraded);
        Assert.Equal(3, agg.RazorToothCombats);
        Assert.Equal(4, agg.RazorToothTurns);
        Assert.Equal(5, agg.RazorToothUpgradedCardPlays);
        Assert.Equal(2, agg.RazorToothUpgradedCardDraws);
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
    public void RunTracker_RazorToothTestHelpers_RecordDenominatorsAndFollowupEvents()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordRazorToothCombatForTest(agg, 3);
        RunTracker.RecordRazorToothTurnForTest(agg, 8);
        RunTracker.RecordRazorToothUpgradedCardPlayForTest(agg, 4);
        RunTracker.RecordRazorToothUpgradedCardDrawForTest(agg, 2);

        Assert.Equal(3, agg.RazorToothCombats);
        Assert.Equal(8, agg.RazorToothTurns);
        Assert.Equal(4, agg.RazorToothUpgradedCardPlays);
        Assert.Equal(2, agg.RazorToothUpgradedCardDraws);
    }

    [Fact]
    public void MergeRelicAggregateInto_RazorToothFields_Accumulates()
    {
        var target = new RelicAggregate
        {
            CardsUpgraded = 1,
            RazorToothCombats = 1,
            RazorToothTurns = 2,
            RazorToothUpgradedCardPlays = 3,
            RazorToothUpgradedCardDraws = 4,
        };
        var source = new RelicAggregate
        {
            CardsUpgraded = 5,
            RazorToothCombats = 2,
            RazorToothTurns = 6,
            RazorToothUpgradedCardPlays = 1,
            RazorToothUpgradedCardDraws = 2,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(6, target.CardsUpgraded);
        Assert.Equal(3, target.RazorToothCombats);
        Assert.Equal(8, target.RazorToothTurns);
        Assert.Equal(4, target.RazorToothUpgradedCardPlays);
        Assert.Equal(6, target.RazorToothUpgradedCardDraws);
    }

    [Fact]
    public void RelicTooltip_RazorTooth_ShowsCardsUpgraded()
    {
        var body = BuildBody(new RelicAggregate
        {
            CardsUpgraded = 7,
            RazorToothCombats = 3,
            RazorToothTurns = 4,
            RazorToothUpgradedCardPlays = 5,
            RazorToothUpgradedCardDraws = 2,
        });

        Assert.Contains("Cards upgraded", body);
        Assert.Contains("[b]7[/b]", body);
        Assert.Contains("Avg cards upgraded/turn", body);
        Assert.Contains("[b]1.75[/b]", body);
        Assert.Contains("Avg cards upgraded/combat", body);
        Assert.Contains("[b]2.33[/b]", body);
        Assert.Contains("Upgraded-card plays", body);
        Assert.Contains("[b]5[/b]", body);
        Assert.Contains("Avg upgraded plays/turn", body);
        Assert.Contains("[b]1.25[/b]", body);
        Assert.Contains("Avg upgraded plays/combat", body);
        Assert.Contains("[b]1.67[/b]", body);
        Assert.Contains("Upgraded-card draws", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("Avg upgraded draws/turn", body);
        Assert.Contains("[b]0.5[/b]", body);
        Assert.Contains("Avg upgraded draws/combat", body);
        Assert.Contains("[b]0.67[/b]", body);
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
        Assert.Contains("Avg cards upgraded/turn", body);
        Assert.Contains("Avg cards upgraded/combat", body);
        Assert.Contains("Upgraded-card plays", body);
        Assert.Contains("Avg upgraded plays/turn", body);
        Assert.Contains("Avg upgraded plays/combat", body);
        Assert.Contains("Upgraded-card draws", body);
        Assert.Contains("Avg upgraded draws/turn", body);
        Assert.Contains("Avg upgraded draws/combat", body);
        Assert.Contains("[b]0[/b]", body);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildRazorToothBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildRazorToothBodyBBCode returned null."));
}
