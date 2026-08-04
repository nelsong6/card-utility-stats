using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class MercuryHourglassStatsTests
{
    private const string MercuryHourglassRelicId = "RELIC.MERCURY_HOURGLASS";

    private static readonly MethodInfo BuildMercuryHourglassBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildMercuryHourglassBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildMercuryHourglassBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_MercuryHourglassDamageStats_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[MercuryHourglassRelicId] = new RelicAggregate
        {
            Activations = 2,
            TotalDamageAttempted = 33,
            TotalDamageDealt = 26,
            TotalDamageBlocked = 4,
            TotalDamageOverkill = 3,
            TotalTargets = 7,
            Kills = 1,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("relic_aggregates", json);
        Assert.Contains("activations", json);
        Assert.Contains("total_damage_attempted", json);
        Assert.Contains("total_damage_dealt", json);
        Assert.Contains("total_damage_blocked", json);
        Assert.Contains("total_damage_overkill", json);
        Assert.Contains("total_targets", json);
        Assert.Contains("kills", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[MercuryHourglassRelicId];
        Assert.Equal(2, agg.Activations);
        Assert.Equal(33, agg.TotalDamageAttempted);
        Assert.Equal(26, agg.TotalDamageDealt);
        Assert.Equal(4, agg.TotalDamageBlocked);
        Assert.Equal(3, agg.TotalDamageOverkill);
        Assert.Equal(7, agg.TotalTargets);
        Assert.Equal(1, agg.Kills);
    }

    [Fact]
    public void RelicAggregate_MercuryHourglassDamageRecording_SplitsObservedDamage()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordMercuryHourglassDamageForTest(
            agg,
            new[]
            {
                (BlockedDamage: 2, UnblockedDamage: 4, OverkillDamage: 0, WasTargetKilled: false),
                (BlockedDamage: 0, UnblockedDamage: 3, OverkillDamage: 5, WasTargetKilled: true),
            });

        Assert.Equal(14, agg.TotalDamageAttempted);
        Assert.Equal(7, agg.TotalDamageDealt);
        Assert.Equal(2, agg.TotalDamageBlocked);
        Assert.Equal(5, agg.TotalDamageOverkill);
        Assert.Equal(2, agg.TotalTargets);
        Assert.Equal(1, agg.Kills);
    }

    [Fact]
    public void RelicTooltip_MercuryHourglassFields_ShowDamageRows()
    {
        var body = BuildBody(new RelicAggregate
        {
            Activations = 2,
            TotalDamageAttempted = 33,
            TotalDamageDealt = 25,
            TotalDamageBlocked = 5,
            TotalDamageOverkill = 3,
            TotalTargets = 4,
            Kills = 1,
        });

        Assert.Contains("Combats triggered", body);
        Assert.Contains("Damage attempted", body);
        Assert.Contains("Damage dealt", body);
        Assert.Contains("Damage blocked", body);
        Assert.Contains("Overkill", body);
        Assert.Contains("Kills", body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("targets_hit"), body);
        Assert.Contains("Damage per combat", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]33[/b]", body);
        Assert.Contains("[b]25[/b]", body);
        Assert.Contains("[b]12.5[/b]", body);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildMercuryHourglassBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildMercuryHourglassBodyBBCode returned null."));
}
