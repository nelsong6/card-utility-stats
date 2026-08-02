using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class MaxHpHistoryTests
{
    [Fact]
    public void Tooltip_ListsEveryChangeInSequenceOrder()
    {
        var history = new[]
        {
            new MaxHpRunHistoryEntry
            {
                Sequence = 2,
                Floor = 9,
                SourceName = "Chosen Cheese",
                PreviousMaxHp = 63,
                NewMaxHp = 66,
            },
            new MaxHpRunHistoryEntry
            {
                Sequence = 1,
                Floor = 4,
                SourceName = "Drowning Beacon",
                PreviousMaxHp = 70,
                NewMaxHp = 63,
            },
        };

        var body = MaxHpHistoryTooltip.BuildBodyBBCode(history);

        Assert.Contains("Drowning Beacon", body);
        Assert.Contains("[b]-7[/b]", body);
        Assert.Contains("70 → 63", body);
        Assert.Contains("Chosen Cheese", body);
        Assert.Contains("[b]+3[/b]", body);
        Assert.True(
            body.IndexOf("Drowning Beacon", StringComparison.Ordinal)
            < body.IndexOf("Chosen Cheese", StringComparison.Ordinal));
    }

    [Fact]
    public void Tooltip_UsesLocationWhenSourceIsUnknown()
    {
        var body = MaxHpHistoryTooltip.BuildBodyBBCode(
        [
            new MaxHpRunHistoryEntry
            {
                Sequence = 1,
                Floor = 7,
                LocationKind = "Event",
                LocationName = "The Cursed Fountain",
                PreviousMaxHp = 80,
                NewMaxHp = 75,
            },
        ]);

        Assert.Contains("The Cursed Fountain", body);
        Assert.Contains("80 → 75", body);
    }

    [Fact]
    public void CombatPromotion_ReplacesCommittedHistoryWithPendingSnapshot()
    {
        var run = new RunData
        {
            MaxHpHistory =
            [
                new MaxHpRunHistoryEntry
                {
                    Sequence = 1,
                    Floor = 4,
                    PreviousMaxHp = 70,
                    NewMaxHp = 75,
                },
            ],
        };
        var pending = new PendingCombat
        {
            MaxHpHistory =
            [
                new MaxHpRunHistoryEntry
                {
                    Sequence = 1,
                    Floor = 4,
                    PreviousMaxHp = 70,
                    NewMaxHp = 75,
                },
                new MaxHpRunHistoryEntry
                {
                    Sequence = 2,
                    Floor = 9,
                    PreviousMaxHp = 75,
                    NewMaxHp = 78,
                },
            ],
        };

        RunTracker.PromotePendingCombatIntoRun(pending, run);

        Assert.Equal(2, run.MaxHpHistory.Count);
        Assert.Equal(78, run.MaxHpHistory[1].NewMaxHp);
        Assert.NotSame(pending.MaxHpHistory, run.MaxHpHistory);
    }
}
