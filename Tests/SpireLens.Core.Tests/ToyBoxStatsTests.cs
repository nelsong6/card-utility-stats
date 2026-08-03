using System;
using System.Reflection;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class ToyBoxStatsTests
{
    private static readonly MethodInfo BuildToyBoxBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildToyBoxBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "BuildToyBoxBodyBBCode not found.");

    [Fact]
    public void ToyBoxMeltPatch_TargetsAuthoritativeRelicCommand()
    {
        var target = typeof(RelicCmd).GetMethod(
            nameof(RelicCmd.Melt),
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(RelicModel)],
            modifiers: null);

        Assert.NotNull(target);
        Assert.Equal(typeof(Task), target!.ReturnType);
    }

    [Fact]
    public void ToyBoxWaxLedger_RecordsBestowedAndMeltedFloorsInOrder()
    {
        var aggregate = new RelicAggregate();

        RunTracker.RecordToyBoxWaxRelicBestowedForTest(
            aggregate,
            "RELIC.ANCHOR",
            "Wax Anchor",
            floorBestowed: 5);
        RunTracker.RecordToyBoxWaxRelicBestowedForTest(
            aggregate,
            "RELIC.BAG_OF_PREPARATION",
            "Wax Bag of Preparation",
            floorBestowed: 5);
        RunTracker.RecordToyBoxWaxRelicMeltedForTest(
            aggregate,
            "RELIC.ANCHOR",
            "Wax Anchor",
            floorBestowed: 5,
            floorMelted: 9);
        RunTracker.RecordToyBoxWaxRelicMeltedForTest(
            aggregate,
            "RELIC.BAG_OF_PREPARATION",
            "Wax Bag of Preparation",
            floorBestowed: 5,
            floorMelted: 14);

        Assert.Equal(2, aggregate.ToyBoxWaxRelics.Count);
        Assert.Equal(1, aggregate.ToyBoxWaxRelics[0].Sequence);
        Assert.Equal(9, aggregate.ToyBoxWaxRelics[0].FloorMelted);
        Assert.Equal(2, aggregate.ToyBoxWaxRelics[1].Sequence);
        Assert.Equal(14, aggregate.ToyBoxWaxRelics[1].FloorMelted);
        Assert.Equal(
            6.5m,
            RelicHoverShowPatch.CalculateToyBoxAverageFloorsToMelt(
                aggregate));
    }

    [Fact]
    public void MergeRelicAggregateInto_ToyBoxMeltUpdatesExactWaxEntry()
    {
        var target = new RelicAggregate
        {
            ToyBoxWaxRelics =
            [
                new ToyBoxWaxRelicAggregate
                {
                    Sequence = 1,
                    RelicId = "RELIC.ANCHOR",
                    DisplayName = "Wax Anchor",
                    FloorBestowed = 5,
                },
            ],
        };
        var pending = new RelicAggregate
        {
            ToyBoxWaxRelics =
            [
                new ToyBoxWaxRelicAggregate
                {
                    Sequence = 1,
                    RelicId = "RELIC.ANCHOR",
                    DisplayName = "Wax Anchor",
                    FloorBestowed = 5,
                    FloorMelted = 9,
                },
            ],
        };

        RunTracker.MergeRelicAggregateInto(target, pending);

        var waxRelic = Assert.Single(target.ToyBoxWaxRelics);
        Assert.Equal(5, waxRelic.FloorBestowed);
        Assert.Equal(9, waxRelic.FloorMelted);
    }

    [Fact]
    public void ToyBoxTooltip_ShowsWaxRelicsFloorsAndAverage()
    {
        var aggregate = new RelicAggregate
        {
            ToyBoxWaxRelics =
            [
                new ToyBoxWaxRelicAggregate
                {
                    Sequence = 1,
                    RelicId = "RELIC.ANCHOR",
                    DisplayName = "Wax Anchor",
                    FloorBestowed = 5,
                    FloorMelted = 9,
                },
                new ToyBoxWaxRelicAggregate
                {
                    Sequence = 2,
                    RelicId = "RELIC.BAG_OF_PREPARATION",
                    DisplayName = "Wax Bag of Preparation",
                    FloorBestowed = 5,
                },
            ],
        };

        var body = BuildBody(aggregate);

        Assert.Contains("Wax relics bestowed", body);
        Assert.Contains("Wax relics melted", body);
        Assert.Contains("Avg floors to melt", body);
        Assert.Contains("Wax Anchor", body);
        Assert.Contains("Wax Bag of Preparation", body);
        Assert.Contains(
            "res://images/atlases/ui_atlas.sprites/top_bar/top_bar_floor.tres",
            body);
        Assert.Contains("5 · melted", body);
        Assert.Contains("9", body);
        Assert.Contains("5 · not melted", body);
        Assert.DoesNotContain("Floor 5", body);
        Assert.DoesNotContain("melted Floor", body);
        Assert.Contains("[b]4[/b]", body);
    }

    private static string BuildBody(RelicAggregate aggregate)
        => (string)(BuildToyBoxBodyMethod.Invoke(
            null,
            new object?[] { aggregate })
            ?? throw new InvalidOperationException(
                "BuildToyBoxBodyBBCode returned null."));
}
