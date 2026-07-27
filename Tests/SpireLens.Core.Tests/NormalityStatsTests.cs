using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Models.Cards;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class NormalityStatsTests
{
    private const string NormalityCardId = "CARD.NORMALITY";

    private static readonly MethodInfo AppendNormalityStatsMethod =
        typeof(CardHoverShowPatch).GetMethod(
            "AppendNormalityStats",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("AppendNormalityStats not found.");

    [Fact]
    public void CardAggregate_NormalityFields_DefaultToZero()
    {
        var agg = new CardAggregate();

        Assert.Equal(0, agg.NormalityTurnsEndedInHand);
        Assert.Equal(0, agg.NormalityExcessEnergyAtTurnEndTotal);
    }

    [Fact]
    public void CardAggregate_NormalityFields_JsonRoundtripPreservesValues()
    {
        var run = new RunData();
        run.Aggregates[$"{NormalityCardId}#1"] = new CardAggregate
        {
            NormalityTurnsEndedInHand = 4,
            NormalityExcessEnergyAtTurnEndTotal = 7,
        };

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"normality_turns_ended_in_hand\"", json);
        Assert.Contains("\"normality_excess_energy_at_turn_end_total\"", json);
        Assert.NotNull(restored);

        var agg = restored!.Aggregates[$"{NormalityCardId}#1"];
        Assert.Equal(4, agg.NormalityTurnsEndedInHand);
        Assert.Equal(7, agg.NormalityExcessEnergyAtTurnEndTotal);
    }

    [Fact]
    public void RunTracker_NormalityTurnEnd_IncludesZeroEnergyTurns()
    {
        var agg = new CardAggregate();

        RunTracker.RecordNormalityTurnEndForTest(agg, excessEnergy: 3);
        RunTracker.RecordNormalityTurnEndForTest(agg, excessEnergy: 0);
        RunTracker.RecordNormalityTurnEndForTest(agg, excessEnergy: -2);

        Assert.Equal(3, agg.NormalityTurnsEndedInHand);
        Assert.Equal(3, agg.NormalityExcessEnergyAtTurnEndTotal);
    }

    [Fact]
    public void CardAggregatePooler_NormalityFields_MergeAcrossInstances()
    {
        var pooled = CardAggregatePooler.PoolByDefinition(
            new Dictionary<string, CardAggregate>
            {
                [$"{NormalityCardId}#1"] = new()
                {
                    NormalityTurnsEndedInHand = 2,
                    NormalityExcessEnergyAtTurnEndTotal = 5,
                },
                [$"{NormalityCardId}#2"] = new()
                {
                    NormalityTurnsEndedInHand = 3,
                    NormalityExcessEnergyAtTurnEndTotal = 4,
                },
                ["CARD.DEBT#1"] = new()
                {
                    NormalityTurnsEndedInHand = 99,
                    NormalityExcessEnergyAtTurnEndTotal = 99,
                },
            },
            NormalityCardId);

        Assert.NotNull(pooled);
        Assert.Equal(5, pooled!.NormalityTurnsEndedInHand);
        Assert.Equal(9, pooled.NormalityExcessEnergyAtTurnEndTotal);
    }

    [Fact]
    public void NormalityTooltip_ShowsTurnEndCountAndAverageExcessEnergy()
    {
        var agg = new CardAggregate
        {
            NormalityTurnsEndedInHand = 4,
            NormalityExcessEnergyAtTurnEndTotal = 7,
        };

        var body = AppendNormalityStats(agg);

        Assert.Contains("Turns ended in hand", body);
        Assert.Contains("avg excess at turn end", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.Contains("[b]1.75[/b]", body);
    }

    [Fact]
    public void CardAggregate_OlderShapeWithoutNormalityFields_DefaultsToZero()
    {
        var agg = JsonSerializer.Deserialize<CardAggregate>("{}", RunStorage.Options);

        Assert.NotNull(agg);
        Assert.Equal(0, agg!.NormalityTurnsEndedInHand);
        Assert.Equal(0, agg.NormalityExcessEnergyAtTurnEndTotal);
    }

    private static string AppendNormalityStats(CardAggregate agg)
    {
        var sb = new StringBuilder();
        var card = (Normality)RuntimeHelpers.GetUninitializedObject(typeof(Normality));
        _ = AppendNormalityStatsMethod.Invoke(null, new object?[] { sb, card, agg });
        return sb.ToString();
    }
}
