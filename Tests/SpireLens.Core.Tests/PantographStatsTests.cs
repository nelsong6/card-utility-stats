using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class PantographStatsTests
{
    private const string PantographRelicId = "RELIC.PANTOGRAPH";

    private static readonly MethodInfo BuildPantographBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildPantographBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildPantographBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_PantographFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.TotalHealingAttempted);
        Assert.Equal(0m, agg.TotalHealingRestored);
        Assert.Equal(0m, agg.TotalHealingLost);
        Assert.Empty(agg.HealingLostReasons);
    }

    [Fact]
    public void RelicAggregate_PantographFields_JsonRoundtrip_PreservesFields()
    {
        var agg = new RelicAggregate
        {
            Activations = 2,
            TotalHealingAttempted = 50m,
            TotalHealingRestored = 31m,
            TotalHealingLost = 19m,
        };
        agg.HealingLostReasons["full_hp"] = new HealingLostReasonAggregate
        {
            ReasonId = "full_hp",
            DisplayName = "full HP",
            Amount = 19m,
        };
        var run = new RunData();
        run.RelicAggregates[PantographRelicId] = agg;

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains(PantographRelicId, json);
        Assert.Contains("total_healing_restored", json);
        Assert.Contains("total_healing_lost", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[PantographRelicId];
        Assert.Equal(2, restoredAgg.Activations);
        Assert.Equal(50m, restoredAgg.TotalHealingAttempted);
        Assert.Equal(31m, restoredAgg.TotalHealingRestored);
        Assert.Equal(19m, restoredAgg.TotalHealingLost);
        Assert.Equal(19m, restoredAgg.HealingLostReasons["full_hp"].Amount);
    }

    [Fact]
    public void RelicTooltip_PantographFields_ShowZeroHealingRows()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Activations", body);
        Assert.Contains("HP healed", body);
        Assert.Contains("healing wasted", body);
        Assert.Contains("[b]0[/b]", body);
    }

    [Fact]
    public void RelicTooltip_PantographFields_OmitsRedundantWastedReasonRow()
    {
        var agg = new RelicAggregate
        {
            Activations = 1,
            TotalHealingRestored = 7m,
            TotalHealingLost = 18m,
        };
        agg.HealingLostReasons["full_hp"] = new HealingLostReasonAggregate
        {
            ReasonId = "full_hp",
            DisplayName = "full HP",
            Amount = 18m,
        };

        var body = BuildBody(agg);

        Assert.Contains("HP healed", body);
        Assert.Contains("healing wasted", body);
        Assert.DoesNotContain("wasted to full HP", body);
        Assert.Contains("[b]7[/b]", body);
        Assert.Contains("[b]18[/b]", body);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildPantographBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildPantographBodyBBCode returned null."));
}
