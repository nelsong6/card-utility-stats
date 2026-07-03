using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class PermafrostStatsTests
{
    private const string PermafrostRelicId = "RELIC.PERMAFROST";

    private static readonly MethodInfo BuildPermafrostBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildPermafrostBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildPermafrostBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_PermafrostBlockStats_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[PermafrostRelicId] = new RelicAggregate
        {
            Activations = 3,
            AdditionalBlockGained = 21,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("relic_aggregates", json);
        Assert.Contains("activations", json);
        Assert.Contains("additional_block_gained", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[PermafrostRelicId];
        Assert.Equal(3, agg.Activations);
        Assert.Equal(21, agg.AdditionalBlockGained);
    }

    [Fact]
    public void RelicTooltip_PermafrostFields_ShowBlockRows()
    {
        var body = BuildBody(new RelicAggregate
        {
            Activations = 2,
            AdditionalBlockGained = 15,
        });

        Assert.Contains("Combats triggered", body);
        Assert.Contains("[img=16x16]res://images/ui/combat/block.png[/img] block gained", body);
        Assert.Contains("[img=16x16]res://images/ui/combat/block.png[/img] block gained per combat", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]15[/b]", body);
        Assert.Contains("[b]7.5[/b]", body);
    }

    [Fact]
    public void RelicTooltip_PermafrostFields_ShowZeroPerCombatWhenNeverTriggered()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Combats triggered", body);
        Assert.Contains("[b]0[/b]", body);
        Assert.Contains("block gained per combat", body);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildPermafrostBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildPermafrostBodyBBCode returned null."));
}
