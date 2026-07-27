using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class CentennialPuzzleStatsTests
{
    private const string CentennialPuzzleRelicId = "RELIC.CENTENNIAL_PUZZLE";

    private static readonly MethodInfo BuildCentennialPuzzleBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildCentennialPuzzleBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildCentennialPuzzleBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_CentennialPuzzleFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0, agg.AdditionalCardsDrawn);
        Assert.Equal(0, agg.CentennialPuzzleActivationTurnTotal);
        Assert.Equal(0, agg.CentennialPuzzleActivationTurnSamples);
        Assert.Equal(0, agg.CentennialPuzzlePlayerTurnActivations);
        Assert.Equal(0, agg.CentennialPuzzleOpponentTurnActivations);
        Assert.Equal(0, agg.CentennialPuzzleStatusActivations);
        Assert.Equal(0, agg.CentennialPuzzleCurseActivations);
        Assert.Equal(0, agg.CentennialPuzzleEnemySourceActivations);
    }

    [Fact]
    public void RelicAggregate_CentennialPuzzleFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[CentennialPuzzleRelicId] = new RelicAggregate
        {
            Activations = 4,
            AdditionalCardsDrawn = 11,
            CentennialPuzzleActivationTurnTotal = 9,
            CentennialPuzzleActivationTurnSamples = 4,
            CentennialPuzzlePlayerTurnActivations = 3,
            CentennialPuzzleOpponentTurnActivations = 1,
            CentennialPuzzleStatusActivations = 1,
            CentennialPuzzleCurseActivations = 1,
            CentennialPuzzleEnemySourceActivations = 2,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("additional_cards_drawn", json);
        Assert.Contains("centennial_puzzle_activation_turn_total", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[CentennialPuzzleRelicId];
        Assert.Equal(4, agg.Activations);
        Assert.Equal(11, agg.AdditionalCardsDrawn);
        Assert.Equal(9, agg.CentennialPuzzleActivationTurnTotal);
        Assert.Equal(4, agg.CentennialPuzzleActivationTurnSamples);
        Assert.Equal(3, agg.CentennialPuzzlePlayerTurnActivations);
        Assert.Equal(1, agg.CentennialPuzzleOpponentTurnActivations);
        Assert.Equal(1, agg.CentennialPuzzleStatusActivations);
        Assert.Equal(1, agg.CentennialPuzzleCurseActivations);
        Assert.Equal(2, agg.CentennialPuzzleEnemySourceActivations);
    }

    [Theory]
    [InlineData(CardType.Status, null, false, true, "Status")]
    [InlineData(CardType.Curse, null, false, true, "Curse")]
    [InlineData(null, PowerType.Debuff, true, false, "Enemy")]
    [InlineData(null, null, false, true, "Enemy")]
    [InlineData(CardType.Skill, null, false, false, "Other")]
    public void SourceClassification_UsesMutuallyExclusiveReliableSignals(
        CardType? cardType,
        PowerType? powerType,
        bool powerOwnedByPlayer,
        bool dealerIsEnemy,
        string expected)
    {
        Assert.Equal(
            Enum.Parse<CentennialPuzzleActivationSource>(expected),
            RunTracker.ClassifyCentennialPuzzleActivationSourceForTest(
                cardType,
                powerType,
                powerOwnedByPlayer,
                dealerIsEnemy));
    }

    [Fact]
    public void Recording_ActivationAddsTurnSideAndAtMostOneSourceBucket()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordCentennialPuzzleActivationForTest(
            agg,
            turnNumber: 2,
            side: CombatSide.Player,
            source: CentennialPuzzleActivationSource.Status);
        RunTracker.RecordCentennialPuzzleActivationForTest(
            agg,
            turnNumber: 3,
            side: CombatSide.Enemy,
            source: CentennialPuzzleActivationSource.Curse);
        RunTracker.RecordCentennialPuzzleActivationForTest(
            agg,
            turnNumber: 4,
            side: CombatSide.Enemy,
            source: CentennialPuzzleActivationSource.Enemy);
        RunTracker.RecordCentennialPuzzleActivationForTest(
            agg,
            turnNumber: -1,
            side: null,
            source: CentennialPuzzleActivationSource.Other);

        Assert.Equal(4, agg.Activations);
        Assert.Equal(9, agg.CentennialPuzzleActivationTurnTotal);
        Assert.Equal(3, agg.CentennialPuzzleActivationTurnSamples);
        Assert.Equal(1, agg.CentennialPuzzlePlayerTurnActivations);
        Assert.Equal(2, agg.CentennialPuzzleOpponentTurnActivations);
        Assert.Equal(1, agg.CentennialPuzzleStatusActivations);
        Assert.Equal(1, agg.CentennialPuzzleCurseActivations);
        Assert.Equal(1, agg.CentennialPuzzleEnemySourceActivations);
    }

    [Fact]
    public void MergeRelicAggregateInto_CentennialPuzzleFields_Accumulate()
    {
        var target = new RelicAggregate
        {
            CentennialPuzzleActivationTurnTotal = 2,
            CentennialPuzzleActivationTurnSamples = 1,
            CentennialPuzzlePlayerTurnActivations = 1,
            CentennialPuzzleStatusActivations = 1,
        };
        var source = new RelicAggregate
        {
            CentennialPuzzleActivationTurnTotal = 7,
            CentennialPuzzleActivationTurnSamples = 3,
            CentennialPuzzlePlayerTurnActivations = 2,
            CentennialPuzzleOpponentTurnActivations = 1,
            CentennialPuzzleCurseActivations = 1,
            CentennialPuzzleEnemySourceActivations = 2,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(9, target.CentennialPuzzleActivationTurnTotal);
        Assert.Equal(4, target.CentennialPuzzleActivationTurnSamples);
        Assert.Equal(3, target.CentennialPuzzlePlayerTurnActivations);
        Assert.Equal(1, target.CentennialPuzzleOpponentTurnActivations);
        Assert.Equal(1, target.CentennialPuzzleStatusActivations);
        Assert.Equal(1, target.CentennialPuzzleCurseActivations);
        Assert.Equal(2, target.CentennialPuzzleEnemySourceActivations);
    }

    [Fact]
    public void RelicTooltip_CentennialPuzzle_ShowsActivationTotalAndAverageRows()
    {
        var body = BuildBody(new RelicAggregate
        {
            Activations = 4,
            AdditionalCardsDrawn = 11,
            CentennialPuzzleActivationTurnTotal = 9,
            CentennialPuzzleActivationTurnSamples = 4,
            CentennialPuzzlePlayerTurnActivations = 3,
            CentennialPuzzleOpponentTurnActivations = 1,
            CentennialPuzzleStatusActivations = 1,
            CentennialPuzzleCurseActivations = 1,
            CentennialPuzzleEnemySourceActivations = 2,
        });

        Assert.Contains("Activations", body);
        Assert.Contains("Triggered this combat", body);
        Assert.Contains("[b]false[/b]", body);
        Assert.Contains("Cards drawn total", body);
        Assert.Contains("Avg cards drawn per combat", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.Contains("[b]11[/b]", body);
        Assert.Contains("[b]2.75[/b]", body);
        Assert.Contains("average player turn number", body);
        Assert.Contains("[b]2.25[/b]", body);
        Assert.Contains("during your turn", body);
        Assert.Contains("during the opponent's turn", body);
        Assert.Contains("Status card caused", body);
        Assert.Contains("Curse card caused", body);
        Assert.Contains("enemy attack or enemy-applied debuff", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[b]1[/b]", body);
        Assert.Contains("[b]2[/b]", body);
    }

    [Fact]
    public void RelicTooltip_CentennialPuzzle_ShowsZeroAverageWithoutActivations()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Cards drawn total", body);
        Assert.Contains("Avg cards drawn per combat", body);
        Assert.Contains("Triggered this combat", body);
        Assert.Contains("[b]false[/b]", body);
        Assert.Contains("[b]0[/b]", body);
    }

    [Fact]
    public void RelicTooltip_CentennialPuzzle_CanShowTriggeredThisCombatTrue()
    {
        var body = BuildBody(new RelicAggregate(), triggeredThisCombat: true);

        Assert.Contains("Triggered this combat", body);
        Assert.Contains("[b]true[/b]", body);
    }

    private static string BuildBody(RelicAggregate agg, bool triggeredThisCombat = false)
        => (string)(BuildCentennialPuzzleBodyMethod.Invoke(
            null,
            new object?[] { agg, triggeredThisCombat })
            ?? throw new InvalidOperationException("BuildCentennialPuzzleBodyBBCode returned null."));
}
