using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class EggRelicStatsTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void EggRelicPatch_TargetsRelicAttributedCardCreationModification()
    {
        var targetMethod = typeof(EggRelicCardOfferStatsPatch).GetMethod(
            "TargetMethod",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("TargetMethod not found.");
        var target = targetMethod.Invoke(null, null) as MethodBase;

        Assert.NotNull(target);
        Assert.Equal(nameof(CardCreationResult.ModifyCard), target!.Name);
        Assert.Equal(
            new[] { typeof(CardModel), typeof(RelicModel) },
            target.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void RelicAggregate_EggFields_JsonRoundtripPreservesOfferAndTakenRarityCounts()
    {
        var run = new RunData();
        run.RelicAggregates["RELIC.MOLTEN_EGG"] = new RelicAggregate
        {
            UpgradedCardsOffered = 7,
            UpgradedCommonCardsOffered = 4,
            UpgradedUncommonCardsOffered = 2,
            UpgradedRareCardsOffered = 1,
            UpgradedCardsTaken = 4,
            UpgradedCommonCardsTaken = 2,
            UpgradedUncommonCardsTaken = 1,
            UpgradedRareCardsTaken = 1,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);
        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates["RELIC.MOLTEN_EGG"];
        Assert.Equal(7, agg.UpgradedCardsOffered);
        Assert.Equal(4, agg.UpgradedCommonCardsOffered);
        Assert.Equal(2, agg.UpgradedUncommonCardsOffered);
        Assert.Equal(1, agg.UpgradedRareCardsOffered);
        Assert.Equal(4, agg.UpgradedCardsTaken);
        Assert.Equal(2, agg.UpgradedCommonCardsTaken);
        Assert.Equal(1, agg.UpgradedUncommonCardsTaken);
        Assert.Equal(1, agg.UpgradedRareCardsTaken);
    }

    [Fact]
    public void RunTracker_EggOfferTestHelper_TracksTotalAndRarityBuckets()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordEggUpgradedCardOfferedForTest(agg, CardRarity.Common, 2);
        RunTracker.RecordEggUpgradedCardOfferedForTest(agg, CardRarity.Uncommon);
        RunTracker.RecordEggUpgradedCardOfferedForTest(agg, CardRarity.Rare);
        RunTracker.RecordEggUpgradedCardOfferedForTest(agg, CardRarity.Basic);
        RunTracker.RecordEggUpgradedCardOfferedForTest(agg, CardRarity.Rare, -1);

        Assert.Equal(5, agg.UpgradedCardsOffered);
        Assert.Equal(2, agg.UpgradedCommonCardsOffered);
        Assert.Equal(1, agg.UpgradedUncommonCardsOffered);
        Assert.Equal(1, agg.UpgradedRareCardsOffered);
    }

    [Fact]
    public void RunTracker_EggTakenTestHelper_TracksTotalAndRarityBuckets()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordEggUpgradedCardTakenForTest(agg, CardRarity.Common, 2);
        RunTracker.RecordEggUpgradedCardTakenForTest(agg, CardRarity.Uncommon);
        RunTracker.RecordEggUpgradedCardTakenForTest(agg, CardRarity.Rare);
        RunTracker.RecordEggUpgradedCardTakenForTest(agg, CardRarity.Basic);
        RunTracker.RecordEggUpgradedCardTakenForTest(agg, CardRarity.Rare, -1);

        Assert.Equal(5, agg.UpgradedCardsTaken);
        Assert.Equal(2, agg.UpgradedCommonCardsTaken);
        Assert.Equal(1, agg.UpgradedUncommonCardsTaken);
        Assert.Equal(1, agg.UpgradedRareCardsTaken);
    }

    [Fact]
    public void RelicAggregate_EggFields_Merge()
    {
        var target = new RelicAggregate
        {
            UpgradedCardsOffered = 4,
            UpgradedCommonCardsOffered = 2,
            UpgradedUncommonCardsOffered = 1,
            UpgradedRareCardsOffered = 1,
            UpgradedCardsTaken = 2,
            UpgradedCommonCardsTaken = 1,
            UpgradedUncommonCardsTaken = 1,
        };
        var source = new RelicAggregate
        {
            UpgradedCardsOffered = 3,
            UpgradedCommonCardsOffered = 1,
            UpgradedUncommonCardsOffered = 1,
            UpgradedRareCardsOffered = 1,
            UpgradedCardsTaken = 2,
            UpgradedCommonCardsTaken = 1,
            UpgradedRareCardsTaken = 1,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(7, target.UpgradedCardsOffered);
        Assert.Equal(3, target.UpgradedCommonCardsOffered);
        Assert.Equal(2, target.UpgradedUncommonCardsOffered);
        Assert.Equal(2, target.UpgradedRareCardsOffered);
        Assert.Equal(4, target.UpgradedCardsTaken);
        Assert.Equal(2, target.UpgradedCommonCardsTaken);
        Assert.Equal(1, target.UpgradedUncommonCardsTaken);
        Assert.Equal(1, target.UpgradedRareCardsTaken);
    }

    [Fact]
    public void RelicTooltip_MoltenEgg_ShowsUpgradedAttacksOffered()
    {
        AssertEggTooltip<MoltenEgg>("Molten Egg", "attacks");
    }

    [Fact]
    public void RelicTooltip_ToxicEgg_ShowsUpgradedSkillsOffered()
    {
        AssertEggTooltip<ToxicEgg>("Toxic Egg", "skills");
    }

    [Fact]
    public void RelicTooltip_FrozenEgg_ShowsUpgradedPowersOffered()
    {
        AssertEggTooltip<FrozenEgg>("Frozen Egg", "powers");
    }

    private static void AssertEggTooltip<TEgg>(string expectedTitle, string cardType)
        where TEgg : RelicModel
    {
        var relic = (TEgg)RuntimeHelpers.GetUninitializedObject(typeof(TEgg));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate
            {
                UpgradedCardsOffered = 6,
                UpgradedCommonCardsOffered = 3,
                UpgradedUncommonCardsOffered = 2,
                UpgradedRareCardsOffered = 1,
                UpgradedCardsTaken = 4,
                UpgradedCommonCardsTaken = 2,
                UpgradedUncommonCardsTaken = 1,
                UpgradedRareCardsTaken = 1,
            },
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal(expectedTitle, title);
        Assert.Contains(
            StatConceptGlossary.RenderHintedGlyph("offered"),
            body);
        Assert.Contains(
            StatConceptGlossary.RenderHintedGlyph("taken"),
            body);
        Assert.Contains(
            $"res://images/packed/card_library/type_sort_{cardType.TrimEnd('s')}.png",
            body);
        Assert.Contains("color=#87CEEB", body);
        Assert.Contains("color=#EFC850", body);
        Assert.DoesNotContain(
            "res://images/ui/reward_screen/reward_icon_card.png",
            body);
        Assert.Contains("[b]6[/b]", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]1[/b]", body);
        Assert.Contains("[b]4[/b]", body);
    }
}
