using Godot;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class MapLegendStatsTooltipTests
{
    private static readonly Rect2 Viewport = new(0f, 0f, 1000f, 800f);

    [Fact]
    public void ClampInsideViewport_PreservesAnAlreadyVisiblePosition()
    {
        var position = new Vector2(300f, 250f);

        var result = MapLegendStatsTooltip.ClampInsideViewport(
            position,
            new Vector2(240f, 180f),
            Viewport);

        Assert.Equal(position, result);
    }

    [Fact]
    public void ClampInsideViewport_MovesRightAndBottomOverflowInsideMargin()
    {
        var result = MapLegendStatsTooltip.ClampInsideViewport(
            new Vector2(900f, 700f),
            new Vector2(300f, 200f),
            Viewport);

        Assert.Equal(new Vector2(692f, 592f), result);
    }

    [Fact]
    public void ClampInsideViewport_MovesLeftAndTopOverflowInsideMargin()
    {
        var result = MapLegendStatsTooltip.ClampInsideViewport(
            new Vector2(-40f, -20f),
            new Vector2(300f, 200f),
            Viewport);

        Assert.Equal(new Vector2(8f, 8f), result);
    }

    [Fact]
    public void ClampInsideViewport_AnchorsOversizedStackAtMinimumMargin()
    {
        var result = MapLegendStatsTooltip.ClampInsideViewport(
            new Vector2(300f, 200f),
            new Vector2(1200f, 900f),
            Viewport);

        Assert.Equal(new Vector2(8f, 8f), result);
    }
}
