using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class OpenBranchRelicStatsTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_OpenBranchFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.VigorGained);
        Assert.Equal(0, agg.TotalDamageAttempted);
        Assert.Equal(0, agg.TotalTargets);
    }

    [Fact]
    public void RelicAggregate_OpenBranchFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates["RELIC.AKABEKO"] = new RelicAggregate { VigorGained = 16 };
        run.RelicAggregates["RELIC.LETTER_OPENER"] = new RelicAggregate
        {
            Activations = 3,
            TotalDamageAttempted = 45,
            TotalTargets = 9,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("vigor_gained", json);
        Assert.Contains("total_damage_attempted", json);
        Assert.Contains("total_targets", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        Assert.Equal(16, restored!.RelicAggregates["RELIC.AKABEKO"].VigorGained);
        Assert.Equal(3, restored.RelicAggregates["RELIC.LETTER_OPENER"].Activations);
        Assert.Equal(45, restored.RelicAggregates["RELIC.LETTER_OPENER"].TotalDamageAttempted);
        Assert.Equal(9, restored.RelicAggregates["RELIC.LETTER_OPENER"].TotalTargets);
    }

    [Fact]
    public void RelicTooltip_OpenBranchRelics_ShowExpectedRows()
    {
        var anchorBody = InvokeTooltipBuilder(
            "BuildAnchorBodyBBCode",
            new RelicAggregate { Activations = 2, AdditionalBlockGained = 20 });
        Assert.Contains("Activations", anchorBody);
        Assert.Contains("block gained", anchorBody);
        Assert.Contains("[b]20[/b]", anchorBody);

        var letterOpenerBody = InvokeTooltipBuilder(
            "BuildLetterOpenerBodyBBCode",
            new RelicAggregate { Activations = 3, TotalDamageAttempted = 45, TotalTargets = 9 });
        Assert.Contains("Damage attempted", letterOpenerBody);
        Assert.Contains("Targets hit", letterOpenerBody);
        Assert.Contains("[b]45[/b]", letterOpenerBody);

        var akabekoBody = InvokeTooltipBuilder(
            "BuildAkabekoBodyBBCode",
            new RelicAggregate { VigorGained = 16 });
        Assert.Contains("vigor gained", akabekoBody);
        Assert.Contains("[b]16[/b]", akabekoBody);

        var boomingConchBody = InvokeTooltipBuilder(
            "BuildBoomingConchBodyBBCode",
            new RelicAggregate { EnergyGenerated = 2, AdditionalCardsDrawn = 4 });
        Assert.Contains("Energy generated", boomingConchBody);
        Assert.Contains("Cards drawn", boomingConchBody);
        Assert.Contains("[b]4[/b]", boomingConchBody);

        var bloodVialBody = InvokeTooltipBuilder(
            "BuildBloodVialBodyBBCode",
            new RelicAggregate { Activations = 2, TotalHealingRestored = 3, TotalHealingLost = 1 });
        Assert.Contains("HP healed", bloodVialBody);
        Assert.Contains("healing lost", bloodVialBody);
    }

    private static string InvokeTooltipBuilder(string methodName, RelicAggregate agg)
    {
        var method = typeof(RelicHoverShowPatch).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (string)(method!.Invoke(null, new object[] { agg })
            ?? throw new InvalidOperationException($"{methodName} returned null."));
    }
}
