using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class WarHammerStatsTests
{
    private const string WarHammerRelicId = "RELIC.WAR_HAMMER";

    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildWarHammerBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildWarHammerBodyBBCode not found.");

    private static readonly MethodInfo TargetMethod =
        typeof(WarHammerAfterCombatVictoryStatsPatch).GetMethod(
            "TargetMethod",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("War Hammer TargetMethod not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void Patch_TargetsWarHammerAfterCombatVictory()
    {
        var target = TargetMethod.Invoke(null, null) as MethodBase;

        Assert.NotNull(target);
        Assert.Equal(typeof(WarHammer), target!.DeclaringType);
        Assert.Equal(nameof(WarHammer.AfterCombatVictory), target.Name);
        Assert.Equal(
            new[] { typeof(CombatRoom) },
            target.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void RelicAggregate_WarHammerFields_JsonRoundtripPreservesValues()
    {
        var run = new RunData();
        run.RelicAggregates[WarHammerRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, SerializerOptions);
        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.Contains("war_hammer_upgraded_card_instance_ids", json);
        Assert.Contains("war_hammer_upgraded_card_plays", json);
        Assert.Contains("war_hammer_combats", json);
        Assert.Contains("war_hammer_turns", json);
        Assert.NotNull(restored);
        AssertWarHammerAggregate(restored!.RelicAggregates[WarHammerRelicId]);
    }

    [Fact]
    public void RelicAggregate_OldShape_DefaultsWarHammerFieldsSafely()
    {
        var restored = JsonSerializer.Deserialize<RelicAggregate>("{}", SerializerOptions);

        Assert.NotNull(restored);
        Assert.Empty(restored!.WarHammerUpgradedCardInstanceIds);
        Assert.Equal(0, restored.WarHammerUpgradedCardPlays);
        Assert.Equal(0, restored.WarHammerCombats);
        Assert.Equal(0, restored.WarHammerTurns);
    }

    [Fact]
    public void RunTracker_WarHammerHelpers_RecordAndDeduplicateStableCardIds()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordWarHammerActivationForTest(
            agg,
            new[] { "Grave Warden+", "Reap+" },
            new[] { "CARD.GRAVE_WARDEN#1", "CARD.REAP#1" });
        RunTracker.RecordWarHammerActivationForTest(
            agg,
            new[] { "Defy+", "Bash+" },
            new[] { "CARD.REAP#1", "CARD.DEFY#1", "CARD.BASH#1" });
        RunTracker.RecordWarHammerCombatForTest(agg, 3);
        RunTracker.RecordWarHammerTurnForTest(agg, 6);
        RunTracker.RecordWarHammerUpgradedCardPlayForTest(agg, 12);

        AssertWarHammerAggregate(agg);
    }

    [Fact]
    public void MergeRelicAggregateInto_WarHammerFields_AccumulatesAndUnionsCardIds()
    {
        var target = new RelicAggregate
        {
            Activations = 1,
            CardsUpgraded = 2,
            UpgradedCards = { "Grave Warden+", "Reap+" },
            WarHammerUpgradedCardInstanceIds =
            {
                "CARD.GRAVE_WARDEN#1",
                "CARD.REAP#1",
            },
            WarHammerUpgradedCardPlays = 5,
            WarHammerCombats = 1,
            WarHammerTurns = 2,
        };
        var source = new RelicAggregate
        {
            Activations = 1,
            CardsUpgraded = 2,
            UpgradedCards = { "Defy+", "Bash+" },
            WarHammerUpgradedCardInstanceIds =
            {
                "CARD.REAP#1",
                "CARD.DEFY#1",
                "CARD.BASH#1",
            },
            WarHammerUpgradedCardPlays = 7,
            WarHammerCombats = 2,
            WarHammerTurns = 4,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        AssertWarHammerAggregate(target);
    }

    [Fact]
    public void RelicTooltip_WarHammer_ShowsAgreedRowsAndEveryUpgradedCard()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("Activations", body);
        Assert.Contains("Cards upgraded", body);
        Assert.Contains("Avg cards upgraded/activation", body);
        Assert.Contains("Upgraded-card plays", body);
        Assert.Contains("Avg upgraded plays/turn", body);
        Assert.Contains("Avg upgraded plays/combat", body);
        Assert.Contains("Grave Warden+", body);
        Assert.Contains("Reap+", body);
        Assert.Contains("Defy+", body);
        Assert.Contains("Bash+", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.DoesNotContain("Avg cards upgraded/turn", body);
        Assert.DoesNotContain("Avg cards upgraded/combat", body);
    }

    [Fact]
    public void RelicTooltip_WarHammer_DispatchesForWarHammerModel()
    {
        var relic = (WarHammer)RuntimeHelpers.GetUninitializedObject(typeof(WarHammer));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            PopulatedAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("War Hammer", title);
        Assert.Contains("Upgraded-card plays", body);
    }

    private static RelicAggregate PopulatedAggregate() => new()
    {
        Activations = 2,
        CardsUpgraded = 4,
        UpgradedCards = { "Grave Warden+", "Reap+", "Defy+", "Bash+" },
        WarHammerUpgradedCardInstanceIds =
        {
            "CARD.GRAVE_WARDEN#1",
            "CARD.REAP#1",
            "CARD.DEFY#1",
            "CARD.BASH#1",
        },
        WarHammerUpgradedCardPlays = 12,
        WarHammerCombats = 3,
        WarHammerTurns = 6,
    };

    private static void AssertWarHammerAggregate(RelicAggregate agg)
    {
        Assert.Equal(2, agg.Activations);
        Assert.Equal(4, agg.CardsUpgraded);
        Assert.Equal(
            new[] { "Grave Warden+", "Reap+", "Defy+", "Bash+" },
            agg.UpgradedCards);
        Assert.Equal(
            new[]
            {
                "CARD.GRAVE_WARDEN#1",
                "CARD.REAP#1",
                "CARD.DEFY#1",
                "CARD.BASH#1",
            },
            agg.WarHammerUpgradedCardInstanceIds);
        Assert.Equal(12, agg.WarHammerUpgradedCardPlays);
        Assert.Equal(3, agg.WarHammerCombats);
        Assert.Equal(6, agg.WarHammerTurns);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildWarHammerBodyBBCode returned null."));
}
