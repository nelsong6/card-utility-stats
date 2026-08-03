using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class RunTimerStatsTooltipTests
{
    [Fact]
    public void LeftPlacement_LeavesClearanceBeforeTimer()
    {
        var target = new Rect2(700f, 20f, 120f, 40f);
        var viewport = new Rect2(0f, 0f, 928f, 512f);

        var x = RunTimerStatsTooltip.GetClearTooltipX(
            target,
            tooltipWidth: 560f,
            HoverTipAlignment.Left,
            viewport);

        Assert.Equal(120f, x);
        Assert.True(x + 560f < target.Position.X);
    }

    [Fact]
    public void RightPlacement_LeavesClearanceAfterTimer()
    {
        var target = new Rect2(100f, 20f, 120f, 40f);
        var viewport = new Rect2(0f, 0f, 1200f, 700f);

        var x = RunTimerStatsTooltip.GetClearTooltipX(
            target,
            tooltipWidth: 560f,
            HoverTipAlignment.Right,
            viewport);

        Assert.Equal(240f, x);
        Assert.True(x > target.Position.X + target.Size.X);
    }

    [Fact]
    public void Placement_StaysInsideViewportMargin()
    {
        var target = new Rect2(300f, 20f, 120f, 40f);
        var viewport = new Rect2(0f, 0f, 640f, 480f);

        var x = RunTimerStatsTooltip.GetClearTooltipX(
            target,
            tooltipWidth: 560f,
            HoverTipAlignment.Left,
            viewport);

        Assert.Equal(8f, x);
    }

    [Fact]
    public void TimerBody_ChangesWhenTrackedClockAdvances()
    {
        var before = RunTimeStatsTooltip.BuildBodyBBCode(
            new RunTimeStats { CombatSeconds = 216 });
        var after = RunTimeStatsTooltip.BuildBodyBBCode(
            new RunTimeStats { CombatSeconds = 217 });

        Assert.Contains("03:36", before);
        Assert.Contains("03:37", after);
        Assert.NotEqual(before, after);
    }
}
