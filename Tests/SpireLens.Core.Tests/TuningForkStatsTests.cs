using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class TuningForkStatsTests
{
    private const string TuningForkRelicId = "RELIC.TUNING_FORK";

    private static readonly MethodInfo BuildTuningForkBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildTuningForkBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildTuningForkBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_TuningForkFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0, agg.AdditionalBlockGained);
    }

    [Fact]
    public void RelicAggregate_TuningForkFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[TuningForkRelicId] = new RelicAggregate
        {
            Activations = 3,
            AdditionalBlockGained = 18,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("additional_block_gained", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[TuningForkRelicId];
        Assert.Equal(3, agg.Activations);
        Assert.Equal(18, agg.AdditionalBlockGained);
    }

    [Fact]
    public void RelicTooltip_TuningFork_ShowsActivationsAndBlockGained()
    {
        var body = BuildBody(new RelicAggregate
        {
            Activations = 3,
            AdditionalBlockGained = 18,
        });

        Assert.Contains("Activations", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[img=16x16]res://images/ui/combat/block.png[/img] block gained", body);
        Assert.Contains("[b]18[/b]", body);
    }

    [Fact]
    public void RelicTooltip_TuningFork_ShowsZeroRows()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Activations", body);
        Assert.Contains("block gained", body);
        Assert.Contains("[b]0[/b]", body);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildTuningForkBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildTuningForkBodyBBCode returned null."));
}
