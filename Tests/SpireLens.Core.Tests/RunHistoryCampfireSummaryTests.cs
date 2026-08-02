using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
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
            },
            entry =>
            {
                Assert.Equal(3, entry.Floor);
                Assert.Equal(["DIG"], entry.ChoiceIds);
            });
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
