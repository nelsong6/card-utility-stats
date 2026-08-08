using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class MapLegendStatsTooltipTests
{
    [Fact]
    public void ClampInsideViewport_PreservesAnAlreadyVisiblePosition()
    {
        var result = Clamp(300f, 250f, 240f, 180f);

        Assert.Equal(new CaptureFloatPoint(300f, 250f), result);
    }

    [Fact]
    public void ClampInsideViewport_MovesRightAndBottomOverflowInsideMargin()
    {
        var result = Clamp(900f, 700f, 300f, 200f);

        Assert.Equal(new CaptureFloatPoint(692f, 592f), result);
    }

    [Fact]
    public void ClampInsideViewport_MovesLeftAndTopOverflowInsideMargin()
    {
        var result = Clamp(-40f, -20f, 300f, 200f);

        Assert.Equal(new CaptureFloatPoint(8f, 8f), result);
    }

    [Fact]
    public void ClampInsideViewport_AnchorsOversizedStackAtMinimumMargin()
    {
        var result = Clamp(300f, 200f, 1200f, 900f);

        Assert.Equal(new CaptureFloatPoint(8f, 8f), result);
    }

    private static CaptureFloatPoint Clamp(
        float positionX,
        float positionY,
        float width,
        float height)
        => MapLegendStatsTooltip.ClampInsideViewportBounds(
            positionX,
            positionY,
            width,
            height,
            viewportX: 0f,
            viewportY: 0f,
            viewportWidth: 1000f,
            viewportHeight: 800f);
}
