using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Cards;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class AllForOneStatsTests
{
    private const string CardId = "CARD.ALL_FOR_ONE";

    private static readonly MethodInfo AppendStatsMethod =
        typeof(CardHoverShowPatch).GetMethod(
            "AppendAllForOneStats",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("AppendAllForOneStats not found.");

    [Fact]
    public void CardAggregate_Field_DefaultsToZero()
    {
        Assert.Equal(0, new CardAggregate().AllForOneZeroCostCardsReturned);
    }

    [Fact]
    public void CardAggregate_Field_JsonRoundtripPreservesValue()
    {
        var run = new RunData();
        run.Aggregates[$"{CardId}#1"] = RepresentativeAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"all_for_one_zero_cost_cards_returned\"", json);
        Assert.NotNull(restored);
        Assert.Equal(
            12,
            restored!.Aggregates[$"{CardId}#1"].AllForOneZeroCostCardsReturned);
    }

    [Theory]
    [InlineData(true, PileType.Discard, PileType.Hand, 0, false, CardType.Attack, true, true)]
    [InlineData(false, PileType.Discard, PileType.Hand, 0, false, CardType.Attack, true, false)]
    [InlineData(true, PileType.Hand, PileType.Hand, 0, false, CardType.Attack, true, false)]
    [InlineData(true, PileType.Discard, PileType.Discard, 0, false, CardType.Attack, true, false)]
    [InlineData(true, PileType.Discard, PileType.Hand, 1, false, CardType.Attack, true, false)]
    [InlineData(true, PileType.Discard, PileType.Hand, 0, true, CardType.Attack, true, false)]
    [InlineData(true, PileType.Discard, PileType.Hand, 0, false, CardType.Status, true, false)]
    [InlineData(true, PileType.Discard, PileType.Hand, 0, false, CardType.Skill, false, false)]
    public void ReturnQualification_MatchesObservedAllForOneResult(
        bool sourceIsAllForOne,
        PileType oldPile,
        PileType newPile,
        int effectiveEnergyCost,
        bool costsX,
        CardType cardType,
        bool sameOwner,
        bool expected)
    {
        Assert.Equal(
            expected,
            RunTracker.AllForOneReturnQualifiesForTest(
                sourceIsAllForOne,
                oldPile,
                newPile,
                effectiveEnergyCost,
                costsX,
                cardType,
                sameOwner));
    }

    [Fact]
    public void RecordHelper_CountsOnlyPositiveReturns()
    {
        var aggregate = new CardAggregate();

        RunTracker.RecordAllForOneReturnForTest(aggregate, 12);
        RunTracker.RecordAllForOneReturnForTest(aggregate, 0);
        RunTracker.RecordAllForOneReturnForTest(aggregate, -2);

        Assert.Equal(12, aggregate.AllForOneZeroCostCardsReturned);
    }

    [Fact]
    public void MergeAggregateInto_AccumulatesReturns()
    {
        var target = new CardAggregate { AllForOneZeroCostCardsReturned = 5 };
        var source = new CardAggregate { AllForOneZeroCostCardsReturned = 7 };

        RunTracker.MergeAggregateInto(target, source);

        Assert.Equal(12, target.AllForOneZeroCostCardsReturned);
    }

    [Fact]
    public void Tooltip_FullViewShowsTotalAndRequestedAverages()
    {
        var body = BuildStatsBody(RepresentativeAggregate(), compact: false);

        Assert.Contains("0-cost cards returned", body);
        Assert.Contains("[b]12[/b]", body);
        Assert.Contains("Avg returned per play", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("Avg returned per combat", body);
        Assert.Contains("[b]3[/b]", body);
    }

    [Fact]
    public void Tooltip_CompactViewKeepsOnlyTotal()
    {
        var body = BuildStatsBody(RepresentativeAggregate(), compact: true);

        Assert.Contains("0-cost cards returned", body);
        Assert.DoesNotContain("Avg returned per play", body);
        Assert.DoesNotContain("Avg returned per combat", body);
    }

    [Fact]
    public void OlderShapeWithoutField_DefaultsToZero()
    {
        var aggregate = JsonSerializer.Deserialize<CardAggregate>("{}", RunStorage.Options);

        Assert.NotNull(aggregate);
        Assert.Equal(0, aggregate!.AllForOneZeroCostCardsReturned);
    }

    private static CardAggregate RepresentativeAggregate() =>
        new()
        {
            CombatsInDeck = 4,
            Plays = 6,
            AllForOneZeroCostCardsReturned = 12,
        };

    private static string BuildStatsBody(CardAggregate aggregate, bool compact)
    {
        var card = (AllForOne)RuntimeHelpers.GetUninitializedObject(typeof(AllForOne));
        var builder = new StringBuilder();
        _ = AppendStatsMethod.Invoke(null, new object?[] { builder, card, aggregate, compact });
        return builder.ToString();
    }
}
