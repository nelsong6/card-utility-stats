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

public class DrainPowerStatsTests
{
    private const string DrainPowerCardId = "CARD.DRAIN_POWER";

    private static readonly MethodInfo AppendDrainPowerStatsMethod =
        typeof(CardHoverShowPatch).GetMethod(
            "AppendDrainPowerStats",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("AppendDrainPowerStats not found.");

    [Fact]
    public void CardAggregate_DrainPowerFields_DefaultToZero()
    {
        var agg = new CardAggregate();

        Assert.Equal(0, agg.DrainPowerCardsUpgraded);
        Assert.Equal(0, agg.DrainPowerTurnsInDeck);
        Assert.Equal(0, agg.DrainPowerUpgradedCardPlays);
    }

    [Fact]
    public void CardAggregate_DrainPowerFields_JsonRoundtripPreservesFields()
    {
        var run = new RunData();
        run.Aggregates[$"{DrainPowerCardId}#1"] = CreateRepresentativeAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"drain_power_cards_upgraded\"", json);
        Assert.Contains("\"drain_power_turns_in_deck\"", json);
        Assert.Contains("\"drain_power_upgraded_card_plays\"", json);
        Assert.NotNull(restored);
        AssertRepresentativeAggregate(restored!.Aggregates[$"{DrainPowerCardId}#1"]);
    }

    [Fact]
    public void RunTracker_DrainPowerHelpers_RecordOnlyPositiveCounts()
    {
        var agg = new CardAggregate();

        RunTracker.RecordDrainPowerUpgradeForTest(agg, 8);
        RunTracker.RecordDrainPowerTurnForTest(agg, 6);
        RunTracker.RecordDrainPowerUpgradedCardPlayForTest(agg, 9);
        RunTracker.RecordDrainPowerUpgradeForTest(agg, 0);
        RunTracker.RecordDrainPowerTurnForTest(agg, -1);
        RunTracker.RecordDrainPowerUpgradedCardPlayForTest(agg, -2);

        AssertRepresentativeAggregate(agg);
    }

    [Fact]
    public void MergeAggregateInto_DrainPowerFields_Accumulates()
    {
        var target = new CardAggregate
        {
            DrainPowerCardsUpgraded = 2,
            DrainPowerTurnsInDeck = 3,
            DrainPowerUpgradedCardPlays = 4,
        };
        var source = new CardAggregate
        {
            DrainPowerCardsUpgraded = 6,
            DrainPowerTurnsInDeck = 3,
            DrainPowerUpgradedCardPlays = 5,
        };

        RunTracker.MergeAggregateInto(target, source);

        AssertRepresentativeAggregate(target);
    }

    [Fact]
    public void DrainPowerTooltip_FullViewShowsUpgradeAndFollowupPlayAverages()
    {
        var sb = new StringBuilder();

        AppendDrainPowerStats(sb, CreateRepresentativeAggregate(), compact: false);
        var body = sb.ToString();

        Assert.Contains("Cards upgraded", body);
        Assert.Contains("[b]8[/b]", body);
        Assert.Contains("Avg cards upgraded per turn", body);
        Assert.Contains("[b]1.33[/b]", body);
        Assert.Contains("Avg cards upgraded per combat", body);
        Assert.Contains("[b]2.67[/b]", body);
        Assert.Contains("Avg upgraded-card plays per turn", body);
        Assert.Contains("[b]1.5[/b]", body);
        Assert.Contains("Avg upgraded-card plays per combat", body);
        Assert.Contains("[b]3[/b]", body);
    }

    [Fact]
    public void DrainPowerTooltip_CompactViewKeepsOnlyCardsUpgraded()
    {
        var sb = new StringBuilder();

        AppendDrainPowerStats(sb, CreateRepresentativeAggregate(), compact: true);
        var body = sb.ToString();

        Assert.Contains("Cards upgraded", body);
        Assert.DoesNotContain("Avg cards upgraded per turn", body);
        Assert.DoesNotContain("Avg upgraded-card plays per combat", body);
    }

    [Fact]
    public void CardAggregate_OlderShapeWithoutDrainPowerFields_DefaultsToZero()
    {
        var agg = JsonSerializer.Deserialize<CardAggregate>("{}", RunStorage.Options);

        Assert.NotNull(agg);
        Assert.Equal(0, agg!.DrainPowerCardsUpgraded);
        Assert.Equal(0, agg.DrainPowerTurnsInDeck);
        Assert.Equal(0, agg.DrainPowerUpgradedCardPlays);
    }

    private static CardAggregate CreateRepresentativeAggregate() =>
        new()
        {
            CombatsInDeck = 3,
            DrainPowerCardsUpgraded = 8,
            DrainPowerTurnsInDeck = 6,
            DrainPowerUpgradedCardPlays = 9,
        };

    private static void AssertRepresentativeAggregate(CardAggregate agg)
    {
        Assert.Equal(8, agg.DrainPowerCardsUpgraded);
        Assert.Equal(6, agg.DrainPowerTurnsInDeck);
        Assert.Equal(9, agg.DrainPowerUpgradedCardPlays);
    }

    private static void AppendDrainPowerStats(
        StringBuilder sb,
        CardAggregate agg,
        bool compact)
    {
        var card = (DrainPower)RuntimeHelpers.GetUninitializedObject(typeof(DrainPower));
        _ = AppendDrainPowerStatsMethod.Invoke(null, new object?[] { sb, card, agg, compact });
    }
}
