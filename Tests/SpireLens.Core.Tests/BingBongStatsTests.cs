using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class BingBongStatsTests
{
    private const string BingBongRelicId = "RELIC.BING_BONG";

    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildBingBongBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "BuildBingBongBodyBBCode not found.");

    [Fact]
    public void Patch_TargetsPermanentDeckAddOverloadWithCloneSource()
    {
        var target = typeof(CardPileCmd).GetMethod(
            nameof(CardPileCmd.Add),
            [
                typeof(CardModel),
                typeof(PileType),
                typeof(CardPilePosition),
                typeof(AbstractModel),
                typeof(bool),
            ]);

        Assert.NotNull(target);
        Assert.Equal(
            typeof(AbstractModel),
            target!.GetParameters().Single(parameter =>
                parameter.Name == "clonedBy").ParameterType);
    }

    [Fact]
    public void RunTracker_BingBongHelper_SplitsSuccessfulAddsByFinalCard()
    {
        var aggregate = new RelicAggregate();

        RunTracker.RecordBingBongCardAddedForTest(
            aggregate,
            CardType.Attack,
            CardRarity.Common,
            3);
        RunTracker.RecordBingBongCardAddedForTest(
            aggregate,
            CardType.Skill,
            CardRarity.Uncommon,
            2);
        RunTracker.RecordBingBongCardAddedForTest(
            aggregate,
            CardType.Power,
            CardRarity.Rare);
        RunTracker.RecordBingBongCardAddedForTest(
            aggregate,
            CardType.Curse,
            CardRarity.Common,
            2);
        RunTracker.RecordBingBongCardAddedForTest(
            aggregate,
            CardType.Skill,
            CardRarity.Basic);
        RunTracker.RecordBingBongCardAddedForTest(
            aggregate,
            CardType.Attack,
            CardRarity.Common,
            0);

        Assert.Equal(9, aggregate.BingBongExtraCardsAdded);
        Assert.Equal(3, aggregate.BingBongCommonCardsAdded);
        Assert.Equal(2, aggregate.BingBongUncommonCardsAdded);
        Assert.Equal(1, aggregate.BingBongRareCardsAdded);
        Assert.Equal(2, aggregate.BingBongCurseCardsAdded);
    }

    [Fact]
    public void RelicAggregate_BingBongFields_JsonRoundtripPreservesValues()
    {
        var run = new RunData();
        run.RelicAggregates[BingBongRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(
            json,
            RunStorage.Options);

        Assert.Contains("\"bing_bong_extra_cards_added\"", json);
        Assert.Contains("\"bing_bong_common_cards_added\"", json);
        Assert.Contains("\"bing_bong_uncommon_cards_added\"", json);
        Assert.Contains("\"bing_bong_rare_cards_added\"", json);
        Assert.Contains("\"bing_bong_curse_cards_added\"", json);
        Assert.NotNull(restored);
        AssertPopulatedAggregate(restored!.RelicAggregates[BingBongRelicId]);
    }

    [Fact]
    public void RelicAggregate_BingBongFields_Merge()
    {
        var target = PopulatedAggregate();

        RunTracker.MergeRelicAggregateInto(target, PopulatedAggregate());

        Assert.Equal(16, target.BingBongExtraCardsAdded);
        Assert.Equal(6, target.BingBongCommonCardsAdded);
        Assert.Equal(4, target.BingBongUncommonCardsAdded);
        Assert.Equal(2, target.BingBongRareCardsAdded);
        Assert.Equal(4, target.BingBongCurseCardsAdded);
    }

    [Fact]
    public void RelicTooltip_BingBong_ShowsRequestedCardTotals()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("Extra cards successfully added to the permanent deck", body);
        Assert.Contains("Non-Curse Common cards successfully added", body);
        Assert.Contains("Non-Curse Uncommon cards successfully added", body);
        Assert.Contains("Non-Curse Rare cards successfully added", body);
        Assert.Contains("Curse cards successfully added", body);
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void RelicTooltip_BingBong_DispatchesForModel()
    {
        var relic = (BingBong)
            RuntimeHelpers.GetUninitializedObject(typeof(BingBong));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Bing Bong", title);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("card"), body);
        Assert.Contains(
            StatConceptGlossary.RenderInformationHint(
                "Extra cards successfully added to the permanent deck by Bing Bong."),
            body);
    }

    [Fact]
    public void RelicAggregate_OlderShapeWithoutBingBongFields_DefaultsToZero()
    {
        var aggregate = JsonSerializer.Deserialize<RelicAggregate>(
            "{}",
            RunStorage.Options);

        Assert.NotNull(aggregate);
        Assert.Equal(0, aggregate!.BingBongExtraCardsAdded);
        Assert.Equal(0, aggregate.BingBongCommonCardsAdded);
        Assert.Equal(0, aggregate.BingBongUncommonCardsAdded);
        Assert.Equal(0, aggregate.BingBongRareCardsAdded);
        Assert.Equal(0, aggregate.BingBongCurseCardsAdded);
    }

    private static RelicAggregate PopulatedAggregate()
        => new()
        {
            BingBongExtraCardsAdded = 8,
            BingBongCommonCardsAdded = 3,
            BingBongUncommonCardsAdded = 2,
            BingBongRareCardsAdded = 1,
            BingBongCurseCardsAdded = 2,
        };

    private static void AssertPopulatedAggregate(RelicAggregate aggregate)
    {
        Assert.Equal(8, aggregate.BingBongExtraCardsAdded);
        Assert.Equal(3, aggregate.BingBongCommonCardsAdded);
        Assert.Equal(2, aggregate.BingBongUncommonCardsAdded);
        Assert.Equal(1, aggregate.BingBongRareCardsAdded);
        Assert.Equal(2, aggregate.BingBongCurseCardsAdded);
    }

    private static string BuildBody(RelicAggregate aggregate)
        => (string)(BuildBodyMethod.Invoke(null, [aggregate])
            ?? throw new InvalidOperationException(
                "BuildBingBongBodyBBCode returned null."));
}
