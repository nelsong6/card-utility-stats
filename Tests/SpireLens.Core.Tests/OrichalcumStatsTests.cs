using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

/// <summary>
/// Tests for Orichalcum relic stat data model, persistence, and schema
/// backwards compatibility. Live RunTracker integration is exercised by the
/// verification phase via live in-run MCP evidence.
/// </summary>
public class OrichalcumStatsTests
{
    private const string OrichalcumRelicId = "RELIC.ORICHALCUM";

    private static readonly MethodInfo BuildOrichalcumBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildOrichalcumBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildOrichalcumBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_AdditionalBlockGained_DefaultsToZero()
    {
        var agg = new RelicAggregate();
        Assert.Equal(0, agg.AdditionalBlockGained);
        Assert.Equal(0, agg.BlockedTriggers);
    }

    [Fact]
    public void RelicAggregate_AdditionalBlockGained_JsonRoundtrip_PreservesField()
    {
        var agg = new RelicAggregate { AdditionalBlockGained = 24, BlockedTriggers = 3 };
        var run = new RunData();
        run.RelicAggregates[OrichalcumRelicId] = agg;

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("relic_aggregates", json);
        Assert.Contains("additional_block_gained", json);
        Assert.Contains("blocked_triggers", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);
        Assert.NotNull(restored);
        Assert.True(restored!.RelicAggregates.ContainsKey(OrichalcumRelicId));
        var restoredAgg = restored.RelicAggregates[OrichalcumRelicId];
        Assert.Equal(24, restoredAgg.AdditionalBlockGained);
        Assert.Equal(3, restoredAgg.BlockedTriggers);
    }

    [Fact]
    public void RelicAggregate_AdditionalBlockGained_AccumulatesAcrossTriggers()
    {
        var run = new RunData();

        if (!run.RelicAggregates.TryGetValue(OrichalcumRelicId, out var agg))
        {
            agg = new RelicAggregate();
            run.RelicAggregates[OrichalcumRelicId] = agg;
        }

        agg.AdditionalBlockGained += 6;
        agg.AdditionalBlockGained += 6;
        agg.AdditionalBlockGained += 6;

        Assert.Equal(18, run.RelicAggregates[OrichalcumRelicId].AdditionalBlockGained);
    }

    [Fact]
    public void RelicTooltip_AdditionalBlockGained_ShowsBlockIconAndTotal()
    {
        var agg = new RelicAggregate { AdditionalBlockGained = 12, BlockedTriggers = 2 };

        var body = (string)(BuildOrichalcumBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildOrichalcumBodyBBCode returned null."));

        Assert.Contains("[hint=\"Block gained:", body);
        Assert.Contains("block gained", body);
        Assert.Contains("[b]12[/b]", body);
        Assert.Contains("Triggers blocked", body);
        Assert.Contains("[b]2[/b]", body);
    }

    [Theory]
    // Leftover below the trigger amount is the only case that costs anything.
    [InlineData(1, 5)]
    [InlineData(3, 3)]
    [InlineData(5, 1)]
    public void UndercutScoring_ChargesTheTriggerAmountMinusLeftoverBlock(
        int leftoverBlock,
        int expectedMissed)
    {
        var agg = new RelicAggregate();

        RunTracker.RecordOrichalcumUndercutTurnForTest(agg, leftoverBlock, 6m);

        Assert.Equal(1, agg.OrichalcumTurnsUndercut);
        Assert.Equal(expectedMissed, agg.OrichalcumBlockMissed);
    }

    [Theory]
    // At or above the trigger amount the player already holds at least what the
    // trigger would have granted, so nothing was given up.
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(40)]
    public void UndercutScoring_IgnoresLeftoverAtOrAboveTheTriggerAmount(int leftoverBlock)
    {
        var agg = new RelicAggregate();

        RunTracker.RecordOrichalcumUndercutTurnForTest(agg, leftoverBlock, 6m);

        Assert.Equal(0, agg.OrichalcumTurnsUndercut);
        Assert.Equal(0, agg.OrichalcumBlockMissed);
    }

    [Fact]
    public void UndercutScoring_IgnoresZeroLeftoverAndUnreadableTriggerAmount()
    {
        var triggered = new RelicAggregate();
        RunTracker.RecordOrichalcumUndercutTurnForTest(triggered, 0, 6m);

        var unreadable = new RelicAggregate();
        RunTracker.RecordOrichalcumUndercutTurnForTest(unreadable, 3, 0m);

        Assert.Equal(0, triggered.OrichalcumTurnsUndercut);
        Assert.Equal(0, triggered.OrichalcumBlockMissed);
        Assert.Equal(0, unreadable.OrichalcumTurnsUndercut);
        Assert.Equal(0, unreadable.OrichalcumBlockMissed);
    }

    [Fact]
    public void UndercutScoring_UsesTheLiveTriggerAmountRatherThanAHardcodedSix()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordOrichalcumUndercutTurnForTest(agg, 4, 10m);

        Assert.Equal(1, agg.OrichalcumTurnsUndercut);
        Assert.Equal(6, agg.OrichalcumBlockMissed);
    }

    [Fact]
    public void Averages_SpreadMissedBlockOverEveryHeldTurnAndCombat()
    {
        var agg = new RelicAggregate
        {
            OrichalcumBlockMissed = 12,
            OrichalcumTurnsUndercut = 3,
            OrichalcumTurns = 16,
            OrichalcumCombats = 4,
        };

        Assert.Equal(
            0.75m,
            RelicHoverShowPatch.CalculateOrichalcumBlockMissedPerTurn(agg));
        Assert.Equal(
            3m,
            RelicHoverShowPatch.CalculateOrichalcumBlockMissedPerCombat(agg));
    }

    [Fact]
    public void Averages_FallBackToUndercutTurnsWhenTheTurnDenominatorIsMissing()
    {
        var agg = new RelicAggregate
        {
            OrichalcumBlockMissed = 9,
            OrichalcumTurnsUndercut = 3,
            OrichalcumTurns = 0,
        };

        Assert.Equal(
            3m,
            RelicHoverShowPatch.CalculateOrichalcumBlockMissedPerTurn(agg));
    }

    [Fact]
    public void Averages_AreZeroWithoutAnyHeldPeriod()
    {
        var agg = new RelicAggregate();

        Assert.Equal(
            0m,
            RelicHoverShowPatch.CalculateOrichalcumBlockMissedPerTurn(agg));
        Assert.Equal(
            0m,
            RelicHoverShowPatch.CalculateOrichalcumBlockMissedPerCombat(agg));
    }

    [Fact]
    public void MergeRelicAggregateInto_AccumulatesBlockMissedFields()
    {
        var target = PopulatedAggregate();

        RunTracker.MergeRelicAggregateInto(target, PopulatedAggregate());

        Assert.Equal(8, target.OrichalcumTurnsUndercut);
        Assert.Equal(22, target.OrichalcumBlockMissed);
        Assert.Equal(28, target.OrichalcumTurns);
        Assert.Equal(6, target.OrichalcumCombats);
    }

    [Fact]
    public void RelicTooltip_ShowsUndercutTurnsMissedBlockAndBothAverages()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("Turns undercut", body);
        Assert.Contains("Block missed", body);
        Assert.Contains("Block missed per turn", body);
        Assert.Contains("Block missed per combat", body);
        Assert.Contains("counterfactual", body);
        // 11 missed over 14 held turns and 3 held combats.
        Assert.Contains("[b]0.79[/b]", body);
        Assert.Contains("[b]3.67[/b]", body);
    }

    [Fact]
    public void RelicAggregate_BlockMissedFields_JsonRoundtripPreservesValues()
    {
        var run = new RunData();
        run.RelicAggregates[OrichalcumRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"orichalcum_turns_undercut\"", json);
        Assert.Contains("\"orichalcum_block_missed\"", json);
        Assert.Contains("\"orichalcum_turns\"", json);
        Assert.Contains("\"orichalcum_combats\"", json);
        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[OrichalcumRelicId];
        Assert.Equal(4, restoredAgg.OrichalcumTurnsUndercut);
        Assert.Equal(11, restoredAgg.OrichalcumBlockMissed);
        Assert.Equal(14, restoredAgg.OrichalcumTurns);
        Assert.Equal(3, restoredAgg.OrichalcumCombats);
    }

    [Fact]
    public void RelicAggregate_BlockMissedFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.OrichalcumTurnsUndercut);
        Assert.Equal(0, agg.OrichalcumBlockMissed);
        Assert.Equal(0, agg.OrichalcumTurns);
        Assert.Equal(0, agg.OrichalcumCombats);
    }

    private static RelicAggregate PopulatedAggregate()
        => new()
        {
            AdditionalBlockGained = 18,
            BlockedTriggers = 5,
            OrichalcumTurnsUndercut = 4,
            OrichalcumBlockMissed = 11,
            OrichalcumTurns = 14,
            OrichalcumCombats = 3,
        };

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildOrichalcumBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildOrichalcumBodyBBCode returned null."));

    [Fact]
    public void RunData_OlderShapeWithoutAdditionalBlockGained_DeserializesWithZeroDefault()
    {
        const string json = """
            {
              "run_id": "test",
              "started_at": "2026-01-01T00:00:00Z",
              "updated_at": "2026-01-01T00:00:00Z",
              "outcome": "in_progress",
              "aggregates": {},
              "events": [],
              "instance_numbers_by_def": {},
              "def_counters": {},
              "relic_aggregates": {
                "RELIC.ORICHALCUM": {
                  "enemies_affected": 0,
                  "vulnerable_applied": 0,
                  "weak_applied": 0
                }
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        Assert.True(run!.RelicAggregates.ContainsKey(OrichalcumRelicId));
        var agg = run.RelicAggregates[OrichalcumRelicId];
        Assert.Equal(0, agg.AdditionalBlockGained);
        Assert.Equal(0, agg.BlockedTriggers);
    }
}
