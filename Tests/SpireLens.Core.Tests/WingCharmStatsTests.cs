using System;
using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Cards;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class WingCharmStatsTests
{
    private const string WingCharmRelicId = "RELIC.WING_CHARM";

    private static readonly MethodInfo BuildWingCharmBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildWingCharmBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildWingCharmBodyBBCode not found.");

    [Fact]
    public void RelicAggregate_WingCharmFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.WingCharmSwiftCardsTaken);
        Assert.Equal(0, agg.WingCharmSwiftCardsNotTaken);
        Assert.Equal(0, agg.WingCharmCommonSwiftCardsOffered);
        Assert.Equal(0, agg.WingCharmUncommonSwiftCardsOffered);
        Assert.Equal(0, agg.WingCharmRareSwiftCardsOffered);
    }

    [Fact]
    public void RelicAggregate_WingCharmFields_JsonRoundtripPreservesValues()
    {
        var run = new RunData();
        run.RelicAggregates[WingCharmRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"wing_charm_swift_cards_taken\"", json);
        Assert.Contains("\"wing_charm_swift_cards_not_taken\"", json);
        Assert.Contains("\"wing_charm_common_swift_cards_offered\"", json);
        Assert.Contains("\"wing_charm_uncommon_swift_cards_offered\"", json);
        Assert.Contains("\"wing_charm_rare_swift_cards_offered\"", json);
        Assert.NotNull(restored);
        AssertPopulated(restored!.RelicAggregates[WingCharmRelicId]);
    }

    [Fact]
    public void RunTracker_WingCharmHelper_RecordsOutcomesAndRarities()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordWingCharmRewardForTest(
            agg,
            new[]
            {
                CardRarity.Common,
                CardRarity.Uncommon,
                CardRarity.Rare,
                CardRarity.Common,
            },
            takenCount: 2);

        Assert.Equal(2, agg.WingCharmSwiftCardsTaken);
        Assert.Equal(2, agg.WingCharmSwiftCardsNotTaken);
        Assert.Equal(2, agg.WingCharmCommonSwiftCardsOffered);
        Assert.Equal(1, agg.WingCharmUncommonSwiftCardsOffered);
        Assert.Equal(1, agg.WingCharmRareSwiftCardsOffered);
    }

    [Fact]
    public void RelicAggregate_WingCharmFields_Merge()
    {
        var target = new RelicAggregate
        {
            WingCharmSwiftCardsTaken = 1,
            WingCharmSwiftCardsNotTaken = 2,
            WingCharmCommonSwiftCardsOffered = 2,
            WingCharmUncommonSwiftCardsOffered = 1,
            WingCharmRareSwiftCardsOffered = 0,
        };

        RunTracker.MergeRelicAggregateInto(target, PopulatedAggregate());

        Assert.Equal(4, target.WingCharmSwiftCardsTaken);
        Assert.Equal(6, target.WingCharmSwiftCardsNotTaken);
        Assert.Equal(6, target.WingCharmCommonSwiftCardsOffered);
        Assert.Equal(3, target.WingCharmUncommonSwiftCardsOffered);
        Assert.Equal(1, target.WingCharmRareSwiftCardsOffered);
    }

    [Fact]
    public void RelicTooltip_WingCharm_ShowsRequestedStatsWithSwiftIcons()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("swift"), body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("card"), body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("card_uncommon"), body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("card_rare"), body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("offered"), body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("taken"), body);
        Assert.Contains("[b]+[/b]", body);
        Assert.DoesNotContain("not taken", body);
        // 3 taken of 7 enchanted options, then the offered-only rarity splits.
        Assert.Contains("[b]7/3[/b]", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]1[/b]", body);
    }

    private static RelicAggregate PopulatedAggregate()
        => new()
        {
            WingCharmSwiftCardsTaken = 3,
            WingCharmSwiftCardsNotTaken = 4,
            WingCharmCommonSwiftCardsOffered = 4,
            WingCharmUncommonSwiftCardsOffered = 2,
            WingCharmRareSwiftCardsOffered = 1,
        };

    private static void AssertPopulated(RelicAggregate agg)
    {
        Assert.Equal(3, agg.WingCharmSwiftCardsTaken);
        Assert.Equal(4, agg.WingCharmSwiftCardsNotTaken);
        Assert.Equal(4, agg.WingCharmCommonSwiftCardsOffered);
        Assert.Equal(2, agg.WingCharmUncommonSwiftCardsOffered);
        Assert.Equal(1, agg.WingCharmRareSwiftCardsOffered);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildWingCharmBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildWingCharmBodyBBCode returned null."));
}
