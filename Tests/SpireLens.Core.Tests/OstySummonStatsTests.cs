using System;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Models.Cards;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class OstySummonStatsTests
{
    private static readonly MethodInfo AppendOstySummonStatsMethod =
        typeof(CardHoverShowPatch).GetMethod("AppendOstySummonStats", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("AppendOstySummonStats not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void OstySummonFields_DefaultToZero()
    {
        var agg = new CardAggregate();
        var meta = new RunMetaStats();

        Assert.Equal(0, agg.TimesOstySummoned);
        Assert.Equal(0m, agg.TotalOstyHpSummoned);
        Assert.Equal(0m, meta.TotalOstyDamageAbsorbed);
    }

    [Fact]
    public void OstySummonFields_JsonRoundtrip_PreserveCardAndMetaFields()
    {
        var run = new RunData();
        run.Aggregates["CARD.SUMMON_FORTH#1"] = new CardAggregate
        {
            TimesOstySummoned = 2,
            TotalOstyHpSummoned = 18m,
        };
        run.MetaStats.TotalOstyDamageAbsorbed = 11m;

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("times_osty_summoned", json);
        Assert.Contains("total_osty_hp_summoned", json);
        Assert.Contains("total_osty_damage_absorbed", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.Aggregates["CARD.SUMMON_FORTH#1"];
        Assert.Equal(2, agg.TimesOstySummoned);
        Assert.Equal(18m, agg.TotalOstyHpSummoned);
        Assert.Equal(11m, restored.MetaStats.TotalOstyDamageAbsorbed);
    }

    [Fact]
    public void Tooltip_OstySummonStats_FullViewShowsCardSummonsAndMetaAbsorbedDamage()
    {
        var sb = new StringBuilder();
        var agg = new CardAggregate
        {
            TimesOstySummoned = 2,
            TotalOstyHpSummoned = 18m,
        };
        var meta = new RunMetaStats { TotalOstyDamageAbsorbed = 11m };

        _ = AppendOstySummonStatsMethod.Invoke(null, new object?[] { sb, new SummonForth(), agg, meta, false });
        var body = sb.ToString();

        Assert.Contains("Osty HP summoned", body);
        Assert.Contains("[b]18[/b]", body);
        Assert.Contains("Osty summons", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("Osty dmg absorbed", body);
        Assert.Contains("[b]11[/b]", body);
    }

    [Fact]
    public void Tooltip_OstySummonStats_CompactSkipsSummonCount()
    {
        var sb = new StringBuilder();
        var agg = new CardAggregate
        {
            TimesOstySummoned = 2,
            TotalOstyHpSummoned = 18m,
        };
        var meta = new RunMetaStats { TotalOstyDamageAbsorbed = 11m };

        _ = AppendOstySummonStatsMethod.Invoke(null, new object?[] { sb, new SummonForth(), agg, meta, true });
        var body = sb.ToString();

        Assert.Contains("Osty HP summoned", body);
        Assert.Contains("Osty dmg absorbed", body);
        Assert.DoesNotContain("Osty summons", body);
    }

    [Fact]
    public void Tooltip_OstySummonStats_MetaDamageCanShowOnUnplayedSummonCard()
    {
        var sb = new StringBuilder();
        var meta = new RunMetaStats { TotalOstyDamageAbsorbed = 11m };

        _ = AppendOstySummonStatsMethod.Invoke(null, new object?[] { sb, new SummonForth(), new CardAggregate(), meta, false });
        var body = sb.ToString();

        Assert.DoesNotContain("Osty HP summoned", body);
        Assert.Contains("Osty dmg absorbed", body);
        Assert.Contains("[b]11[/b]", body);
    }
}
