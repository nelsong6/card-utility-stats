using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

/// <summary>
/// Pins White Star's option classification and persisted presentation. Live
/// reward lifecycle timing remains user-owned gameplay verification.
/// </summary>
public class WhiteStarStatsTests
{
    private const string WhiteStarRelicId = "RELIC.WHITE_STAR";

    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildWhiteStarBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "BuildWhiteStarBodyBBCode not found.");

    [Fact]
    public void TrackingMath_CountsOnlyRareOffersAndSplitsCardTypes()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordWhiteStarActivationForTest(agg);
        RunTracker.RecordWhiteStarOffersForTest(
            agg,
            [
                (CardRarity.Rare, CardType.Attack),
                (CardRarity.Rare, CardType.Attack),
                (CardRarity.Rare, CardType.Skill),
                (CardRarity.Rare, CardType.Power),
                (CardRarity.Uncommon, CardType.Attack),
            ]);
        RunTracker.RecordWhiteStarRewardDeclinedForTest(agg);

        Assert.Equal(1, agg.Activations);
        Assert.Equal(4, agg.RareCardsOffered);
        Assert.Equal(2, agg.RareAttackCardsOffered);
        Assert.Equal(1, agg.RareSkillCardsOffered);
        Assert.Equal(1, agg.RarePowerCardsOffered);
        Assert.Equal(1, agg.RareCardRewardScreensDeclined);
        Assert.Equal(0, agg.RareCardsTaken);
    }

    [Fact]
    public void TrackingMath_CreditsTakenRaresBySplitCardType()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordWhiteStarTakenForTest(
            agg,
            rareCards: 1,
            rareAttackCards: 1);
        RunTracker.RecordWhiteStarTakenForTest(
            agg,
            rareCards: 1,
            rarePowerCards: 1);

        Assert.Equal(2, agg.RareCardsTaken);
        Assert.Equal(1, agg.RareAttackCardsTaken);
        Assert.Equal(0, agg.RareSkillCardsTaken);
        Assert.Equal(1, agg.RarePowerCardsTaken);
        Assert.Equal(0, agg.RareCardRewardScreensDeclined);
    }

    [Fact]
    public void RelicAggregate_WhiteStarFields_JsonRoundtripPreservesValues()
    {
        var run = new RunData();
        run.RelicAggregates[WhiteStarRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(
            json,
            RunStorage.Options);

        Assert.Contains("\"rare_attack_cards_offered\"", json);
        Assert.Contains("\"rare_skill_cards_offered\"", json);
        Assert.Contains("\"rare_power_cards_offered\"", json);
        Assert.Contains("\"rare_attack_cards_taken\"", json);
        Assert.Contains("\"rare_skill_cards_taken\"", json);
        Assert.Contains("\"rare_power_cards_taken\"", json);
        Assert.Contains("\"rare_card_reward_screens_declined\"", json);
        Assert.NotNull(restored);
        AssertPopulatedAggregate(restored!.RelicAggregates[WhiteStarRelicId]);
    }

    [Fact]
    public void MergeRelicAggregateInto_AccumulatesWhiteStarFields()
    {
        var target = PopulatedAggregate();

        RunTracker.MergeRelicAggregateInto(target, PopulatedAggregate());

        Assert.Equal(6, target.Activations);
        Assert.Equal(16, target.RareCardsOffered);
        Assert.Equal(8, target.RareAttackCardsOffered);
        Assert.Equal(4, target.RareSkillCardsOffered);
        Assert.Equal(4, target.RarePowerCardsOffered);
        Assert.Equal(6, target.RareCardsTaken);
        Assert.Equal(4, target.RareAttackCardsTaken);
        Assert.Equal(2, target.RareSkillCardsTaken);
        Assert.Equal(0, target.RarePowerCardsTaken);
        Assert.Equal(4, target.RareCardRewardScreensDeclined);
    }

    [Fact]
    public void Tooltip_ShowsRequestedWhiteStarRowsAndRareTypeIcons()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("Activations", body);
        Assert.Contains(
            StatConceptGlossary.RenderHintedGlyph("offered"),
            body);
        Assert.Contains(
            StatConceptGlossary.RenderHintedGlyph("taken"),
            body);
        Assert.Contains("Rare card reward screens declined", body);
        Assert.Contains("type_sort_attack.png", body);
        Assert.Contains("type_sort_skill.png", body);
        Assert.Contains("type_sort_power.png", body);
        Assert.Contains("color=#EFC850", body);
        Assert.Contains("[b]8/3[/b]", body);
        Assert.Contains("[b]4/2[/b]", body);
        Assert.Contains("[b]2/1[/b]", body);
        Assert.Contains("[b]2/0[/b]", body);
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void TooltipDispatch_RecognizesWhiteStar()
    {
        var relic = (WhiteStar)
            RuntimeHelpers.GetUninitializedObject(typeof(WhiteStar));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            PopulatedAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("White Star", title);
        Assert.Contains("Rares offered/taken", body);
    }

    [Fact]
    public void OlderShape_DefaultsWhiteStarFieldsToZero()
    {
        var agg = JsonSerializer.Deserialize<RelicAggregate>(
            "{}",
            RunStorage.Options);

        Assert.NotNull(agg);
        Assert.Equal(0, agg!.RareAttackCardsOffered);
        Assert.Equal(0, agg.RareSkillCardsOffered);
        Assert.Equal(0, agg.RarePowerCardsOffered);
        Assert.Equal(0, agg.RareCardsTaken);
        Assert.Equal(0, agg.RareAttackCardsTaken);
        Assert.Equal(0, agg.RareSkillCardsTaken);
        Assert.Equal(0, agg.RarePowerCardsTaken);
        Assert.Equal(0, agg.RareCardRewardScreensDeclined);
    }

    private static RelicAggregate PopulatedAggregate()
        => new()
        {
            Activations = 3,
            RareCardsOffered = 8,
            RareAttackCardsOffered = 4,
            RareSkillCardsOffered = 2,
            RarePowerCardsOffered = 2,
            RareCardsTaken = 3,
            RareAttackCardsTaken = 2,
            RareSkillCardsTaken = 1,
            RareCardRewardScreensDeclined = 2,
        };

    private static void AssertPopulatedAggregate(RelicAggregate agg)
    {
        Assert.Equal(3, agg.Activations);
        Assert.Equal(8, agg.RareCardsOffered);
        Assert.Equal(4, agg.RareAttackCardsOffered);
        Assert.Equal(2, agg.RareSkillCardsOffered);
        Assert.Equal(2, agg.RarePowerCardsOffered);
        Assert.Equal(3, agg.RareCardsTaken);
        Assert.Equal(2, agg.RareAttackCardsTaken);
        Assert.Equal(1, agg.RareSkillCardsTaken);
        Assert.Equal(0, agg.RarePowerCardsTaken);
        Assert.Equal(2, agg.RareCardRewardScreensDeclined);
    }

    private static string BuildBody(RelicAggregate aggregate)
        => (string)(BuildBodyMethod.Invoke(null, [aggregate])
            ?? throw new InvalidOperationException(
                "BuildWhiteStarBodyBBCode returned null."));
}
