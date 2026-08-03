using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class SmallCapsuleStatsTests
{
    private const string SmallCapsuleRelicId = "RELIC.SMALL_CAPSULE";

    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildSmallCapsuleBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "BuildSmallCapsuleBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void Patches_TargetSmallCapsuleRewardLifecycle()
    {
        var pickup = typeof(SmallCapsule).GetMethod(
            nameof(SmallCapsule.AfterObtained),
            Type.EmptyTypes);
        var offerCustom = typeof(RewardsCmd).GetMethod(
            nameof(RewardsCmd.OfferCustom),
            [typeof(Player), typeof(List<Reward>)]);
        var populate = typeof(RelicReward).GetMethod(
            nameof(RelicReward.Populate));
        var onSelect = typeof(RelicReward).GetMethod(
            "OnSelect",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var onSkipped = typeof(RelicReward).GetMethod(
            nameof(RelicReward.OnSkipped));

        Assert.NotNull(pickup);
        Assert.Equal(typeof(Task), pickup!.ReturnType);
        Assert.NotNull(offerCustom);
        Assert.NotNull(populate);
        Assert.NotNull(onSelect);
        Assert.Equal(typeof(Task<bool>), onSelect!.ReturnType);
        Assert.NotNull(onSkipped);
    }

    [Fact]
    public void RelicAggregate_SmallCapsuleChoices_DefaultToEmpty()
    {
        Assert.Empty(new RelicAggregate().RelicRewardChoices);
    }

    [Fact]
    public void RelicAggregate_SmallCapsuleChoices_JsonRoundtripPreservesOutcome()
    {
        var run = new RunData();
        run.RelicAggregates[SmallCapsuleRelicId] = AggregateWithChoices();

        var json = JsonSerializer.Serialize(run, SerializerOptions);
        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.Contains("relic_reward_choices", json);
        Assert.NotNull(restored);
        var choices = restored!.RelicAggregates[SmallCapsuleRelicId]
            .RelicRewardChoices;
        Assert.Equal(2, choices.Count);
        Assert.Equal("RELIC.DATA_DISK", choices[0].RelicId);
        Assert.Equal("taken", choices[0].Outcome);
        Assert.Equal("skipped", choices[1].Outcome);
    }

    [Fact]
    public void RunTracker_SmallCapsuleChoice_UpdatesOneExactReward()
    {
        var aggregate = new RelicAggregate();

        RunTracker.RecordSmallCapsuleRewardChoiceForTest(
            aggregate,
            1,
            null,
            null,
            "pending");
        RunTracker.RecordSmallCapsuleRewardChoiceForTest(
            aggregate,
            1,
            "RELIC.DATA_DISK",
            "Data Disk",
            "pending");
        RunTracker.RecordSmallCapsuleRewardChoiceForTest(
            aggregate,
            1,
            "RELIC.DATA_DISK",
            "Data Disk",
            "taken");
        RunTracker.RecordSmallCapsuleRewardChoiceForTest(
            aggregate,
            1,
            "RELIC.DATA_DISK",
            "Data Disk",
            "pending");

        var choice = Assert.Single(aggregate.RelicRewardChoices);
        Assert.Equal(1, choice.ChoiceNumber);
        Assert.Equal("RELIC.DATA_DISK", choice.RelicId);
        Assert.Equal("Data Disk", choice.DisplayName);
        Assert.Equal("taken", choice.Outcome);
    }

    [Fact]
    public void MergeRelicAggregateInto_SmallCapsuleChoices_AppendsInOrder()
    {
        var target = new RelicAggregate();
        RunTracker.RecordSmallCapsuleRewardChoiceForTest(
            target,
            1,
            "RELIC.DATA_DISK",
            "Data Disk",
            "taken");
        var source = new RelicAggregate();
        RunTracker.RecordSmallCapsuleRewardChoiceForTest(
            source,
            1,
            "RELIC.BAG_OF_PREPARATION",
            "Bag of Preparation",
            "skipped");

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(2, target.RelicRewardChoices.Count);
        Assert.Equal(1, target.RelicRewardChoices[0].ChoiceNumber);
        Assert.Equal(2, target.RelicRewardChoices[1].ChoiceNumber);
        Assert.Equal("skipped", target.RelicRewardChoices[1].Outcome);
    }

    [Fact]
    public void RelicTooltip_SmallCapsule_ShowsHoverableRelicsAndOutcomes()
    {
        var body = BuildBody(AggregateWithChoices());

        Assert.Contains("data_disk.tres", body);
        Assert.Contains("Data Disk", body);
        Assert.Contains("taken", body);
        Assert.Contains("bag_of_preparation.tres", body);
        Assert.Contains("Bag of Preparation", body);
        Assert.Contains("not taken", body);
    }

    [Fact]
    public void RelicTooltip_SmallCapsule_DispatchesForModel()
    {
        var relic = (SmallCapsule)RuntimeHelpers.GetUninitializedObject(
            typeof(SmallCapsule));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate(),
            floorCount: null,
            out var title,
            out _);

        Assert.True(recognized);
        Assert.Equal("Small Capsule", title);
    }

    private static RelicAggregate AggregateWithChoices()
        => new()
        {
            RelicRewardChoices =
            [
                new RelicRewardChoiceAggregate
                {
                    ChoiceNumber = 1,
                    RelicId = "RELIC.DATA_DISK",
                    DisplayName = "Data Disk",
                    Outcome = "taken",
                },
                new RelicRewardChoiceAggregate
                {
                    ChoiceNumber = 2,
                    RelicId = "RELIC.BAG_OF_PREPARATION",
                    DisplayName = "Bag of Preparation",
                    Outcome = "skipped",
                },
            ],
        };

    private static string BuildBody(RelicAggregate aggregate)
        => (string)(BuildBodyMethod.Invoke(null, [aggregate])
            ?? throw new InvalidOperationException(
                "BuildSmallCapsuleBodyBBCode returned null."));
}
