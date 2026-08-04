using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

/// <summary>
/// Pins Game Piece's result math and tooltip projection. Live hook timing
/// remains user-owned gameplay verification.
/// </summary>
public class GamePieceStatsTests
{
    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildGamePieceBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildGamePieceBodyBBCode not found.");

    [Fact]
    public void TrackingMath_AccumulatesPowerPlaysAndObservedDrawOutcomes()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordGamePieceStatsForTest(
            agg, powersPlayed: 1, cardsRequested: 1, cardsDrawn: 1);
        RunTracker.RecordGamePieceStatsForTest(
            agg, powersPlayed: 1, cardsRequested: 1, cardsDrawn: 0);
        RunTracker.RecordGamePieceStatsForTest(
            agg, powersPlayed: -1, cardsRequested: -1, cardsDrawn: -1);
        RunTracker.RecordGamePieceTurnForTest(agg, 6);
        RunTracker.RecordGamePieceTurnForTest(agg, -1);
        RunTracker.RecordGamePieceCombatForTest(agg, 2);
        RunTracker.RecordGamePieceCombatForTest(agg, -1);

        Assert.Equal(2, agg.Activations);
        Assert.Equal(1, agg.AdditionalCardsDrawn);
        Assert.Equal(1, agg.AdditionalCardDrawsBlocked);
        Assert.Equal(6, agg.GamePieceTurns);
        Assert.Equal(2, agg.GamePieceCombats);
    }

    [Fact]
    public void Tooltip_ShowsZeroInclusiveTurnAndCombatAverages()
    {
        var agg = new RelicAggregate
        {
            Activations = 4,
            AdditionalCardsDrawn = 3,
            AdditionalCardDrawsBlocked = 1,
            GamePieceTurns = 6,
            GamePieceCombats = 2,
        };

        var body = (string)(BuildBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException(
                "BuildGamePieceBodyBBCode returned null."));

        Assert.Contains("Power cards played by Game Piece's owner", body);
        Assert.Contains("Cards drawn", body);
        Assert.Contains("Card draws blocked", body);
        Assert.Contains("Average cards drawn per turn", body);
        Assert.Contains("Average cards drawn per combat", body);
        Assert.DoesNotContain("Average cards drawn per Power", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[b]1[/b]", body);
        Assert.Contains("[b]0.5[/b]", body);
        Assert.Contains("[b]1.5[/b]", body);
    }

    [Fact]
    public void RelicAggregate_GamePieceDenominators_RoundTripAndMerge()
    {
        var run = new RunData();
        run.RelicAggregates["RELIC.GAME_PIECE"] = new RelicAggregate
        {
            GamePieceTurns = 6,
            GamePieceCombats = 2,
        };

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"game_piece_turns\"", json);
        Assert.Contains("\"game_piece_combats\"", json);
        Assert.NotNull(restored);
        Assert.Equal(6, restored!.RelicAggregates["RELIC.GAME_PIECE"].GamePieceTurns);
        Assert.Equal(2, restored.RelicAggregates["RELIC.GAME_PIECE"].GamePieceCombats);

        var merged = new RelicAggregate
        {
            GamePieceTurns = 1,
            GamePieceCombats = 1,
        };
        RunTracker.MergeRelicAggregateInto(
            merged,
            new RelicAggregate
            {
                GamePieceTurns = 5,
                GamePieceCombats = 1,
            });

        Assert.Equal(6, merged.GamePieceTurns);
        Assert.Equal(2, merged.GamePieceCombats);
    }

    [Fact]
    public void TooltipDispatch_RecognizesGamePiece()
    {
        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            (GamePiece)RuntimeHelpers.GetUninitializedObject(typeof(GamePiece)),
            new RelicAggregate
            {
                Activations = 1,
                AdditionalCardsDrawn = 1,
            },
            floorCount: null,
            bloodSoakedRoseCurseAgg: null,
            cursedPearlCurseAgg: null,
            neowsBonesCurseAggs: null,
            storybookBrightestFlameAgg: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Game Piece", title);
        Assert.Contains("[b]1[/b]", body);
    }
}
