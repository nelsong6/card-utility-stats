using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class BronzeScalesStatsTests
{
    private const string BronzeScalesRelicId = "RELIC.BRONZE_SCALES";

    private static readonly MethodInfo BuildBronzeScalesBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildBronzeScalesBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildBronzeScalesBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_BronzeScalesDamageStats_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[BronzeScalesRelicId] = new RelicAggregate
        {
            Activations = 4,
            TotalDamageAttempted = 13,
            TotalDamageDealt = 10,
            TotalDamageBlocked = 2,
            TotalDamageOverkill = 1,
            TotalTargets = 4,
            Kills = 1,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("relic_aggregates", json);
        Assert.Contains(BronzeScalesRelicId, json);
        Assert.Contains("total_damage_dealt", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[BronzeScalesRelicId];
        Assert.Equal(4, agg.Activations);
        Assert.Equal(13, agg.TotalDamageAttempted);
        Assert.Equal(10, agg.TotalDamageDealt);
        Assert.Equal(2, agg.TotalDamageBlocked);
        Assert.Equal(1, agg.TotalDamageOverkill);
        Assert.Equal(4, agg.TotalTargets);
        Assert.Equal(1, agg.Kills);
    }

    [Fact]
    public void RelicAggregate_BronzeScalesDamageRecording_SplitsObservedDamage()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordBronzeScalesDamageForTest(
            agg,
            new[]
            {
                (BlockedDamage: 1, UnblockedDamage: 3, OverkillDamage: 0, WasTargetKilled: false),
                (BlockedDamage: 0, UnblockedDamage: 2, OverkillDamage: 4, WasTargetKilled: true),
            },
            totalAmount: 3m,
            attributedAmount: 3m);

        Assert.Equal(10, agg.TotalDamageAttempted);
        Assert.Equal(5, agg.TotalDamageDealt);
        Assert.Equal(1, agg.TotalDamageBlocked);
        Assert.Equal(4, agg.TotalDamageOverkill);
        Assert.Equal(2, agg.TotalTargets);
        Assert.Equal(1, agg.Kills);
    }

    [Fact]
    public void RelicAggregate_BronzeScalesStackedThorns_CreditsOnlyBronzeShare()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordBronzeScalesDamageForTest(
            agg,
            new[]
            {
                (BlockedDamage: 0, UnblockedDamage: 8, OverkillDamage: 0, WasTargetKilled: false),
            },
            totalAmount: 8m,
            attributedAmount: 3m);

        Assert.Equal(3, agg.TotalDamageAttempted);
        Assert.Equal(3, agg.TotalDamageDealt);
        Assert.Equal(0, agg.TotalDamageBlocked);
        Assert.Equal(0, agg.TotalDamageOverkill);
        Assert.Equal(1, agg.TotalTargets);
    }

    [Fact]
    public void RelicTooltip_BronzeScalesFields_ShowZeroRows()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Times triggered", body);
        Assert.Contains("Damage attempted", body);
        Assert.Contains("Damage dealt", body);
        Assert.Contains("Damage blocked", body);
        Assert.Contains("Overkill", body);
        Assert.Contains("Kills", body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("targets_hit"), body);
        Assert.Contains("Damage per trigger", body);
        Assert.Contains("[b]0[/b]", body);
        Assert.Equal(1, CountOccurrences(body, "[table=4]"));
        Assert.DoesNotContain("[table=2]", body);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildBronzeScalesBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildBronzeScalesBodyBBCode returned null."));

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
