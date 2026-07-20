using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class EggRelicStatsTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void EggRelicPatch_TargetsRelicAttributedCardCreationModification()
    {
        var targetMethod = typeof(EggRelicCardOfferStatsPatch).GetMethod(
            "TargetMethod",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("TargetMethod not found.");
        var target = targetMethod.Invoke(null, null) as MethodBase;

        Assert.NotNull(target);
        Assert.Equal(nameof(CardCreationResult.ModifyCard), target!.Name);
        Assert.Equal(
            new[] { typeof(CardModel), typeof(RelicModel) },
            target.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void RelicAggregate_EggOfferField_JsonRoundtripPreservesCount()
    {
        var run = new RunData();
        run.RelicAggregates["RELIC.MOLTEN_EGG"] = new RelicAggregate
        {
            UpgradedCardsOffered = 7,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);
        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        Assert.Equal(7, restored!.RelicAggregates["RELIC.MOLTEN_EGG"].UpgradedCardsOffered);
    }

    [Fact]
    public void RunTracker_EggOfferTestHelper_AccumulatesOnlyPositiveCounts()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordEggUpgradedCardOfferedForTest(agg, 2);
        RunTracker.RecordEggUpgradedCardOfferedForTest(agg, -1);
        RunTracker.RecordEggUpgradedCardOfferedForTest(agg);

        Assert.Equal(3, agg.UpgradedCardsOffered);
    }

    [Fact]
    public void RelicTooltip_MoltenEgg_ShowsUpgradedAttacksOffered()
    {
        AssertEggTooltip<MoltenEgg>("Molten Egg", "Upgraded attacks offered", 4);
    }

    [Fact]
    public void RelicTooltip_ToxicEgg_ShowsUpgradedSkillsOffered()
    {
        AssertEggTooltip<ToxicEgg>("Toxic Egg", "Upgraded skills offered", 5);
    }

    [Fact]
    public void RelicTooltip_FrozenEgg_ShowsUpgradedPowersOffered()
    {
        AssertEggTooltip<FrozenEgg>("Frozen Egg", "Upgraded powers offered", 6);
    }

    private static void AssertEggTooltip<TEgg>(string expectedTitle, string expectedLabel, int count)
        where TEgg : RelicModel
    {
        var relic = (TEgg)RuntimeHelpers.GetUninitializedObject(typeof(TEgg));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate { UpgradedCardsOffered = count },
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal(expectedTitle, title);
        Assert.Contains(expectedLabel, body);
        Assert.Contains($"[b]{count}[/b]", body);
    }
}
