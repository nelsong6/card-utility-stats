using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

public sealed class StatsTooltipOwnershipTests
{
    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void StatsTooltip_CreatesNativeHoverTipData()
    {
        var tip = StatsTooltip.CreateNativeTip(
            "Wrought in War #1",
            "[b]Played[/b] 2",
            stretchHorizontally: true);

        Assert.Equal("Wrought in War #1", tip.Title);
        Assert.Equal(
            "[font_size=20][b]Played[/b] 2[/font_size]",
            tip.Description);
        Assert.Equal("SPIRELENS.STATS", tip.Id);
        Assert.True(tip.ShouldOverrideTextOverflow);
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void StatsTooltip_CreatesTitlelessEscapedNativeHintData()
    {
        var tip = StatsTooltip.CreateNativeHint("Activation [observed]");

        Assert.Null(tip.Title);
        Assert.Equal(
            "[font_size=20]Activation [lb]observed][/font_size]",
            tip.Description);
        Assert.Equal("SPIRELENS.HINT", tip.Id);
    }

    [Fact]
    public void StatsTooltip_DoesNotOwnGodotUiState()
    {
        var uiStateFields = typeof(StatsTooltip)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => typeof(Node).IsAssignableFrom(field.FieldType)
                            || field.FieldType == typeof(SceneTree))
            .ToArray();

        Assert.Empty(uiStateFields);
    }

    [Fact]
    public void NativeAugmentationPatch_TargetMethodHasExpectedSignature()
    {
        Assert.NotNull(typeof(NHoverTipSet).GetMethod(
            nameof(NHoverTipSet.CreateAndShow),
            new[]
            {
                typeof(Control),
                typeof(IEnumerable<IHoverTip>),
                typeof(HoverTipAlignment),
            }));
    }
}
