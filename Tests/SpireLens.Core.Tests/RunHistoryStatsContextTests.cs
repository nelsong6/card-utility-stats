using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class RunHistoryStatsContextTests
{
    [Fact]
    public void SelectAggregateKeysForHistoryEntry_FiltersByFloorAndFinalUpgrade()
    {
        var run = new RunData();
        run.InstanceNumbersByDef["CARD.STRIKE"] = new List<int> { 1, 2, 3 };
        run.Aggregates["CARD.STRIKE#1"] = new CardAggregate
        {
            FloorAdded = 1,
            InitialUpgradeLevel = 0,
        };
        run.Aggregates["CARD.STRIKE#2"] = new CardAggregate
        {
            FloorAdded = 2,
            InitialUpgradeLevel = 1,
        };
        run.Aggregates["CARD.STRIKE#3"] = new CardAggregate
        {
            FloorAdded = 2,
            InitialUpgradeLevel = 0,
        };
        run.Events.Add(new CardEvent
        {
            Type = "card_upgraded",
            CardId = "CARD.STRIKE#3",
            Floor = 4,
            UpgradeLevel = 1,
        });

        var keys = RunHistoryStatsContext.SelectAggregateKeysForHistoryEntry(
            run,
            "CARD.STRIKE",
            amount: 2,
            currentUpgradeLevel: 1,
            floorsAdded: new[] { 2 });

        Assert.Equal(new[] { "CARD.STRIKE#2", "CARD.STRIKE#3" }, keys);
    }

    [Fact]
    public void SelectAggregateKeysForHistoryEntry_AcceptsHistoricPooledShape()
    {
        var run = new RunData();
        run.Aggregates["CARD.SHIV"] = new CardAggregate { Plays = 7 };

        var keys = RunHistoryStatsContext.SelectAggregateKeysForHistoryEntry(
            run,
            "CARD.SHIV",
            amount: 3,
            currentUpgradeLevel: 0,
            floorsAdded: Array.Empty<int>());

        Assert.Equal(new[] { "CARD.SHIV" }, keys);
    }

    [Fact]
    public void SelectAggregateKeysForHistoryEntry_ExpandsWhenFilteredGroupIsShort()
    {
        var run = new RunData();
        run.InstanceNumbersByDef["CARD.DEFEND"] = new List<int> { 1, 2 };
        run.Aggregates["CARD.DEFEND#1"] = new CardAggregate
        {
            FloorAdded = 1,
            InitialUpgradeLevel = 0,
        };
        run.Aggregates["CARD.DEFEND#2"] = new CardAggregate
        {
            FloorAdded = 2,
            InitialUpgradeLevel = 0,
        };

        var keys = RunHistoryStatsContext.SelectAggregateKeysForHistoryEntry(
            run,
            "CARD.DEFEND",
            amount: 2,
            currentUpgradeLevel: 0,
            floorsAdded: new[] { 2 });

        Assert.Equal(new[] { "CARD.DEFEND#2", "CARD.DEFEND#1" }, keys);
    }

    [Fact]
    public void RelicStatsAggregateId_NormalizesFakeRelics()
    {
        Assert.Equal("RELIC.ANCHOR", RelicHoverShowPatch.GetStatsAggregateId(Uninitialized<FakeAnchor>()));
        Assert.Equal("RELIC.STRIKE_DUMMY", RelicHoverShowPatch.GetStatsAggregateId(Uninitialized<FakeStrikeDummy>()));
        Assert.Equal("RELIC.MINIATURE_CANNON", RelicHoverShowPatch.GetStatsAggregateId(Uninitialized<MiniatureCannon>()));
    }

    [Fact]
    public void RelicStatsTooltip_BloodSoakedRose_UsesProvidedEnthralledAggregate()
    {
        var ok = RelicHoverShowPatch.TryBuildBodyBBCode(
            Uninitialized<BloodSoakedRose>(),
            new RelicAggregate
            {
                Activations = 2,
                EnergyGenerated = 5,
            },
            null,
            new CardAggregate
            {
                CombatsInDeck = 4,
                TimesDrawn = 7,
                TimesDiscarded = 3,
                Plays = 1,
                TimesExhausted = 2,
            },
            null,
            out var title,
            out var body);

        Assert.True(ok);
        Assert.Equal("Blood-Soaked Rose", title);
        Assert.Contains("Enthralled combats", body);
        Assert.Contains("Enthralled drawn", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.Contains("[b]7[/b]", body);
    }

    private static T Uninitialized<T>() where T : class
    {
        return (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    }
}
