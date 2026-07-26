using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class RainbowRingStatsTests
{
    private const string RainbowRingRelicId = "RELIC.RAINBOW_RING";

    private static readonly MethodInfo BuildRainbowRingBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildRainbowRingBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildRainbowRingBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void Patches_TargetOwnerCardCallbackAndPlayerTurnStart()
    {
        Assert.NotNull(typeof(RainbowRing).GetMethod(
            nameof(RainbowRing.AfterCardPlayed),
            new[] { typeof(PlayerChoiceContext), typeof(CardPlay) }));
        Assert.NotNull(typeof(Hook).GetMethod(
            nameof(Hook.AfterPlayerTurnStart),
            new[] { typeof(ICombatState), typeof(PlayerChoiceContext), typeof(Player) }));
    }

    [Fact]
    public void RelicAggregate_RainbowRingFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0, agg.RainbowRingTurns);
        Assert.Equal(0, agg.RainbowRingCombats);
    }

    [Fact]
    public void RelicAggregate_RainbowRingFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[RainbowRingRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("\"activations\":5", json);
        Assert.Contains("\"rainbow_ring_turns\":12", json);
        Assert.Contains("\"rainbow_ring_combats\":4", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[RainbowRingRelicId];
        Assert.Equal(5, agg.Activations);
        Assert.Equal(12, agg.RainbowRingTurns);
        Assert.Equal(4, agg.RainbowRingCombats);
    }

    [Fact]
    public void RelicTooltip_RainbowRing_ShowsRatesAndLiveTurnState()
    {
        var body = BuildBody(
            PopulatedAggregate(),
            attackPlayedThisTurn: true,
            powerPlayedThisTurn: false,
            skillPlayedThisTurn: true);

        Assert.Contains("Activations", body);
        Assert.Contains("[b]5[/b]", body);
        Assert.Contains("Avg activations per turn", body);
        Assert.Contains("[b]0.42[/b]", body);
        Assert.Contains("Avg activations per combat", body);
        Assert.Contains("[b]1.25[/b]", body);
        Assert.Contains("Attack played this turn", body);
        Assert.Contains("[b]true[/b]", body);
        Assert.Contains("Power played this turn", body);
        Assert.Contains("[b]false[/b]", body);
        Assert.Contains("Skill played this turn", body);
    }

    [Fact]
    public void RunTracker_RainbowRingHelpers_ClampNegativeCounts()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordRainbowRingActivationForTest(agg, 5);
        RunTracker.RecordRainbowRingActivationForTest(agg, -2);
        RunTracker.RecordRainbowRingTurnForTest(agg, 12);
        RunTracker.RecordRainbowRingTurnForTest(agg, -2);
        RunTracker.RecordRainbowRingCombatForTest(agg, 4);
        RunTracker.RecordRainbowRingCombatForTest(agg, -2);

        Assert.Equal(5, agg.Activations);
        Assert.Equal(12, agg.RainbowRingTurns);
        Assert.Equal(4, agg.RainbowRingCombats);
    }

    [Fact]
    public void RelicAggregate_RainbowRingFields_Merge()
    {
        var target = PopulatedAggregate();

        RunTracker.MergeRelicAggregateInto(target, PopulatedAggregate());

        Assert.Equal(10, target.Activations);
        Assert.Equal(24, target.RainbowRingTurns);
        Assert.Equal(8, target.RainbowRingCombats);
    }

    private static RelicAggregate PopulatedAggregate()
        => new()
        {
            Activations = 5,
            RainbowRingTurns = 12,
            RainbowRingCombats = 4,
        };

    private static string BuildBody(
        RelicAggregate agg,
        bool attackPlayedThisTurn,
        bool powerPlayedThisTurn,
        bool skillPlayedThisTurn)
        => (string)(BuildRainbowRingBodyMethod.Invoke(
                null,
                new object?[]
                {
                    agg,
                    attackPlayedThisTurn,
                    powerPlayedThisTurn,
                    skillPlayedThisTurn,
                })
            ?? throw new InvalidOperationException(
                "BuildRainbowRingBodyBBCode returned null."));
}
