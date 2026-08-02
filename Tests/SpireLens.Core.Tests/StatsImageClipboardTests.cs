using System.Buffers.Binary;
using Godot;
using Xunit;

namespace SpireLens.Core.Tests;

public class StatsImageClipboardTests
{
    [Fact]
    public void CalculatePixelRect_AccountsForViewportScaling()
    {
        var result = StatsImageCapture.CalculatePixelRect(
            new Rect2(100f, 50f, 200f, 100f),
            new Rect2(0f, 0f, 960f, 540f),
            new Vector2I(1920, 1080));

        Assert.Equal(new Rect2I(200, 100, 400, 200), result);
    }

    [Fact]
    public void CalculatePixelRect_ClipsPanelToViewportImage()
    {
        var result = StatsImageCapture.CalculatePixelRect(
            new Rect2(-10f, -5f, 30f, 15f),
            new Rect2(0f, 0f, 100f, 100f),
            new Vector2I(200, 200));

        Assert.Equal(new Rect2I(0, 0, 40, 20), result);
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
