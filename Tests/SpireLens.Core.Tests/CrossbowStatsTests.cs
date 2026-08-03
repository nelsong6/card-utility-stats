using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class CrossbowStatsTests
{
    private const string CrossbowRelicId = "RELIC.CROSSBOW";

    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildCrossbowBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildCrossbowBodyBBCode not found.");

    [Fact]
    public void Patches_TargetCrossbowDiscountAndGeneratedCardCallbacks()
    {
        var turnStart = typeof(Crossbow).GetMethod(nameof(Crossbow.AfterSideTurnStart));
        var setFree = typeof(CardModel).GetMethod(nameof(CardModel.SetToFreeThisTurn));
        var addGenerated = typeof(CardPileCmd).GetMethod(
            nameof(CardPileCmd.AddGeneratedCardsToCombat),
            new[]
            {
                typeof(IEnumerable<CardModel>),
                typeof(PileType),
                typeof(Player),
                typeof(CardPilePosition),
            });

        Assert.NotNull(turnStart);
        Assert.Equal(
            new[]
            {
                typeof(CombatSide),
                typeof(IReadOnlyList<Creature>),
                typeof(ICombatState),
            },
            turnStart!.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.NotNull(setFree);
        Assert.NotNull(addGenerated);
    }

    [Fact]
    public void RelicAggregate_CrossbowFields_DefaultToZero()
    {
        AssertCrossbowAggregate(new RelicAggregate(), 0, 0, 0, 0, 0m, 0, 0);
    }

    [Fact]
    public void RunTracker_CrossbowHelpers_CountSuccessfulAttacksRaritiesAndDiscount()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordCrossbowAttackGainedForTest(agg, true, CardRarity.Common, 2m);
        RunTracker.RecordCrossbowAttackGainedForTest(agg, true, CardRarity.Uncommon, 1m);
        RunTracker.RecordCrossbowAttackGainedForTest(agg, true, CardRarity.Rare, 3m);
        RunTracker.RecordCrossbowAttackGainedForTest(agg, true, CardRarity.Basic, -2m);
        RunTracker.RecordCrossbowAttackGainedForTest(agg, false, CardRarity.Rare, 9m);
        RunTracker.RecordCrossbowTurnForTest(agg, 4);
        RunTracker.RecordCrossbowTurnForTest(agg, -1);
        RunTracker.RecordCrossbowCombatForTest(agg, 2);
        RunTracker.RecordCrossbowCombatForTest(agg, -1);

        AssertCrossbowAggregate(agg, 4, 1, 1, 1, 6m, 4, 2);
    }

    [Fact]
    public void RelicAggregate_CrossbowFields_JsonRoundtripPreservesValues()
    {
        var run = new RunData();
        run.RelicAggregates[CrossbowRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"crossbow_attacks_gained\"", json);
        Assert.Contains("\"crossbow_common_attacks_gained\"", json);
        Assert.Contains("\"crossbow_uncommon_attacks_gained\"", json);
        Assert.Contains("\"crossbow_rare_attacks_gained\"", json);
        Assert.Contains("\"crossbow_discount_given_total\"", json);
        Assert.Contains("\"crossbow_turns\"", json);
        Assert.Contains("\"crossbow_combats\"", json);
        Assert.NotNull(restored);
        AssertCrossbowAggregate(
            restored!.RelicAggregates[CrossbowRelicId],
            6,
            2,
            2,
            1,
            9m,
            4,
            2);
    }

    [Fact]
    public void RelicAggregate_CrossbowFields_Merge()
    {
        var target = PopulatedAggregate();

        RunTracker.MergeRelicAggregateInto(target, PopulatedAggregate());

        AssertCrossbowAggregate(target, 12, 4, 4, 2, 18m, 8, 4);
    }

    [Fact]
    public void RelicTooltip_Crossbow_ShowsRequestedTotalsAndAverages()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("Attacks gained from Crossbow", body);
        Assert.Contains("Average Attacks gained from Crossbow per combat", body);
        Assert.Contains("Average effective energy discount", body);
        Assert.Contains("per player turn", body);
        Assert.Contains("per combat", body);
        Assert.Contains("Common Attacks gained from Crossbow", body);
        Assert.Contains("Uncommon Attacks gained from Crossbow", body);
        Assert.Contains("Rare Attacks gained from Crossbow", body);
        Assert.Contains("[b]2.25[/b]", body);
        Assert.Contains("[b]4.5[/b]", body);
    }

    [Fact]
    public void RelicTooltip_Crossbow_DispatchesForModel()
    {
        var relic = (Crossbow)RuntimeHelpers.GetUninitializedObject(typeof(Crossbow));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            PopulatedAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Crossbow", title);
        Assert.Contains("Attacks gained from Crossbow", body);
    }

    [Fact]
    public void RelicAggregate_OlderShapeWithoutCrossbowFields_DefaultsToZero()
    {
        var agg = JsonSerializer.Deserialize<RelicAggregate>("{}", RunStorage.Options);

        Assert.NotNull(agg);
        AssertCrossbowAggregate(agg!, 0, 0, 0, 0, 0m, 0, 0);
    }

    private static RelicAggregate PopulatedAggregate()
        => new()
        {
            CrossbowAttacksGained = 6,
            CrossbowCommonAttacksGained = 2,
            CrossbowUncommonAttacksGained = 2,
            CrossbowRareAttacksGained = 1,
            CrossbowDiscountGivenTotal = 9m,
            CrossbowTurns = 4,
            CrossbowCombats = 2,
        };

    private static void AssertCrossbowAggregate(
        RelicAggregate agg,
        int attacksGained,
        int common,
        int uncommon,
        int rare,
        decimal discount,
        int turns,
        int combats)
    {
        Assert.Equal(attacksGained, agg.CrossbowAttacksGained);
        Assert.Equal(common, agg.CrossbowCommonAttacksGained);
        Assert.Equal(uncommon, agg.CrossbowUncommonAttacksGained);
        Assert.Equal(rare, agg.CrossbowRareAttacksGained);
        Assert.Equal(discount, agg.CrossbowDiscountGivenTotal);
        Assert.Equal(turns, agg.CrossbowTurns);
        Assert.Equal(combats, agg.CrossbowCombats);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildBodyMethod.Invoke(null, new object[] { agg })
                    ?? throw new InvalidOperationException("Builder returned null."));
}
