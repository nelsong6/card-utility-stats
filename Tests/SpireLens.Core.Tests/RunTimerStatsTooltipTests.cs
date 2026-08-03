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
        var x = RunTimerStatsTooltip.GetClearTooltipX(
            targetX: 700f,
            targetWidth: 120f,
            tooltipWidth: 560f,
            HoverTipAlignment.Left,
            viewportX: 0f,
            viewportWidth: 928f);

        Assert.Equal(120f, x);
        Assert.True(x + 560f < 700f);
    }

    [Fact]
    public void RightPlacement_LeavesClearanceAfterTimer()
    {
        var x = RunTimerStatsTooltip.GetClearTooltipX(
            targetX: 100f,
            targetWidth: 120f,
            tooltipWidth: 560f,
            HoverTipAlignment.Right,
            viewportX: 0f,
            viewportWidth: 1200f);

        Assert.Equal(240f, x);
        Assert.True(x > 220f);
    }

    [Fact]
    public void Placement_StaysInsideViewportMargin()
    {
        var x = RunTimerStatsTooltip.GetClearTooltipX(
            targetX: 300f,
            targetWidth: 120f,
            tooltipWidth: 560f,
            HoverTipAlignment.Left,
            viewportX: 0f,
            viewportWidth: 640f);

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
        Assert.Contains(
            StatConceptGlossary.RenderInformationHint(
                "Total time spent in combats this run."),
            before);
        Assert.Contains("03:37", after);
        Assert.NotEqual(before, after);
    }
}
