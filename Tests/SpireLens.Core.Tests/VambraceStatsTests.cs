using System;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class VambraceStatsTests
{
    private const string VambraceRelicId = "RELIC.VAMBRACE";

    private static readonly MethodInfo BuildVambraceBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildVambraceBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildVambraceBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_VambraceFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0, agg.AdditionalBlockGained);
    }

    [Fact]
    public void RelicAggregate_VambraceFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[VambraceRelicId] = new RelicAggregate
        {
            Activations = 2,
            AdditionalBlockGained = 13,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("additional_block_gained", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[VambraceRelicId];
        Assert.Equal(2, restoredAgg.Activations);
        Assert.Equal(13, restoredAgg.AdditionalBlockGained);
    }

    [Theory]
    [InlineData("8", 4)]
    [InlineData("7.5", 4)]
    [InlineData("1.5", 1)]
    [InlineData("0.5", 0)]
    [InlineData("0", 0)]
    public void RunTracker_VambraceExtraBlock_UsesIntegerMarginalBlock(string modifiedAmountText, int expected)
    {
        var modifiedAmount = decimal.Parse(modifiedAmountText, CultureInfo.InvariantCulture);

        var extraBlock = RunTracker.ComputeVambraceExtraBlockForTest(modifiedAmount);

        Assert.Equal(expected, extraBlock);
    }

    [Fact]
    public void RunTracker_VambraceExtraBlock_AccumulatesAndClamps()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordVambraceExtraBlockGainedForTest(agg, 7.5m);
        RunTracker.RecordVambraceExtraBlockGainedForTest(agg, 4m);
        RunTracker.RecordVambraceExtraBlockGainedForTest(agg, -3m);

        Assert.Equal(6, agg.AdditionalBlockGained);
    }

    [Fact]
    public void RelicTooltip_Vambrace_ShowsBlockRows()
    {
        var body = BuildBody(new RelicAggregate
        {
            Activations = 2,
            AdditionalBlockGained = 13,
        });

        Assert.Contains("Activations", body);
        Assert.Contains("Used this combat", body);
        Assert.Contains("[b]false[/b]", body);
        Assert.Contains("[img=16x16]res://images/ui/combat/block.png[/img] extra block gained", body);
        Assert.Contains("[img=16x16]res://images/ui/combat/block.png[/img] extra block per activation", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]13[/b]", body);
        Assert.Contains("[b]6.5[/b]", body);
    }

    [Fact]
    public void RelicTooltip_Vambrace_ShowsZeroValues()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Activations", body);
        Assert.Contains("Used this combat", body);
        Assert.Contains("[b]false[/b]", body);
        Assert.Contains("extra block gained", body);
        Assert.Contains("extra block per activation", body);
        Assert.Contains("[b]0[/b]", body);
    }

    [Fact]
    public void RelicTooltip_Vambrace_CanShowUsedThisCombatTrue()
    {
        var body = BuildBody(new RelicAggregate(), usedThisCombat: true);

        Assert.Contains("Used this combat", body);
        Assert.Contains("[b]true[/b]", body);
    }

    private static string BuildBody(RelicAggregate agg, bool usedThisCombat = false)
        => (string)(BuildVambraceBodyMethod.Invoke(null, new object?[] { agg, usedThisCombat })
            ?? throw new InvalidOperationException("BuildVambraceBodyBBCode returned null."));
}
