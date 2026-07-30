using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Models.Cards;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class ArmamentsStatsTests
{
    private const string ArmamentsCardId = "CARD.ARMAMENTS";

    private static readonly MethodInfo AppendArmamentsStatsMethod =
        typeof(CardHoverShowPatch).GetMethod(
            "AppendArmamentsStats",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("AppendArmamentsStats not found.");

    [Fact]
    public void CardAggregate_ArmamentsCardsUpgraded_DefaultsToZero()
    {
        var agg = new CardAggregate();

        Assert.Equal(0, agg.ArmamentsCardsUpgraded);
    }

    [Fact]
    public void CardAggregate_ArmamentsCardsUpgraded_JsonRoundtripPreservesValue()
    {
        var run = new RunData();
        run.Aggregates[$"{ArmamentsCardId}#1"] = new CardAggregate
        {
            ArmamentsCardsUpgraded = 9,
        };

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"armaments_cards_upgraded\"", json);
        Assert.NotNull(restored);
        Assert.Equal(
            9,
            restored!.Aggregates[$"{ArmamentsCardId}#1"].ArmamentsCardsUpgraded);
    }

    [Fact]
    public void RunTracker_ArmamentsHelper_RecordsOnlyPositiveCounts()
    {
        var agg = new CardAggregate();

        RunTracker.RecordArmamentsUpgradeForTest(agg, 9);
        RunTracker.RecordArmamentsUpgradeForTest(agg, 0);
        RunTracker.RecordArmamentsUpgradeForTest(agg, -1);

        Assert.Equal(9, agg.ArmamentsCardsUpgraded);
    }

    [Fact]
    public void MergeAggregateInto_ArmamentsCardsUpgraded_Accumulates()
    {
        var target = new CardAggregate { ArmamentsCardsUpgraded = 4 };
        var source = new CardAggregate { ArmamentsCardsUpgraded = 5 };

        RunTracker.MergeAggregateInto(target, source);

        Assert.Equal(9, target.ArmamentsCardsUpgraded);
    }

    [Fact]
    public void ArmamentsTooltip_ShowsCardsUpgradedAtZeroAndAfterUpgrades()
    {
        var zero = new StringBuilder();
        var populated = new StringBuilder();

        AppendArmamentsStats(zero, new CardAggregate());
        AppendArmamentsStats(
            populated,
            new CardAggregate { ArmamentsCardsUpgraded = 9 });

        Assert.Contains("Cards upgraded", zero.ToString());
        Assert.Contains("[b]0[/b]", zero.ToString());
        Assert.Contains("Cards upgraded", populated.ToString());
        Assert.Contains("[b]9[/b]", populated.ToString());
    }

    [Fact]
    public void CardAggregate_OlderShapeWithoutArmamentsField_DefaultsToZero()
    {
        var agg = JsonSerializer.Deserialize<CardAggregate>("{}", RunStorage.Options);

        Assert.NotNull(agg);
        Assert.Equal(0, agg!.ArmamentsCardsUpgraded);
    }

    private static void AppendArmamentsStats(
        StringBuilder sb,
        CardAggregate agg)
    {
        var card = (Armaments)RuntimeHelpers.GetUninitializedObject(typeof(Armaments));
        _ = AppendArmamentsStatsMethod.Invoke(null, new object?[] { sb, card, agg });
    }
}
