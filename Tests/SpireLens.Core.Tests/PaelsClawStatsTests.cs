using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class PaelsClawStatsTests
{
    private const string PaelsClawRelicId = "RELIC.PAELS_CLAW";

    private static readonly MethodInfo BuildPaelsClawBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildPaelsClawBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildPaelsClawBodyBBCode not found.");

    [Fact]
    public void Patches_TargetExactPaelsClawAndGoopyCallbacks()
    {
        var obtained = typeof(PaelsClaw).GetMethod(nameof(PaelsClaw.AfterObtained));
        var played = typeof(Goopy).GetMethod(nameof(Goopy.AfterCardPlayed));

        Assert.NotNull(obtained);
        Assert.Empty(obtained!.GetParameters());
        Assert.NotNull(played);
        Assert.Equal(
            new[] { typeof(PlayerChoiceContext), typeof(CardPlay) },
            played!.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void RelicAggregate_PaelsClawFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.PaelsClawGoopyCardsPlayed);
        Assert.Equal(0, agg.PaelsClawGoopyEnhancements);
        Assert.Equal(0, agg.PaelsClawGoopyCards);
        Assert.Equal(0, agg.PaelsClawTurns);
        Assert.Equal(0, agg.PaelsClawCombats);
    }

    [Fact]
    public void RelicAggregate_PaelsClawFields_JsonRoundtripPreservesValues()
    {
        var run = new RunData();
        run.RelicAggregates[PaelsClawRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"paels_claw_goopy_cards_played\"", json);
        Assert.Contains("\"paels_claw_goopy_enhancements\"", json);
        Assert.Contains("\"paels_claw_goopy_cards\"", json);
        Assert.Contains("\"paels_claw_turns\"", json);
        Assert.Contains("\"paels_claw_combats\"", json);
        Assert.NotNull(restored);

        AssertPopulatedAggregate(restored!.RelicAggregates[PaelsClawRelicId]);
    }

    [Fact]
    public void RunTracker_PaelsClawHelpers_AccumulatePlaysObservedEnhancementsAndDenominators()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordPaelsClawSnapshotForTest(agg, 4, 2);
        RunTracker.RecordPaelsClawGoopyCardPlayedForTest(agg, 7);
        RunTracker.RecordPaelsClawEnhancementForTest(agg, 2, 3);
        RunTracker.RecordPaelsClawEnhancementForTest(agg, 4, 6);
        RunTracker.RecordPaelsClawTurnForTest(agg, 4);
        RunTracker.RecordPaelsClawCombatForTest(agg, 2);

        AssertPopulatedAggregate(agg);
    }

    [Fact]
    public void RunTracker_PaelsClawHelpers_ClampNegativeValuesAndKeepLargestSnapshot()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordPaelsClawSnapshotForTest(agg, 4, 3);
        RunTracker.RecordPaelsClawSnapshotForTest(agg, 2, 1);
        RunTracker.RecordPaelsClawGoopyCardPlayedForTest(agg, -2);
        RunTracker.RecordPaelsClawEnhancementForTest(agg, 5, 3);
        RunTracker.RecordPaelsClawTurnForTest(agg, -4);
        RunTracker.RecordPaelsClawCombatForTest(agg, -2);

        Assert.Equal(0, agg.PaelsClawGoopyCardsPlayed);
        Assert.Equal(3, agg.PaelsClawGoopyEnhancements);
        Assert.Equal(4, agg.PaelsClawGoopyCards);
        Assert.Equal(0, agg.PaelsClawTurns);
        Assert.Equal(0, agg.PaelsClawCombats);
    }

    [Fact]
    public void RelicAggregate_PaelsClawFields_MergeAddsOutcomesAndKeepsLargestCardDenominator()
    {
        var target = PopulatedAggregate();
        var source = PopulatedAggregate();
        source.PaelsClawGoopyCards = 3;

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(14, target.PaelsClawGoopyCardsPlayed);
        Assert.Equal(10, target.PaelsClawGoopyEnhancements);
        Assert.Equal(4, target.PaelsClawGoopyCards);
        Assert.Equal(8, target.PaelsClawTurns);
        Assert.Equal(4, target.PaelsClawCombats);
    }

    [Fact]
    public void RelicTooltip_PaelsClaw_ShowsRequestedTotalsAndAverages()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("Goopy cards played", body);
        Assert.Contains("Avg Goopy cards played per turn", body);
        Assert.Contains("Avg Goopy cards played per combat", body);
        Assert.Contains("Avg number of Goopy enhancements per card with Goopy", body);
        Assert.Contains("[b]7[/b]", body);
        Assert.Contains("[b]1.75[/b]", body);
        Assert.Contains("[b]3.5[/b]", body);
        Assert.Contains("[b]1.25[/b]", body);
    }

    [Fact]
    public void RelicTooltip_PaelsClaw_ShowsZeroAveragesWithoutDenominators()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Goopy cards played", body);
        Assert.Contains("Avg number of Goopy enhancements per card with Goopy", body);
        Assert.Contains("[b]0[/b]", body);
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void RelicTooltip_PaelsClaw_DispatchesForModel()
    {
        var relic = (PaelsClaw)RuntimeHelpers.GetUninitializedObject(typeof(PaelsClaw));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Pael's Claw", title);
        Assert.Contains("Goopy cards played", body);
    }

    [Fact]
    public void RelicAggregate_OlderShapeWithoutPaelsClawFields_DefaultsToZero()
    {
        var agg = JsonSerializer.Deserialize<RelicAggregate>("{}", RunStorage.Options);

        Assert.NotNull(agg);
        Assert.Equal(0, agg!.PaelsClawGoopyCardsPlayed);
        Assert.Equal(0, agg.PaelsClawGoopyEnhancements);
        Assert.Equal(0, agg.PaelsClawGoopyCards);
        Assert.Equal(0, agg.PaelsClawTurns);
        Assert.Equal(0, agg.PaelsClawCombats);
    }

    private static RelicAggregate PopulatedAggregate()
    {
        var agg = new RelicAggregate();
        RunTracker.RecordPaelsClawSnapshotForTest(agg, 4, 2);
        RunTracker.RecordPaelsClawGoopyCardPlayedForTest(agg, 7);
        RunTracker.RecordPaelsClawEnhancementForTest(agg, 2, 3);
        RunTracker.RecordPaelsClawEnhancementForTest(agg, 4, 6);
        RunTracker.RecordPaelsClawTurnForTest(agg, 4);
        RunTracker.RecordPaelsClawCombatForTest(agg, 2);
        return agg;
    }

    private static void AssertPopulatedAggregate(RelicAggregate agg)
    {
        Assert.Equal(7, agg.PaelsClawGoopyCardsPlayed);
        Assert.Equal(5, agg.PaelsClawGoopyEnhancements);
        Assert.Equal(4, agg.PaelsClawGoopyCards);
        Assert.Equal(4, agg.PaelsClawTurns);
        Assert.Equal(2, agg.PaelsClawCombats);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildPaelsClawBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildPaelsClawBodyBBCode returned null."));
}
