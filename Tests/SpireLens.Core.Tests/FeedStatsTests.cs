using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Cards;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class FeedStatsTests
{
    private static readonly MethodInfo TargetMethod =
        typeof(FeedOnPlayPatch).GetMethod(
            "TargetMethod",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Feed TargetMethod not found.");

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void Patch_TargetsFeedOnPlayWithExpectedParameters()
    {
        var target = TargetMethod.Invoke(null, null) as MethodBase;

        Assert.NotNull(target);
        Assert.Equal(typeof(Feed), target!.DeclaringType);
        Assert.Equal("OnPlay", target.Name);
        Assert.Equal(
            new[] { typeof(PlayerChoiceContext), typeof(CardPlay) },
            target.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void RunTracker_FeedCountsOnlyPositiveObservedMaxHpGain()
    {
        var agg = new CardAggregate();

        RunTracker.RecordFeedMaxHpGainedForTest(agg, previousMaxHp: 70, currentMaxHp: 73);
        RunTracker.RecordFeedMaxHpGainedForTest(agg, previousMaxHp: 73, currentMaxHp: 73);
        RunTracker.RecordFeedMaxHpGainedForTest(agg, previousMaxHp: 73, currentMaxHp: 72);
        RunTracker.RecordFeedMaxHpGainedForTest(agg, previousMaxHp: 72, currentMaxHp: 76);

        Assert.Equal(7, agg.TotalMaxHpGained);
    }

    [Fact]
    public void CardAggregate_MaxHpGainedMergesAndPools()
    {
        var merged = new CardAggregate { TotalMaxHpGained = 3 };
        RunTracker.MergeAggregateInto(
            merged,
            new CardAggregate { TotalMaxHpGained = 4 });

        var pooled = CardAggregatePooler.PoolByDefinition(
            new[]
            {
                new KeyValuePair<string, CardAggregate>(
                    "CARD.FEED#1",
                    new CardAggregate { TotalMaxHpGained = 3 }),
                new KeyValuePair<string, CardAggregate>(
                    "CARD.FEED#2",
                    new CardAggregate { TotalMaxHpGained = 4 }),
            },
            "CARD.FEED");

        Assert.Equal(7, merged.TotalMaxHpGained);
        Assert.NotNull(pooled);
        Assert.Equal(7, pooled!.TotalMaxHpGained);
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void CardTooltip_FeedShowsMaxHpGainedInFullAndCompactViews()
    {
        var card = (Feed)RuntimeHelpers.GetUninitializedObject(typeof(Feed));
        var agg = new CardAggregate { TotalMaxHpGained = 7 };

        var full = CardHoverShowPatch.BuildHistoricalBodyBBCode(
            card,
            agg,
            new RunMetaStats());
        var compact = CardHoverShowPatch.BuildHistoricalBodyBBCode(
            card,
            agg,
            new RunMetaStats(),
            compact: true);

        Assert.Contains("Max HP gained", full);
        Assert.Contains("[b]7[/b]", full);
        Assert.Contains("Max HP gained", compact);
        Assert.Contains("[b]7[/b]", compact);
    }
}
