using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class UnsettlingLampStatsTests
{
    private const string UnsettlingLampRelicId = "RELIC.UNSETTLING_LAMP";

    private static readonly MethodInfo BuildUnsettlingLampBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildUnsettlingLampBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildUnsettlingLampBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_UnsettlingLampFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0, agg.VulnerableApplied);
        Assert.Equal(0, agg.WeakApplied);
        Assert.Empty(agg.AppliedEffects);
    }

    [Fact]
    public void RelicAggregate_UnsettlingLampFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[UnsettlingLampRelicId] = BuildTrackedAggregate();

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("vulnerable_applied", json);
        Assert.Contains("weak_applied", json);
        Assert.Contains("applied_effects", json);
        Assert.Contains("POWER.POISON", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[UnsettlingLampRelicId];
        Assert.Equal(3, restoredAgg.Activations);
        Assert.Equal(4, restoredAgg.VulnerableApplied);
        Assert.Equal(2, restoredAgg.WeakApplied);
        var poison = restoredAgg.AppliedEffects["POWER.POISON"];
        Assert.Equal("Poison", poison.DisplayName);
        Assert.Equal(2, poison.TimesApplied);
        Assert.Equal(6m, poison.TotalAmountApplied);
    }

    [Fact]
    public void RunTracker_UnsettlingLampHelpers_RecordFixedAndDynamicDebuffs()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordUnsettlingLampCombatForTest(agg, 3);
        RunTracker.RecordUnsettlingLampDebuffForTest(agg, "POWER.VULNERABLE", "Vulnerable", 4m);
        RunTracker.RecordUnsettlingLampDebuffForTest(agg, "POWER.WEAK", "Weak", 2m);
        RunTracker.RecordUnsettlingLampDebuffForTest(agg, "POWER.POISON", "Poison", 6m);
        RunTracker.RecordUnsettlingLampDebuffForTest(agg, "POWER.POISON", "Poison", -1m);

        Assert.Equal(3, agg.Activations);
        Assert.Equal(4, agg.VulnerableApplied);
        Assert.Equal(2, agg.WeakApplied);
        Assert.Equal(6m, agg.AppliedEffects["POWER.POISON"].TotalAmountApplied);
    }

    [Fact]
    public void RelicTooltip_UnsettlingLamp_ShowsZeroRows()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Combats held", body);
        Assert.Contains("vulnerable applied", body);
        Assert.Contains("avg vulnerable/combat", body);
        Assert.Contains("weak applied", body);
        Assert.Contains("avg weak/combat", body);
        Assert.Contains("[b]0[/b]", body);
        Assert.DoesNotContain("Poison applied", body);
    }

    [Fact]
    public void RelicTooltip_UnsettlingLamp_ShowsFixedAndDynamicAverages()
    {
        var body = BuildBody(BuildTrackedAggregate());

        Assert.Contains("Combats held", body);
        Assert.Contains("vulnerable applied", body);
        Assert.Contains("avg vulnerable/combat", body);
        Assert.Contains("weak applied", body);
        Assert.Contains("avg weak/combat", body);
        Assert.Contains("Poison applied", body);
        Assert.Contains("Poison avg/combat", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]1.33[/b]", body);
    }

    private static RelicAggregate BuildTrackedAggregate()
    {
        var agg = new RelicAggregate
        {
            Activations = 3,
            VulnerableApplied = 4,
            WeakApplied = 2,
        };
        agg.AppliedEffects["POWER.POISON"] = new AppliedEffectAggregate
        {
            EffectId = "POWER.POISON",
            DisplayName = "Poison",
            TimesApplied = 2,
            TotalAmountApplied = 6m,
        };
        return agg;
    }

    private static string BuildBody(RelicAggregate agg)
    {
        return (string)(BuildUnsettlingLampBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildUnsettlingLampBodyBBCode returned null."));
    }
}
