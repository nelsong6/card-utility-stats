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

public class UnmovableStatsTests
{
    private static readonly MethodInfo AppendUnmovablePowerStatsMethod =
        typeof(CardHoverShowPatch).GetMethod("AppendUnmovablePowerStats", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("AppendUnmovablePowerStats not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void UnmovableMetaField_DefaultsToZero()
    {
        var meta = new RunMetaStats();

        Assert.Equal(0m, meta.ExtraBlockGainedFromUnmovablePower);
    }

    [Fact]
    public void UnmovableMetaField_JsonRoundtrip_PreservesExtraBlock()
    {
        var run = new RunData();
        run.MetaStats.ExtraBlockGainedFromUnmovablePower = 24m;

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("extra_block_gained_from_unmovable_power", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        Assert.Equal(24m, restored!.MetaStats.ExtraBlockGainedFromUnmovablePower);
    }

    [Fact]
    public void RecordUnmovablePowerExtraBlockForTest_IgnoresNonPositiveAmounts()
    {
        var meta = new RunMetaStats();

        RunTracker.RecordUnmovablePowerExtraBlockForTest(meta, 7m);
        RunTracker.RecordUnmovablePowerExtraBlockForTest(meta, 0m);
        RunTracker.RecordUnmovablePowerExtraBlockForTest(meta, -3m);

        Assert.Equal(7m, meta.ExtraBlockGainedFromUnmovablePower);
    }

    [Trait("Category", "RequiresLiveGame")]
    [Fact]
    public void Tooltip_UnmovablePowerStats_ShowsGlobalExtraBlockOnUnmovable()
    {
        var sb = new StringBuilder();
        var meta = new RunMetaStats { ExtraBlockGainedFromUnmovablePower = 18m };

        _ = AppendUnmovablePowerStatsMethod.Invoke(null, new object?[] { sb, new Unmovable(), meta });
        var body = sb.ToString();

        Assert.Contains("Extra block gained from unmovable's power", body);
        Assert.Contains("[b]18[/b]", body);
    }

    [Trait("Category", "RequiresLiveGame")]
    [Fact]
    public void Tooltip_UnmovablePowerStats_ShowsZeroOnUnmovable()
    {
        var sb = new StringBuilder();

        _ = AppendUnmovablePowerStatsMethod.Invoke(null, new object?[] { sb, new Unmovable(), new RunMetaStats() });
        var body = sb.ToString();

        Assert.Contains("Extra block gained from unmovable's power", body);
        Assert.Contains("[b]0[/b]", body);
    }

    [Trait("Category", "RequiresLiveGame")]
    [Fact]
    public void Tooltip_UnmovablePowerStats_DoesNotShowOnOtherCards()
    {
        var sb = new StringBuilder();
        var meta = new RunMetaStats { ExtraBlockGainedFromUnmovablePower = 18m };

        _ = AppendUnmovablePowerStatsMethod.Invoke(null, new object?[] { sb, new Bash(), meta });

        Assert.DoesNotContain("Extra block gained from unmovable's power", sb.ToString());
    }
}
