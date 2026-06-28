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

public class UnleashStatsTests
{
    private static readonly MethodInfo AppendUnleashStatsMethod =
        typeof(CardHoverShowPatch).GetMethod("AppendUnleashStats", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("AppendUnleashStats not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void CardAggregate_UnleashFields_DefaultToZero()
    {
        var agg = new CardAggregate();

        Assert.Equal(0, agg.TotalOstyHpAttackBonus);
        Assert.Equal(0, agg.TimesOstyHpAttackBonusApplied);
    }

    [Fact]
    public void CardAggregate_UnleashFields_JsonRoundtrip_PreserveFields()
    {
        var run = new RunData();
        run.Aggregates["CARD.UNLEASH#1"] = new CardAggregate
        {
            TotalOstyHpAttackBonus = 27,
            TimesOstyHpAttackBonusApplied = 3,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("total_osty_hp_attack_bonus", json);
        Assert.Contains("times_osty_hp_attack_bonus_applied", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.Aggregates["CARD.UNLEASH#1"];
        Assert.Equal(27, agg.TotalOstyHpAttackBonus);
        Assert.Equal(3, agg.TimesOstyHpAttackBonusApplied);
    }

    [Fact]
    public void Tooltip_UnleashStats_ShowTotalAndAverage()
    {
        var sb = new StringBuilder();
        var agg = new CardAggregate
        {
            TotalOstyHpAttackBonus = 25,
            TimesOstyHpAttackBonusApplied = 2,
        };

        _ = AppendUnleashStatsMethod.Invoke(null, new object?[] { sb, new Unleash(), agg, false });
        var body = sb.ToString();

        Assert.Contains("Osty HP damage", body);
        Assert.Contains("[b]25[/b]", body);
        Assert.Contains("avg Osty HP damage", body);
        Assert.Contains("[b]12.5[/b]", body);
    }

    [Fact]
    public void Tooltip_UnleashStats_CompactShowsTotalOnly()
    {
        var sb = new StringBuilder();
        var agg = new CardAggregate
        {
            TotalOstyHpAttackBonus = 18,
            TimesOstyHpAttackBonusApplied = 1,
        };

        _ = AppendUnleashStatsMethod.Invoke(null, new object?[] { sb, new Unleash(), agg, true });
        var body = sb.ToString();

        Assert.Contains("Osty HP damage", body);
        Assert.Contains("[b]18[/b]", body);
        Assert.DoesNotContain("avg Osty HP damage", body);
    }
}
