using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class MrStrugglesStatsTests
{
    private const string MrStrugglesRelicId = "RELIC.MR_STRUGGLES";

    private static readonly MethodInfo BuildMrStrugglesBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildMrStrugglesBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildMrStrugglesBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_MrStrugglesDamageStats_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[MrStrugglesRelicId] = new RelicAggregate
        {
            Activations = 3,
            TotalDamageAttempted = 40,
            TotalDamageDealt = 31,
            TotalDamageBlocked = 5,
            TotalDamageOverkill = 4,
            TotalTargets = 6,
            Kills = 2,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains(MrStrugglesRelicId, json);
        Assert.Contains("total_damage_attempted", json);
        Assert.Contains("total_damage_dealt", json);
        Assert.Contains("total_damage_blocked", json);
        Assert.Contains("total_damage_overkill", json);
        Assert.Contains("total_targets", json);
        Assert.Contains("kills", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[MrStrugglesRelicId];
        Assert.Equal(3, agg.Activations);
        Assert.Equal(40, agg.TotalDamageAttempted);
        Assert.Equal(31, agg.TotalDamageDealt);
        Assert.Equal(5, agg.TotalDamageBlocked);
        Assert.Equal(4, agg.TotalDamageOverkill);
        Assert.Equal(6, agg.TotalTargets);
        Assert.Equal(2, agg.Kills);
    }

    [Fact]
    public void RelicAggregate_MrStrugglesDamageRecording_SplitsObservedDamage()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordMrStrugglesDamageForTest(
            agg,
            new[]
            {
                (BlockedDamage: 3, UnblockedDamage: 8, OverkillDamage: 0, WasTargetKilled: false),
                (BlockedDamage: 0, UnblockedDamage: 5, OverkillDamage: 4, WasTargetKilled: true),
            });

        Assert.Equal(20, agg.TotalDamageAttempted);
        Assert.Equal(13, agg.TotalDamageDealt);
        Assert.Equal(3, agg.TotalDamageBlocked);
        Assert.Equal(4, agg.TotalDamageOverkill);
        Assert.Equal(2, agg.TotalTargets);
        Assert.Equal(1, agg.Kills);
    }

    [Fact]
    public void RelicTooltip_MrStruggles_ShowsDamageTemplateRows()
    {
        var body = BuildBody(new RelicAggregate
        {
            Activations = 3,
            TotalDamageAttempted = 40,
            TotalDamageDealt = 31,
            TotalDamageBlocked = 5,
            TotalDamageOverkill = 4,
            TotalTargets = 6,
            Kills = 2,
        });

        Assert.Contains("Activations", body);
        Assert.Contains("Damage attempted", body);
        Assert.Contains("Damage dealt", body);
        Assert.Contains("Damage blocked", body);
        Assert.Contains("Overkill", body);
        Assert.Contains("Kills", body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("targets_hit"), body);
        Assert.Contains("Damage per activation", body);
        Assert.Contains("[b]40[/b]", body);
        Assert.Contains("[b]31[/b]", body);
        Assert.Contains("[b]10.33[/b]", body);
    }

    [Fact]
    public void RelicTooltip_MrStrugglesModelRecognition_UsesGameRelicId()
    {
        var relic = (MrStruggles)RuntimeHelpers.GetUninitializedObject(typeof(MrStruggles));

        Assert.Equal(MrStrugglesRelicId, RelicHoverShowPatch.GetStatsAggregateId(relic));
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildMrStrugglesBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildMrStrugglesBodyBBCode returned null."));
}
