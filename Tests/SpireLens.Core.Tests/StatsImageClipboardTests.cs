using System.Buffers.Binary;
using Xunit;

namespace SpireLens.Core.Tests;

public class StatsImageClipboardTests
{
    [Fact]
    public void CalculatePixelRect_AccountsForViewportScaling()
    {
        var result = StatsImageCapture.CalculatePixelBounds(
            100f, 50f, 200f, 100f,
            0f, 0f, 960f, 540f,
            1920, 1080);

        Assert.Equal(new CapturePixelRect(200, 100, 400, 200), result);
    }

    [Fact]
    public void CalculatePixelRect_ClipsPanelToViewportImage()
    {
        var result = StatsImageCapture.CalculatePixelBounds(
            -10f, -5f, 30f, 15f,
            0f, 0f, 100f, 100f,
            200, 200);

        Assert.Equal(new CapturePixelRect(0, 0, 40, 20), result);
    }

    [Fact]
    public void TransformRect_AppliesCanvasScaleAndTranslation()
    {
        var result = StatsImageCapture.TransformBounds(
            0f, 0f, 100f, 50f,
            2f, 0f, 0f, 3f,
            10f, 20f);

        Assert.Equal(new CaptureFloatRect(10f, 20f, 200f, 150f), result);
    }

    [Fact]
    public void CalculateShareBounds_PreservesVisibleRelativePlacement()
    {
        var result = StatsImageCapture.CalculateShareBounds(
            new CaptureFloatRect(700f, 100f, 100f, 150f),
            new CaptureFloatRect(100f, 250f, 400f, 300f));

        Assert.Equal(new CaptureFloatRect(90f, 90f, 720f, 470f), result);
    }

    [Fact]
    public void BuildDib_WritesHeaderAndBottomUpBgraPixels()
    {
        // Top row: red, green. Bottom row: blue, white.
        byte[] rgba =
        [
            255, 0, 0, 255,
            0, 255, 0, 255,
            0, 0, 255, 255,
            255, 255, 255, 255,
        ];

        var dib = WindowsImageClipboard.BuildDib(2, 2, rgba);

        Assert.Equal(56, dib.Length);
        Assert.Equal(40, BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(0, 4)));
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(4, 4)));
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(8, 4)));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(dib.AsSpan(12, 2)));
        Assert.Equal(32, BinaryPrimitives.ReadInt16LittleEndian(dib.AsSpan(14, 2)));
        Assert.Equal(
            new byte[]
            {
                // Bottom row first, in BGRA order.
                255, 0, 0, 255,
                255, 255, 255, 255,
                // Top row second, in BGRA order.
                0, 0, 255, 255,
                0, 255, 0, 255,
            },
            dib[40..]);
    }

    [Fact]
    public void BuildDib_RejectsMismatchedPixelData()
    {
        Assert.Throws<ArgumentException>(() =>
            WindowsImageClipboard.BuildDib(2, 2, new byte[4]));
    }
}
