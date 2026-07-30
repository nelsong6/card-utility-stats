using System;
using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class ThrowingAxeStatsTests
{
    private const string ThrowingAxeRelicId = "RELIC.THROWING_AXE";

    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildThrowingAxeBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "BuildThrowingAxeBodyBBCode not found.");

    [Fact]
    public void RelicAggregate_ThrowingAxeFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.ThrowingAxeExtraCardsPlayed);
        Assert.Equal(0, agg.ThrowingAxeExtraPlayEnergyCostTotal);
        Assert.Equal(0, agg.ThrowingAxeCombats);
        Assert.Equal(0, agg.ThrowingAxeCommonCardsPlayed);
        Assert.Equal(0, agg.ThrowingAxeUncommonCardsPlayed);
        Assert.Equal(0, agg.ThrowingAxeRareCardsPlayed);
    }

    [Fact]
    public void RelicAggregate_ThrowingAxeFields_JsonRoundtripPreservesValues()
    {
        var run = new RunData();
        run.RelicAggregates[ThrowingAxeRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(
            json,
            RunStorage.Options);

        Assert.Contains("\"throwing_axe_extra_cards_played\"", json);
        Assert.Contains("\"throwing_axe_extra_play_energy_cost_total\"", json);
        Assert.Contains("\"throwing_axe_combats\"", json);
        Assert.NotNull(restored);
        AssertAggregate(restored!.RelicAggregates[ThrowingAxeRelicId]);
    }

    [Fact]
    public void RunTracker_ThrowingAxeHelpers_CountFinishedPlayValuesAndRarities()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordThrowingAxeExtraPlayForTest(
            agg,
            1,
            CardRarity.Common);
        RunTracker.RecordThrowingAxeExtraPlayForTest(
            agg,
            2,
            CardRarity.Uncommon);
        RunTracker.RecordThrowingAxeExtraPlayForTest(
            agg,
            3,
            CardRarity.Rare);
        RunTracker.RecordThrowingAxeExtraPlayForTest(
            agg,
            -2,
            CardRarity.Basic);
        RunTracker.RecordThrowingAxeCombatForTest(agg, 4);
        RunTracker.RecordThrowingAxeCombatForTest(agg, 0);

        Assert.Equal(4, agg.ThrowingAxeExtraCardsPlayed);
        Assert.Equal(6, agg.ThrowingAxeExtraPlayEnergyCostTotal);
        Assert.Equal(4, agg.ThrowingAxeCombats);
        Assert.Equal(1, agg.ThrowingAxeCommonCardsPlayed);
        Assert.Equal(1, agg.ThrowingAxeUncommonCardsPlayed);
        Assert.Equal(1, agg.ThrowingAxeRareCardsPlayed);
    }

    [Fact]
    public void RelicAggregate_ThrowingAxeFields_Merge()
    {
        var target = PopulatedAggregate();

        RunTracker.MergeRelicAggregateInto(target, PopulatedAggregate());

        Assert.Equal(6, target.ThrowingAxeExtraCardsPlayed);
        Assert.Equal(14, target.ThrowingAxeExtraPlayEnergyCostTotal);
        Assert.Equal(8, target.ThrowingAxeCombats);
        Assert.Equal(2, target.ThrowingAxeCommonCardsPlayed);
        Assert.Equal(2, target.ThrowingAxeUncommonCardsPlayed);
        Assert.Equal(2, target.ThrowingAxeRareCardsPlayed);
    }

    [Fact]
    public void RelicTooltip_ThrowingAxe_ShowsRequestedTotalsAndCombatAverage()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("Finished extra card plays contributed by Throwing Axe.", body);
        Assert.Contains("play-time energy values of cards replayed by Throwing Axe.", body);
        Assert.Contains("Total energy cost of Throwing Axe extra plays divided by combats", body);
        Assert.Contains("Common cards replayed by Throwing Axe.", body);
        Assert.Contains("Uncommon cards replayed by Throwing Axe.", body);
        Assert.Contains("Rare cards replayed by Throwing Axe.", body);
        Assert.Contains("[b]1.75[/b]", body);
    }

    private static RelicAggregate PopulatedAggregate()
        => new()
        {
            ThrowingAxeExtraCardsPlayed = 3,
            ThrowingAxeExtraPlayEnergyCostTotal = 7,
            ThrowingAxeCombats = 4,
            ThrowingAxeCommonCardsPlayed = 1,
            ThrowingAxeUncommonCardsPlayed = 1,
            ThrowingAxeRareCardsPlayed = 1,
        };

    private static void AssertAggregate(RelicAggregate agg)
    {
        Assert.Equal(3, agg.ThrowingAxeExtraCardsPlayed);
        Assert.Equal(7, agg.ThrowingAxeExtraPlayEnergyCostTotal);
        Assert.Equal(4, agg.ThrowingAxeCombats);
        Assert.Equal(1, agg.ThrowingAxeCommonCardsPlayed);
        Assert.Equal(1, agg.ThrowingAxeUncommonCardsPlayed);
        Assert.Equal(1, agg.ThrowingAxeRareCardsPlayed);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildBodyMethod.Invoke(null, new object[] { agg })
                    ?? throw new InvalidOperationException(
                        "Builder returned null."));
}
