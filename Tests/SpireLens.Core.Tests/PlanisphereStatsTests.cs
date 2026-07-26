using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class PlanisphereStatsTests
{
    private const string PlanisphereRelicId = "RELIC.PLANISPHERE";

    private static readonly MethodInfo BuildPlanisphereBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildPlanisphereBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildPlanisphereBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_PlanisphereFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.TotalHealingAttempted);
        Assert.Equal(0m, agg.TotalHealingRestored);
        Assert.Equal(0m, agg.TotalHealingLost);
        Assert.Empty(agg.HealingLostReasons);
    }

    [Fact]
    public void RelicAggregate_PlanisphereFields_JsonRoundtrip_PreserveFields()
    {
        var agg = new RelicAggregate
        {
            Activations = 3,
            TotalHealingAttempted = 15m,
            TotalHealingRestored = 11m,
            TotalHealingLost = 4m,
        };
        agg.HealingLostReasons["full_hp"] = new HealingLostReasonAggregate
        {
            ReasonId = "full_hp",
            DisplayName = "full HP",
            Amount = 4m,
        };
        var run = new RunData();
        run.RelicAggregates[PlanisphereRelicId] = agg;

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("total_healing_attempted", json);
        Assert.Contains("total_healing_restored", json);
        Assert.Contains("total_healing_lost", json);
        Assert.Contains("healing_lost_reasons", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        Assert.True(restored!.RelicAggregates.ContainsKey(PlanisphereRelicId));
        var restoredAgg = restored.RelicAggregates[PlanisphereRelicId];
        Assert.Equal(3, restoredAgg.Activations);
        Assert.Equal(15m, restoredAgg.TotalHealingAttempted);
        Assert.Equal(11m, restoredAgg.TotalHealingRestored);
        Assert.Equal(4m, restoredAgg.TotalHealingLost);
        Assert.Equal(4m, restoredAgg.HealingLostReasons["full_hp"].Amount);
    }

    [Fact]
    public void RelicTooltip_PlanisphereFields_ShowQuestionFloorsAndHealing()
    {
        var agg = new RelicAggregate
        {
            Activations = 3,
            TotalHealingRestored = 11m,
            TotalHealingLost = 4m,
        };
        agg.HealingLostReasons["full_hp"] = new HealingLostReasonAggregate
        {
            ReasonId = "full_hp",
            DisplayName = "full HP",
            Amount = 4m,
        };

        var body = BuildBody(agg);

        Assert.Contains("? floors gained", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("HP healed", body);
        Assert.Contains("[b]11[/b]", body);
        Assert.Contains("healing lost", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.DoesNotContain("lost to full HP", body);
    }

    [Fact]
    public void RelicTooltip_PlanisphereFields_ShowZeroHealingRows()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("? floors gained", body);
        Assert.Contains("HP healed", body);
        Assert.Contains("healing lost", body);
        Assert.Contains("[b]0[/b]", body);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildPlanisphereBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildPlanisphereBodyBBCode returned null."));
}
