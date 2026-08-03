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
    public void HpTooltip_ShowsLossRatesAndMaximumHpTotals()
    {
        var body = MaxHpHistoryTooltip.BuildBodyBBCode(
            new RunHealthStats
            {
                HpLostInCombats = 30m,
                HpLostInEvents = 10m,
                Combats = 3,
            },
            floors: 8,
            history:
            [
                new MaxHpRunHistoryEntry
                {
                    Sequence = 1,
                    Floor = 3,
                    SourceName = "Mango",
                    PreviousMaxHp = 70,
                    NewMaxHp = 84,
                },
                new MaxHpRunHistoryEntry
                {
                    Sequence = 2,
                    Floor = 7,
                    SourceName = "Drowning Beacon",
                    PreviousMaxHp = 84,
                    NewMaxHp = 77,
                },
            ]);

        Assert.Contains("HP lost in combats   [b]30[/b]", body);
        Assert.Contains("HP lost in events   [b]10[/b]", body);
        Assert.Contains("Avg HP lost per floor   [b]5[/b]", body);
        Assert.Contains("Avg HP lost per combat   [b]10[/b]", body);
        Assert.Contains("Max HP gained   [b]14[/b]", body);
        Assert.Contains("Max HP lost   [b]7[/b]", body);
        Assert.Contains(
            StatConceptGlossary.RenderInformationHint("HP lost to combat damage this run."),
            body);
        Assert.Contains("70 → 84", body);
        Assert.Contains("84 → 77", body);
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

    [Fact]
    public void CombatPromotion_AddsHpLossAndZeroInclusiveCombatCount()
    {
        var run = new RunData
        {
            HealthStats = new RunHealthStats
            {
                HpLostInCombats = 10m,
                HpLostInEvents = 4m,
                Combats = 2,
            },
        };
        var pending = new PendingCombat
        {
            HealthStats = new RunHealthStats
            {
                HpLostInCombats = 7m,
                Combats = 1,
            },
        };

        RunTracker.PromotePendingCombatIntoRun(pending, run);

        Assert.Equal(17m, run.HealthStats.HpLostInCombats);
        Assert.Equal(4m, run.HealthStats.HpLostInEvents);
        Assert.Equal(3, run.HealthStats.Combats);
    }
}
