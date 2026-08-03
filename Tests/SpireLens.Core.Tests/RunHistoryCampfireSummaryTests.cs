using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves.Runs;
using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

public class RunHistoryCampfireSummaryTests
{
    [Fact]
    public void CollectEntries_PreservesChronologicalFloorsAndSelectedPlayerChoices()
    {
        var history = new RunHistory
        {
            MapPointHistory =
            [
                [
                    Point(MapPointType.Monster, (1, []), (2, [])),
                    Point(MapPointType.RestSite, (1, ["SMITH"]), (2, ["REST"])),
                ],
                [
                    Point(MapPointType.RestSite, (1, ["DIG"]), (2, ["LIFT"])),
                ],
            ],
        };

        var entries = RunHistoryCampfireSummary.CollectEntries(history, 1);

        Assert.Collection(
            entries,
            entry =>
            {
                Assert.Equal(2, entry.Floor);
                Assert.Equal(["SMITH"], entry.ChoiceIds);
                Assert.Equal(1UL, entry.PlayerEntry.PlayerId);
            },
            entry =>
            {
                Assert.Equal(3, entry.Floor);
                Assert.Equal(["DIG"], entry.ChoiceIds);
            });
    }

    [Fact]
    public void BuildOutcomeText_ReportsHealingAndUpgradedCard()
    {
        var playerEntry = new PlayerMapPointHistoryEntry
        {
            PlayerId = 1,
            HpHealed = 22,
            RestSiteChoices = ["SMITH", "HEAL"],
            UpgradedCards = [new ModelId("CARD", "WHITE_NOISE")],
        };
        var entry = new RunHistoryCampfireEntry(
            7,
            playerEntry.RestSiteChoices,
            playerEntry);

        var outcome = RunHistoryCampfireSummary.BuildOutcomeText(
            entry,
            liftNumber: 0,
            choice => choice == "HEAL"
                ? "Rest"
                : RunHistoryCampfireSummary.HumanizeChoiceId(choice));

        Assert.Contains("Smith — upgraded White Noise", outcome);
        Assert.Contains("Rest — healed 22 HP", outcome);
    }

    [Fact]
    public void BuildBodyBBCode_SplitsEternalFeatherFromRestHealing()
    {
        var playerEntry = new PlayerMapPointHistoryEntry
        {
            PlayerId = 1,
            HpHealed = 31,
            RestSiteChoices = ["HEAL"],
        };
        var entry = new RunHistoryCampfireEntry(
            7,
            playerEntry.RestSiteChoices,
            playerEntry,
            EternalFeatherHealing: 9m);

        var body = RunHistoryCampfireSummary.BuildBodyBBCode(
            [entry],
            choice => choice == "HEAL" ? "Rest" : choice);

        Assert.Contains("Rest — healed 22 HP", body);
        Assert.Contains("\n      Eternal Feather — healed 9 HP", body);
        Assert.DoesNotContain("Rest — healed 31 HP", body);
    }

    [Fact]
    public void CollectEntries_AddsTrackedEternalFeatherHealingToItsFloor()
    {
        var history = new RunHistory
        {
            MapPointHistory =
            [
                [
                    Point(MapPointType.Monster, (1, [])),
                    Point(MapPointType.RestSite, (1, ["SMITH"])),
                ],
            ],
        };
        var run = new RunData();
        var aggregate = new RelicAggregate();
        aggregate.EternalFeatherHealingActivations.Add(
            new EternalFeatherHealingActivationAggregate
            {
                Floor = 2,
                HpRestored = 6m,
            });
        run.RelicAggregates["RELIC.ETERNAL_FEATHER"] = aggregate;

        var entry = Assert.Single(
            RunHistoryCampfireSummary.CollectEntries(history, 1, run));

        Assert.Equal(6m, entry.EternalFeatherHealing);
    }

    [Fact]
    public void BuildOutcomeText_ReportsCookResults()
    {
        var playerEntry = new PlayerMapPointHistoryEntry
        {
            PlayerId = 1,
            HpHealed = 9,
            MaxHpGained = 9,
            RestSiteChoices = ["COOK"],
            CardsRemoved =
            [
                new SerializableCard
                {
                    Id = new ModelId("CARD", "REGRET"),
                },
                new SerializableCard
                {
                    Id = new ModelId("CARD", "STRIKE_SILENT"),
                },
            ],
        };
        var entry = new RunHistoryCampfireEntry(
            40,
            playerEntry.RestSiteChoices,
            playerEntry);

        var outcome = RunHistoryCampfireSummary.BuildOutcomeText(
            entry,
            liftNumber: 0,
            RunHistoryCampfireSummary.HumanizeChoiceId);

        Assert.Contains("Cook — removed Regret, Strike Silent", outcome);
        Assert.Contains("gained 9 Max HP", outcome);
        Assert.Contains("healed 9 HP", outcome);
    }

    [Fact]
    public void BuildOutcomeText_ReportsDigRelicAndLiftProgress()
    {
        var digEntry = new PlayerMapPointHistoryEntry
        {
            PlayerId = 1,
            RestSiteChoices = ["DIG"],
            RelicChoices =
            [
                new ModelChoiceHistoryEntry(
                    new ModelId("RELIC", "ANCHOR"),
                    wasPicked: true),
            ],
        };
        var liftEntry = new PlayerMapPointHistoryEntry
        {
            PlayerId = 1,
            RestSiteChoices = ["LIFT"],
        };

        var digOutcome = RunHistoryCampfireSummary.BuildOutcomeText(
            new RunHistoryCampfireEntry(7, digEntry.RestSiteChoices, digEntry),
            liftNumber: 0,
            RunHistoryCampfireSummary.HumanizeChoiceId);
        var liftOutcome = RunHistoryCampfireSummary.BuildOutcomeText(
            new RunHistoryCampfireEntry(13, liftEntry.RestSiteChoices, liftEntry),
            liftNumber: 2,
            RunHistoryCampfireSummary.HumanizeChoiceId);

        Assert.Equal("Dig — obtained Anchor", digOutcome);
        Assert.Equal("Lift — gained 1 Strength (lift 2 of 3)", liftOutcome);
    }

    [Fact]
    public void CollectEntries_RetainsCampfireWithNoRecordedChoice()
    {
        var history = new RunHistory
        {
            MapPointHistory =
            [
                [Point(MapPointType.RestSite, (1, []))],
            ],
        };

        var entry = Assert.Single(
            RunHistoryCampfireSummary.CollectEntries(history, 1));

        Assert.Equal(1, entry.Floor);
        Assert.Empty(entry.ChoiceIds);
    }

    [Theory]
    [InlineData("SMITH", "Smith")]
    [InlineData("special-cook_option", "Special Cook Option")]
    public void HumanizeChoiceId_ProvidesReadableLocalizationFallback(
        string choiceId,
        string expected)
    {
        Assert.Equal(
            expected,
            RunHistoryCampfireSummary.HumanizeChoiceId(choiceId));
    }

    [Fact]
    public void SelectPlayerPatchTargetExists()
    {
        var target = AccessTools.Method(
            typeof(NRunHistory),
            "SelectPlayer",
            [typeof(NRunHistoryPlayerIcon)]);

        Assert.NotNull(target);
    }

    private static MapPointHistoryEntry Point(
        MapPointType type,
        params (ulong playerId, string[] choices)[] players)
    {
        return new MapPointHistoryEntry
        {
            MapPointType = type,
            PlayerStats = players
                .Select(player => new PlayerMapPointHistoryEntry
                {
                    PlayerId = player.playerId,
                    RestSiteChoices = [.. player.choices],
                })
                .ToList(),
        };
    }
}
