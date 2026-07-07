using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class PaperPhrogStatsTests
{
    private const string PaperPhrogRelicId = "RELIC.PAPER_PHROG";

    private static readonly MethodInfo BuildPaperPhrogBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildPaperPhrogBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildPaperPhrogBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_PaperPhrogFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0m, agg.PaperPhrogDamageAdded);
        Assert.Equal(0, agg.PaperPhrogEnhancedAttacks);
        Assert.Equal(0, agg.PaperPhrogCombats);
        Assert.Equal(0, agg.PaperPhrogTurns);
    }

    [Fact]
    public void RelicAggregate_PaperPhrogFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[PaperPhrogRelicId] = new RelicAggregate
        {
            PaperPhrogDamageAdded = 18.75m,
            PaperPhrogEnhancedAttacks = 6,
            PaperPhrogCombats = 3,
            PaperPhrogTurns = 5,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("paper_phrog_damage_added", json);
        Assert.Contains("paper_phrog_enhanced_attacks", json);
        Assert.Contains("paper_phrog_combats", json);
        Assert.Contains("paper_phrog_turns", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[PaperPhrogRelicId];
        Assert.Equal(18.75m, restoredAgg.PaperPhrogDamageAdded);
        Assert.Equal(6, restoredAgg.PaperPhrogEnhancedAttacks);
        Assert.Equal(3, restoredAgg.PaperPhrogCombats);
        Assert.Equal(5, restoredAgg.PaperPhrogTurns);
    }

    [Fact]
    public void RunTracker_PaperPhrogHelpers_AccumulateAndClamp()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordPaperPhrogVulnerableBonusForTest(agg, 12.5m, enhancedAttacks: 4);
        RunTracker.RecordPaperPhrogCombatForTest(agg, 3);
        RunTracker.RecordPaperPhrogTurnForTest(agg, 5);
        RunTracker.RecordPaperPhrogVulnerableBonusForTest(agg, -2m, enhancedAttacks: -1);
        RunTracker.RecordPaperPhrogCombatForTest(agg, -3);
        RunTracker.RecordPaperPhrogTurnForTest(agg, -5);

        Assert.Equal(12.5m, agg.PaperPhrogDamageAdded);
        Assert.Equal(4, agg.PaperPhrogEnhancedAttacks);
        Assert.Equal(3, agg.PaperPhrogCombats);
        Assert.Equal(5, agg.PaperPhrogTurns);
    }

    [Fact]
    public void MergeRelicAggregateInto_PaperPhrogFields_Accumulates()
    {
        var target = new RelicAggregate
        {
            PaperPhrogDamageAdded = 10.25m,
            PaperPhrogEnhancedAttacks = 3,
            PaperPhrogCombats = 2,
            PaperPhrogTurns = 4,
        };
        var source = new RelicAggregate
        {
            PaperPhrogDamageAdded = 8.5m,
            PaperPhrogEnhancedAttacks = 2,
            PaperPhrogCombats = 1,
            PaperPhrogTurns = 2,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(18.75m, target.PaperPhrogDamageAdded);
        Assert.Equal(5, target.PaperPhrogEnhancedAttacks);
        Assert.Equal(3, target.PaperPhrogCombats);
        Assert.Equal(6, target.PaperPhrogTurns);
    }

    [Fact]
    public void RelicTooltip_PaperPhrog_ShowsTotalsAndAverages()
    {
        var body = BuildBody(new RelicAggregate
        {
            PaperPhrogDamageAdded = 18.75m,
            PaperPhrogEnhancedAttacks = 6,
            PaperPhrogCombats = 3,
            PaperPhrogTurns = 5,
        });

        Assert.Contains("Damage added", body);
        Assert.Contains("Avg damage added per combat", body);
        Assert.Contains("Avg damage added per turn", body);
        Assert.Contains("Vulnerable-enhanced attacks", body);
        Assert.Contains("Avg enhanced attacks per combat", body);
        Assert.Contains("Avg enhanced attacks per turn", body);
        Assert.Contains("[b]18.75[/b]", body);
        Assert.Contains("[b]6.25[/b]", body);
        Assert.Contains("[b]3.75[/b]", body);
        Assert.Contains("[b]6[/b]", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]1.2[/b]", body);
    }

    [Fact]
    public void RelicTooltip_PaperPhrog_ShowsZeroRows()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Damage added", body);
        Assert.Contains("Avg damage added per combat", body);
        Assert.Contains("Avg damage added per turn", body);
        Assert.Contains("Vulnerable-enhanced attacks", body);
        Assert.Contains("Avg enhanced attacks per combat", body);
        Assert.Contains("Avg enhanced attacks per turn", body);
        Assert.Contains("[b]0[/b]", body);
    }

    [Fact]
    public void PaperPhrogFrameTracker_OnlyResolvesInsideVulnerableDamageFrame()
    {
        Assert.False(PaperPhrogDamageFrameTracker.TryResolveVulnerableDamageAmount(null, null, null, out _));

        PaperPhrogDamageFrameTracker.PushDamageCommandFrame(null, null, null);
        try
        {
            Assert.True(PaperPhrogDamageFrameTracker.HasActiveDamageCommandFrame(null, null, null));
        }
        finally
        {
            PaperPhrogDamageFrameTracker.PopDamageCommandFrame();
        }

        Assert.False(PaperPhrogDamageFrameTracker.HasActiveDamageCommandFrame(null, null, null));
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildPaperPhrogBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildPaperPhrogBodyBBCode returned null."));
}
