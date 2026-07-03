using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class FestivePopperStatsTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_FestivePopperUsesExistingDamageFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates["RELIC.FESTIVE_POPPER"] = new RelicAggregate
        {
            Activations = 2,
            TotalDamageAttempted = 33,
            TotalDamageDealt = 26,
            TotalDamageBlocked = 4,
            TotalDamageOverkill = 3,
            TotalTargets = 4,
            Kills = 1,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("total_damage_attempted", json);
        Assert.Contains("total_damage_dealt", json);
        Assert.Contains("total_damage_blocked", json);
        Assert.Contains("total_damage_overkill", json);
        Assert.Contains("total_targets", json);
        Assert.Contains("kills", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates["RELIC.FESTIVE_POPPER"];
        Assert.Equal(2, agg.Activations);
        Assert.Equal(33, agg.TotalDamageAttempted);
        Assert.Equal(26, agg.TotalDamageDealt);
        Assert.Equal(4, agg.TotalDamageBlocked);
        Assert.Equal(3, agg.TotalDamageOverkill);
        Assert.Equal(4, agg.TotalTargets);
        Assert.Equal(1, agg.Kills);
    }

    [Fact]
    public void RelicAggregate_FestivePopperDamageRecording_SplitsObservedDamage()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordFestivePopperDamageForTest(
            agg,
            new[]
            {
                (BlockedDamage: 2, UnblockedDamage: 7, OverkillDamage: 0, WasTargetKilled: false),
                (BlockedDamage: 0, UnblockedDamage: 3, OverkillDamage: 6, WasTargetKilled: true),
            });

        Assert.Equal(18, agg.TotalDamageAttempted);
        Assert.Equal(10, agg.TotalDamageDealt);
        Assert.Equal(2, agg.TotalDamageBlocked);
        Assert.Equal(6, agg.TotalDamageOverkill);
        Assert.Equal(2, agg.TotalTargets);
        Assert.Equal(1, agg.Kills);
    }

    [Fact]
    public void RelicTooltip_FestivePopperFields_ShowDamageRows()
    {
        var body = InvokeTooltipBuilder(new RelicAggregate
        {
            Activations = 2,
            TotalDamageDealt = 25,
        });

        Assert.Contains("Combats triggered", body);
        Assert.Contains("Damage dealt", body);
        Assert.Contains("Damage per combat", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]25[/b]", body);
        Assert.Contains("[b]12.5[/b]", body);
    }

    private static string InvokeTooltipBuilder(RelicAggregate agg)
    {
        var method = typeof(RelicHoverShowPatch).GetMethod(
            "BuildFestivePopperBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (string)(method!.Invoke(null, new object[] { agg })
            ?? throw new InvalidOperationException("BuildFestivePopperBodyBBCode returned null."));
    }
}
