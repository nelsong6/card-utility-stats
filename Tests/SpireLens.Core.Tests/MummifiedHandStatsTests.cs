using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class MummifiedHandStatsTests
{
    private const string MummifiedHandRelicId = "RELIC.MUMMIFIED_HAND";

    private static readonly MethodInfo BuildMummifiedHandBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildMummifiedHandBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildMummifiedHandBodyBBCode not found.");

    [Fact]
    public void Patch_TargetsMummifiedHandAfterCardPlayedWithExpectedParameters()
    {
        var target = typeof(MummifiedHand).GetMethod(nameof(MummifiedHand.AfterCardPlayed));

        Assert.NotNull(target);
        Assert.Equal(
            new[] { typeof(PlayerChoiceContext), typeof(CardPlay) },
            target!.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void RelicAggregate_MummifiedHandFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.MummifiedHandTriggeringPowerCostTotal);
        Assert.Equal(0m, agg.MummifiedHandDiscountGivenTotal);
        Assert.Equal(0m, agg.MummifiedHandEnergySpentToDiscountedCostRatioTotal);
        Assert.Equal(0, agg.MummifiedHandEnergySpentToDiscountedCostRatioCount);
        Assert.Equal(0, agg.MummifiedHandCombats);
        Assert.Equal(0, agg.MummifiedHandTurns);
        Assert.Equal(0, agg.MummifiedHandDiscountedPowers);
        Assert.Equal(0, agg.MummifiedHandDiscountedAttacks);
        Assert.Equal(0, agg.MummifiedHandDiscountedSkills);
        Assert.Equal(0, agg.MummifiedHandDiscountedCommons);
        Assert.Equal(0, agg.MummifiedHandDiscountedUncommons);
        Assert.Equal(0, agg.MummifiedHandDiscountedRares);
    }

    [Fact]
    public void RelicAggregate_MummifiedHandFields_JsonRoundtripPreservesValues()
    {
        var run = new RunData();
        run.RelicAggregates[MummifiedHandRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"mummified_hand_triggering_power_cost_total\"", json);
        Assert.Contains("\"mummified_hand_discount_given_total\"", json);
        Assert.Contains("\"mummified_hand_energy_spent_to_discounted_cost_ratio_total\"", json);
        Assert.Contains("\"mummified_hand_energy_spent_to_discounted_cost_ratio_count\"", json);
        Assert.Contains("\"mummified_hand_combats\"", json);
        Assert.Contains("\"mummified_hand_turns\"", json);
        Assert.Contains("\"mummified_hand_discounted_powers\"", json);
        Assert.Contains("\"mummified_hand_discounted_attacks\"", json);
        Assert.Contains("\"mummified_hand_discounted_skills\"", json);
        Assert.Contains("\"mummified_hand_discounted_commons\"", json);
        Assert.Contains("\"mummified_hand_discounted_uncommons\"", json);
        Assert.Contains("\"mummified_hand_discounted_rares\"", json);
        Assert.NotNull(restored);

        AssertAggregate(restored!.RelicAggregates[MummifiedHandRelicId]);
    }

    [Fact]
    public void RunTracker_MummifiedHandHelpers_AccumulateObservedCostsRatiosAndTypes()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordMummifiedHandTriggerForTest(
            agg,
            2,
            2,
            2m,
            0m,
            CardType.Attack,
            CardRarity.Common);
        RunTracker.RecordMummifiedHandTriggerForTest(
            agg,
            1,
            1,
            4m,
            0m,
            CardType.Skill,
            CardRarity.Uncommon);
        RunTracker.RecordMummifiedHandTriggerForTest(
            agg,
            0,
            0,
            0m,
            0m,
            CardType.Power,
            CardRarity.Rare);
        RunTracker.RecordMummifiedHandTriggerForTest(agg, 3, 3, 0m, 0m, null);
        RunTracker.RecordMummifiedHandCombatForTest(agg, 2);
        RunTracker.RecordMummifiedHandTurnForTest(agg, 5);
        RunTracker.RecordMummifiedHandCombatForTest(agg, -2);
        RunTracker.RecordMummifiedHandTurnForTest(agg, -5);

        AssertAggregate(agg);
    }

    [Fact]
    public void RunTracker_MummifiedHandHelper_ClampsNegativeCosts()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordMummifiedHandTriggerForTest(agg, -2, -3, -4m, -1m, CardType.Attack);

        Assert.Equal(1, agg.Activations);
        Assert.Equal(0m, agg.MummifiedHandTriggeringPowerCostTotal);
        Assert.Equal(0m, agg.MummifiedHandDiscountGivenTotal);
        Assert.Equal(0m, agg.MummifiedHandEnergySpentToDiscountedCostRatioTotal);
        Assert.Equal(0, agg.MummifiedHandEnergySpentToDiscountedCostRatioCount);
        Assert.Equal(1, agg.MummifiedHandDiscountedAttacks);
    }

    [Fact]
    public void RelicAggregate_MummifiedHandFields_Merge()
    {
        var target = PopulatedAggregate();

        RunTracker.MergeRelicAggregateInto(target, PopulatedAggregate());

        Assert.Equal(8, target.Activations);
        Assert.Equal(12m, target.MummifiedHandTriggeringPowerCostTotal);
        Assert.Equal(12m, target.MummifiedHandDiscountGivenTotal);
        Assert.Equal(2.5m, target.MummifiedHandEnergySpentToDiscountedCostRatioTotal);
        Assert.Equal(4, target.MummifiedHandEnergySpentToDiscountedCostRatioCount);
        Assert.Equal(4, target.MummifiedHandCombats);
        Assert.Equal(10, target.MummifiedHandTurns);
        Assert.Equal(2, target.MummifiedHandDiscountedPowers);
        Assert.Equal(2, target.MummifiedHandDiscountedAttacks);
        Assert.Equal(2, target.MummifiedHandDiscountedSkills);
        Assert.Equal(2, target.MummifiedHandDiscountedCommons);
        Assert.Equal(2, target.MummifiedHandDiscountedUncommons);
        Assert.Equal(2, target.MummifiedHandDiscountedRares);
    }

    [Fact]
    public void RelicTooltip_MummifiedHand_ShowsRequestedTotalsAndAverages()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("Times triggered", body);
        Assert.Contains("Avg cost of triggering Power", body);
        Assert.Contains("Avg discount given", body);
        Assert.Contains("Avg ratio: Power energy spent / discounted card cost", body);
        Assert.Contains("Avg activations per combat", body);
        Assert.Contains("Avg activations per turn", body);
        Assert.Contains("Discounted Powers", body);
        Assert.Contains("Discounted Attacks", body);
        Assert.Contains("Discounted Skills", body);
        Assert.Contains("Discounted Commons", body);
        Assert.Contains("Discounted Uncommons", body);
        Assert.Contains("Discounted Rares", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.Contains("[b]1.5[/b]", body);
        Assert.Contains("[b]0.63[/b]", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]0.8[/b]", body);
    }

    [Fact]
    public void RelicTooltip_MummifiedHand_ShowsZeroAveragesWithoutDenominators()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Times triggered", body);
        Assert.Contains("Avg activations per combat", body);
        Assert.Contains("Avg activations per turn", body);
        Assert.Contains("[b]0[/b]", body);
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void RelicTooltip_MummifiedHand_DispatchesForModel()
    {
        var relic = (MummifiedHand)RuntimeHelpers.GetUninitializedObject(typeof(MummifiedHand));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Mummified Hand", title);
        Assert.Contains("Times triggered", body);
    }

    [Fact]
    public void RelicAggregate_OlderShapeWithoutMummifiedHandFields_DefaultsToZero()
    {
        var agg = JsonSerializer.Deserialize<RelicAggregate>("{}", RunStorage.Options);

        Assert.NotNull(agg);
        Assert.Equal(0m, agg!.MummifiedHandTriggeringPowerCostTotal);
        Assert.Equal(0m, agg.MummifiedHandDiscountGivenTotal);
        Assert.Equal(0m, agg.MummifiedHandEnergySpentToDiscountedCostRatioTotal);
        Assert.Equal(0, agg.MummifiedHandEnergySpentToDiscountedCostRatioCount);
        Assert.Equal(0, agg.MummifiedHandCombats);
        Assert.Equal(0, agg.MummifiedHandTurns);
        Assert.Equal(0, agg.MummifiedHandDiscountedPowers);
        Assert.Equal(0, agg.MummifiedHandDiscountedAttacks);
        Assert.Equal(0, agg.MummifiedHandDiscountedSkills);
        Assert.Equal(0, agg.MummifiedHandDiscountedCommons);
        Assert.Equal(0, agg.MummifiedHandDiscountedUncommons);
        Assert.Equal(0, agg.MummifiedHandDiscountedRares);
    }

    private static RelicAggregate PopulatedAggregate()
        => new()
        {
            Activations = 4,
            MummifiedHandTriggeringPowerCostTotal = 6m,
            MummifiedHandDiscountGivenTotal = 6m,
            MummifiedHandEnergySpentToDiscountedCostRatioTotal = 1.25m,
            MummifiedHandEnergySpentToDiscountedCostRatioCount = 2,
            MummifiedHandCombats = 2,
            MummifiedHandTurns = 5,
            MummifiedHandDiscountedPowers = 1,
            MummifiedHandDiscountedAttacks = 1,
            MummifiedHandDiscountedSkills = 1,
            MummifiedHandDiscountedCommons = 1,
            MummifiedHandDiscountedUncommons = 1,
            MummifiedHandDiscountedRares = 1,
        };

    private static void AssertAggregate(RelicAggregate agg)
    {
        Assert.Equal(4, agg.Activations);
        Assert.Equal(6m, agg.MummifiedHandTriggeringPowerCostTotal);
        Assert.Equal(6m, agg.MummifiedHandDiscountGivenTotal);
        Assert.Equal(1.25m, agg.MummifiedHandEnergySpentToDiscountedCostRatioTotal);
        Assert.Equal(2, agg.MummifiedHandEnergySpentToDiscountedCostRatioCount);
        Assert.Equal(2, agg.MummifiedHandCombats);
        Assert.Equal(5, agg.MummifiedHandTurns);
        Assert.Equal(1, agg.MummifiedHandDiscountedPowers);
        Assert.Equal(1, agg.MummifiedHandDiscountedAttacks);
        Assert.Equal(1, agg.MummifiedHandDiscountedSkills);
        Assert.Equal(1, agg.MummifiedHandDiscountedCommons);
        Assert.Equal(1, agg.MummifiedHandDiscountedUncommons);
        Assert.Equal(1, agg.MummifiedHandDiscountedRares);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildMummifiedHandBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildMummifiedHandBodyBBCode returned null."));
}
