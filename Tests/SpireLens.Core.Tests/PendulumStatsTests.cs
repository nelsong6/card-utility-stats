using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class PendulumStatsTests
{
    private const string PendulumRelicId = "RELIC.PENDULUM";

    private static readonly MethodInfo BuildPendulumBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildPendulumBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildPendulumBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_PendulumCombatEndFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.PendulumCombatsEndedOn0Charges);
        Assert.Equal(0, agg.PendulumCombatsEndedOn1Charge);
        Assert.Equal(0, agg.PendulumCombatsEndedOn2Charges);
        Assert.Equal(0, agg.PendulumCombatEndChargeTotal);
        Assert.Equal(0, agg.PendulumCombatEndChargeCount);
    }

    [Fact]
    public void RelicAggregate_PendulumCombatEndFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[PendulumRelicId] = new RelicAggregate
        {
            PendulumCombatsEndedOn0Charges = 1,
            PendulumCombatsEndedOn1Charge = 1,
            PendulumCombatsEndedOn2Charges = 2,
            PendulumCombatEndChargeTotal = 5,
            PendulumCombatEndChargeCount = 4,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("pendulum_combats_ended_on0_charges", json);
        Assert.Contains("pendulum_combats_ended_on1_charge", json);
        Assert.Contains("pendulum_combats_ended_on2_charges", json);
        Assert.Contains("pendulum_combat_end_charge_total", json);
        Assert.Contains("pendulum_combat_end_charge_count", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[PendulumRelicId];
        Assert.Equal(1, agg.PendulumCombatsEndedOn0Charges);
        Assert.Equal(1, agg.PendulumCombatsEndedOn1Charge);
        Assert.Equal(2, agg.PendulumCombatsEndedOn2Charges);
        Assert.Equal(5, agg.PendulumCombatEndChargeTotal);
        Assert.Equal(4, agg.PendulumCombatEndChargeCount);
    }

    [Fact]
    public void RunTracker_RecordPendulumCombatEndChargeForTest_TracksValidCharges()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordPendulumCombatEndChargeForTest(agg, 0);
        RunTracker.RecordPendulumCombatEndChargeForTest(agg, 1);
        RunTracker.RecordPendulumCombatEndChargeForTest(agg, 2);
        RunTracker.RecordPendulumCombatEndChargeForTest(agg, 2);
        RunTracker.RecordPendulumCombatEndChargeForTest(agg, -1);
        RunTracker.RecordPendulumCombatEndChargeForTest(agg, 3);

        Assert.Equal(1, agg.PendulumCombatsEndedOn0Charges);
        Assert.Equal(1, agg.PendulumCombatsEndedOn1Charge);
        Assert.Equal(2, agg.PendulumCombatsEndedOn2Charges);
        Assert.Equal(5, agg.PendulumCombatEndChargeTotal);
        Assert.Equal(4, agg.PendulumCombatEndChargeCount);
    }

    [Fact]
    public void MergeRelicAggregateInto_PendulumCombatEndFields_Accumulate()
    {
        var target = new RelicAggregate
        {
            PendulumCombatsEndedOn0Charges = 1,
            PendulumCombatsEndedOn1Charge = 2,
            PendulumCombatEndChargeTotal = 2,
            PendulumCombatEndChargeCount = 3,
        };
        var source = new RelicAggregate
        {
            PendulumCombatsEndedOn1Charge = 1,
            PendulumCombatsEndedOn2Charges = 2,
            PendulumCombatEndChargeTotal = 5,
            PendulumCombatEndChargeCount = 3,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(1, target.PendulumCombatsEndedOn0Charges);
        Assert.Equal(3, target.PendulumCombatsEndedOn1Charge);
        Assert.Equal(2, target.PendulumCombatsEndedOn2Charges);
        Assert.Equal(7, target.PendulumCombatEndChargeTotal);
        Assert.Equal(6, target.PendulumCombatEndChargeCount);
    }

    [Fact]
    public void RelicTooltip_Pendulum_ShowsCombatEndChargesAndAverage()
    {
        var body = BuildBody(new RelicAggregate
        {
            PendulumCombatsEndedOn0Charges = 1,
            PendulumCombatsEndedOn1Charge = 1,
            PendulumCombatsEndedOn2Charges = 2,
            PendulumCombatEndChargeTotal = 5,
            PendulumCombatEndChargeCount = 4,
        });

        Assert.Contains("Combats ended on 0 charges", body);
        Assert.Contains("Combats ended on 1 charge", body);
        Assert.Contains("Combats ended on 2 charges", body);
        Assert.Contains("Avg charge at combat end", body);
        Assert.Contains("[b]1.25[/b]", body);
    }

    [Fact]
    public void RelicTooltip_Pendulum_ReconstructsAverageForBucketOnlyRuns()
    {
        var body = BuildBody(new RelicAggregate
        {
            PendulumCombatsEndedOn0Charges = 1,
            PendulumCombatsEndedOn1Charge = 1,
            PendulumCombatsEndedOn2Charges = 2,
        });

        Assert.Contains("Avg charge at combat end", body);
        Assert.Contains("[b]1.25[/b]", body);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildPendulumBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildPendulumBodyBBCode returned null."));
}
