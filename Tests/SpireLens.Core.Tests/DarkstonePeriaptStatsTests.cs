using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class DarkstonePeriaptStatsTests
{
    private const string DarkstonePeriaptRelicId = "RELIC.DARKSTONE_PERIAPT";

    private static readonly MethodInfo BuildDarkstonePeriaptBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildDarkstonePeriaptBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildDarkstonePeriaptBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_DarkstonePeriaptFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0, agg.CursesAcquired);
        Assert.Equal(0, agg.TotalMaxHpGained);
        Assert.Null(agg.OriginalMaxHp);
        Assert.Null(agg.NewMaxHp);
    }

    [Fact]
    public void RelicAggregate_DarkstonePeriaptFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[DarkstonePeriaptRelicId] = new RelicAggregate
        {
            Activations = 3,
            CursesAcquired = 3,
            TotalMaxHpGained = 18,
            OriginalMaxHp = 70m,
            NewMaxHp = 88m,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("curses_acquired", json);
        Assert.Contains("total_max_hp_gained", json);
        Assert.Contains("original_max_hp", json);
        Assert.Contains("new_max_hp", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[DarkstonePeriaptRelicId];
        Assert.Equal(3, restoredAgg.Activations);
        Assert.Equal(3, restoredAgg.CursesAcquired);
        Assert.Equal(18, restoredAgg.TotalMaxHpGained);
        Assert.Equal(70m, restoredAgg.OriginalMaxHp);
        Assert.Equal(88m, restoredAgg.NewMaxHp);
    }

    [Fact]
    public void RunTracker_RecordDarkstonePeriaptCurseAcquired_AccumulatesAndClampsHpGain()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordDarkstonePeriaptCurseAcquiredForTest(agg, 6, 70m, 76m);
        RunTracker.RecordDarkstonePeriaptCurseAcquiredForTest(agg, 12, 76m, 88m);
        RunTracker.RecordDarkstonePeriaptCurseAcquiredForTest(agg, -5, 88m, 88m);

        Assert.Equal(3, agg.Activations);
        Assert.Equal(3, agg.CursesAcquired);
        Assert.Equal(18, agg.TotalMaxHpGained);
        Assert.Equal(70m, agg.OriginalMaxHp);
        Assert.Equal(88m, agg.NewMaxHp);
    }

    [Fact]
    public void RelicTooltip_DarkstonePeriapt_ShowsCurseAndHpRows()
    {
        var agg = new RelicAggregate
        {
            Activations = 3,
            CursesAcquired = 3,
            TotalMaxHpGained = 18,
            OriginalMaxHp = 70m,
            NewMaxHp = 88m,
        };

        var body = (string)(BuildDarkstonePeriaptBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildDarkstonePeriaptBodyBBCode returned null."));

        Assert.Contains("Curses acquired", body);
        Assert.Contains("Original max HP", body);
        Assert.Contains("New max HP", body);
        Assert.Contains("Max HP gained", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[b]18[/b]", body);
        Assert.Contains("[b]70[/b]", body);
        Assert.Contains("[b]88[/b]", body);
    }
}
