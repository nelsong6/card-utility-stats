using System;
using Godot;

namespace SpireLens.Core;

/// <summary>
/// Captures an already-rendered UI control from its viewport. The control's
/// logical Godot coordinates are converted to texture pixels so resolution
/// scaling does not crop the wrong part of the frame.
/// </summary>
internal static class StatsImageCapture
{
    public static bool TryCapture(
        Control control,
        out Image image,
        out string error)
    {
        image = null!;
        error = string.Empty;

        if (control == null || !GodotObject.IsInstanceValid(control))
        {
            error = "The stats panel is no longer available.";
            return false;
        }

        var viewport = control.GetViewport();
        if (viewport == null || !GodotObject.IsInstanceValid(viewport))
        {
            error = "The stats panel has no active viewport.";
            return false;
        }

        using var viewportImage = viewport.GetTexture()?.GetImage();
        if (viewportImage == null
            || viewportImage.GetWidth() <= 0
            || viewportImage.GetHeight() <= 0)
        {
            error = "The game viewport did not provide an image.";
            return false;
        }

        var pixelRect = CalculatePixelRect(
            control.GetGlobalRect(),
            viewport.GetVisibleRect(),
            new Vector2I(
                viewportImage.GetWidth(),
                viewportImage.GetHeight()));
        if (pixelRect.Size.X <= 0 || pixelRect.Size.Y <= 0)
        {
            error = "The stats panel is outside the visible game viewport.";
            return false;
        }

        image = viewportImage.GetRegion(pixelRect);
        if (image == null || image.GetWidth() <= 0 || image.GetHeight() <= 0)
        {
            image?.Dispose();
            image = null!;
            error = "The stats panel image was empty.";
            return false;
        }

        return true;
    }

    internal static Rect2I CalculatePixelRect(
        Rect2 controlRect,
        Rect2 viewportRect,
        Vector2I imageSize)
    {
        if (controlRect.Size.X <= 0f
            || controlRect.Size.Y <= 0f
            || viewportRect.Size.X <= 0f
            || viewportRect.Size.Y <= 0f
            || imageSize.X <= 0
            || imageSize.Y <= 0)
        {
            return new Rect2I();
        }

        var scaleX = imageSize.X / viewportRect.Size.X;
        var scaleY = imageSize.Y / viewportRect.Size.Y;
        var relativeLeft = controlRect.Position.X - viewportRect.Position.X;
        var relativeTop = controlRect.Position.Y - viewportRect.Position.Y;
        var relativeRight = controlRect.End.X - viewportRect.Position.X;
        var relativeBottom = controlRect.End.Y - viewportRect.Position.Y;

        var left = Math.Clamp(
            (int)MathF.Floor(relativeLeft * scaleX),
            0,
            imageSize.X);
        var top = Math.Clamp(
            (int)MathF.Floor(relativeTop * scaleY),
            0,
            imageSize.Y);
        var right = Math.Clamp(
            (int)MathF.Ceiling(relativeRight * scaleX),
            0,
            imageSize.X);
        var bottom = Math.Clamp(
            (int)MathF.Ceiling(relativeBottom * scaleY),
            0,
            imageSize.Y);

        return new Rect2I(
            left,
            top,
            Math.Max(0, right - left),
            Math.Max(0, bottom - top));
    }
}
