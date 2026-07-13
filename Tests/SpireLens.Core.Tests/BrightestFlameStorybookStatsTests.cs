using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class BrightestFlameStorybookStatsTests
{
    private static readonly MethodInfo TargetMethod =
        typeof(BrightestFlameOnPlayPatch).GetMethod("TargetMethod", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Brightest Flame TargetMethod not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void Patch_TargetsBrightestFlameOnPlayWithExpectedParameters()
    {
        var target = TargetMethod.Invoke(null, null) as MethodBase;

        Assert.NotNull(target);
        Assert.Equal(typeof(BrightestFlame), target!.DeclaringType);
        Assert.Equal("OnPlay", target.Name);
        Assert.Equal(
            new[] { typeof(PlayerChoiceContext), typeof(CardPlay) },
            target.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void CardAggregate_MaxHpLost_JsonRoundtripPreservesValue()
    {
        var run = new RunData();
        run.Aggregates["CARD.BRIGHTEST_FLAME#1"] = new CardAggregate
        {
            TotalMaxHpLost = 4,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);
        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.Contains("total_max_hp_lost", json);
        Assert.NotNull(restored);
        Assert.Equal(4, restored!.Aggregates["CARD.BRIGHTEST_FLAME#1"].TotalMaxHpLost);
    }

    [Fact]
    public void RunTracker_BrightestFlame_CountsOnlyPositiveObservedMaxHpLoss()
    {
        var agg = new CardAggregate();

        RunTracker.RecordBrightestFlameMaxHpLostForTest(agg, previousMaxHp: 70, currentMaxHp: 69);
        RunTracker.RecordBrightestFlameMaxHpLostForTest(agg, previousMaxHp: 69, currentMaxHp: 69);
        RunTracker.RecordBrightestFlameMaxHpLostForTest(agg, previousMaxHp: 69, currentMaxHp: 70);
        RunTracker.RecordBrightestFlameMaxHpLostForTest(agg, previousMaxHp: 3, currentMaxHp: 1);

        Assert.Equal(3, agg.TotalMaxHpLost);
    }

    [Fact]
    public void CardAggregate_MaxHpLost_MergesAndPools()
    {
        var merged = new CardAggregate { TotalMaxHpLost = 2 };
        RunTracker.MergeAggregateInto(merged, new CardAggregate { TotalMaxHpLost = 3 });

        var pooled = CardAggregatePooler.PoolByDefinition(
            new[]
            {
                new KeyValuePair<string, CardAggregate>(
                    "CARD.BRIGHTEST_FLAME#1",
                    new CardAggregate { TotalMaxHpLost = 4 }),
                new KeyValuePair<string, CardAggregate>(
                    "CARD.BRIGHTEST_FLAME#2",
                    new CardAggregate { TotalMaxHpLost = 1 }),
            },
            "CARD.BRIGHTEST_FLAME");

        Assert.Equal(5, merged.TotalMaxHpLost);
        Assert.NotNull(pooled);
        Assert.Equal(5, pooled!.TotalMaxHpLost);
    }

    [Fact]
    public void CardTooltip_BrightestFlame_ShowsMaxHpLostInFullAndCompactViews()
    {
        var card = (BrightestFlame)RuntimeHelpers.GetUninitializedObject(typeof(BrightestFlame));
        var agg = new CardAggregate { TotalMaxHpLost = 4 };

        var full = CardHoverShowPatch.BuildHistoricalBodyBBCode(card, agg, new RunMetaStats());
        var compact = CardHoverShowPatch.BuildHistoricalBodyBBCode(
            card,
            agg,
            new RunMetaStats(),
            compact: true);

        Assert.Contains("Max HP lost", full);
        Assert.Contains("[b]4[/b]", full);
        Assert.Contains("Max HP lost", compact);
        Assert.Contains("[b]4[/b]", compact);
    }

    [Fact]
    public void RelicTooltip_Storybook_ShowsPooledBrightestFlameStats()
    {
        var storybook = (Storybook)RuntimeHelpers.GetUninitializedObject(typeof(Storybook));
        var brightestFlameAgg = new CardAggregate
        {
            Plays = 4,
            TimesDrawn = 6,
            TotalEnergyGenerated = 8,
            TimesCardsDrawn = 8,
            TotalMaxHpLost = 4,
        };

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            storybook,
            new RelicAggregate(),
            floorCount: null,
            bloodSoakedRoseCurseAgg: null,
            cursedPearlCurseAgg: null,
            neowsBonesCurseAggs: null,
            storybookBrightestFlameAgg: brightestFlameAgg,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Storybook", title);
        Assert.Contains("Brightest Flame played", body);
        Assert.Contains("Brightest Flame drawn", body);
        Assert.Contains("gained by Flame", body);
        Assert.Contains("Cards drawn by Flame", body);
        Assert.Contains("Max HP lost to Flame", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.Contains("[b]6[/b]", body);
        Assert.Contains("[b]8[/b]", body);
    }
}
