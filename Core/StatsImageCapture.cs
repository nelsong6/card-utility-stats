using System;
using System.Collections.Generic;
using Godot;

namespace SpireLens.Core;

internal readonly record struct CaptureFloatRect(
    float X,
    float Y,
    float Width,
    float Height);

internal readonly record struct CapturePixelRect(
    int X,
    int Y,
    int Width,
    int Height);

/// <summary>
/// Captures an already-rendered UI control from its viewport. The control's
/// logical Godot coordinates are converted to texture pixels so resolution
/// scaling does not crop the wrong part of the frame.
/// </summary>
internal static class StatsImageCapture
{
    private const int ShareImagePadding = 24;
    private const int ShareImageGap = 24;
    private const int MaxRenderedSubjectWidth = 420;
    private const int MaxRenderedSubjectHeight = 560;
    private const int MaxIsolatedSubjectSize = 224;
    private const float TooltipCaptureMargin = 10f;

    private static readonly Color ShareImageBackground =
        Color.FromHtml("#0B0910");

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

        if (!TryNormalizeForComposition(viewportImage, out error))
            return false;

        var pixelRect = CalculatePixelRect(
            GetViewportRect(control),
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

    /// <summary>
    /// Builds a clipboard-ready share image from the rendered tooltip set and
    /// the thing that owns it. Relics can provide their source texture so the
    /// composition contains isolated artwork instead of the surrounding relic
    /// bar; cards and other targets use their exact rendered viewport bounds.
    /// </summary>
    public static bool TryCaptureShareImage(
        Control viewportAnchor,
        Rect2 renderedSubjectRect,
        Texture2D? isolatedSubjectTexture,
        IReadOnlyList<Control> tooltipGroups,
        out Image image,
        out string error)
    {
        image = null!;
        error = string.Empty;

        if (viewportAnchor == null
            || !GodotObject.IsInstanceValid(viewportAnchor))
        {
            error = "The pinned item is no longer available.";
            return false;
        }

        var viewport = viewportAnchor.GetViewport();
        if (viewport == null || !GodotObject.IsInstanceValid(viewport))
        {
            error = "The pinned item has no active viewport.";
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

        if (!TryNormalizeForComposition(viewportImage, out error))
            return false;

        if (!TryGetVisibleBounds(tooltipGroups, out var tooltipBounds))
        {
            error = "The pinned tooltip set is outside the visible game viewport.";
            return false;
        }

        tooltipBounds = Grow(tooltipBounds, TooltipCaptureMargin);
        var viewportRect = viewport.GetVisibleRect();
        var viewportImageSize = new Vector2I(
            viewportImage.GetWidth(),
            viewportImage.GetHeight());
        var tooltipPixelRect = CalculatePixelRect(
            tooltipBounds,
            viewportRect,
            viewportImageSize);
        if (tooltipPixelRect.Size.X <= 0 || tooltipPixelRect.Size.Y <= 0)
        {
            error = "The pinned tooltip set is outside the visible game viewport.";
            return false;
        }

        using var tooltipImage = viewportImage.GetRegion(tooltipPixelRect);
        if (tooltipImage == null
            || tooltipImage.GetWidth() <= 0
            || tooltipImage.GetHeight() <= 0)
        {
            error = "The pinned tooltip image was empty.";
            return false;
        }

        Image? subjectImage = null;
        try
        {
            var isolatedSubject = TryCaptureTexture(
                isolatedSubjectTexture,
                out subjectImage);
            if (!isolatedSubject)
            {
                var subjectPixelRect = CalculatePixelRect(
                    renderedSubjectRect,
                    viewportRect,
                    viewportImageSize);
                if (subjectPixelRect.Size.X > 0 && subjectPixelRect.Size.Y > 0)
                    subjectImage = viewportImage.GetRegion(subjectPixelRect);
            }

            if (subjectImage != null
                && subjectImage.GetWidth() > 0
                && subjectImage.GetHeight() > 0)
            {
                ResizeSubject(subjectImage, isolatedSubject);
            }
            else
            {
                subjectImage?.Dispose();
                subjectImage = null;
            }

            var subjectWidth = subjectImage?.GetWidth() ?? 0;
            var subjectHeight = subjectImage?.GetHeight() ?? 0;
            var contentGap = subjectImage == null ? 0 : ShareImageGap;
            var width = checked(
                ShareImagePadding * 2
                + subjectWidth
                + contentGap
                + tooltipImage.GetWidth());
            var height = checked(
                ShareImagePadding * 2
                + Math.Max(subjectHeight, tooltipImage.GetHeight()));

            var combined = Image.CreateEmpty(
                width,
                height,
                false,
                Image.Format.Rgba8);
            combined.Fill(ShareImageBackground);

            if (subjectImage != null)
            {
                combined.BlendRect(
                    subjectImage,
                    new Rect2I(
                        0,
                        0,
                        subjectImage.GetWidth(),
                        subjectImage.GetHeight()),
                    new Vector2I(
                        ShareImagePadding,
                        (height - subjectImage.GetHeight()) / 2));
            }

            combined.BlitRect(
                tooltipImage,
                new Rect2I(
                    0,
                    0,
                    tooltipImage.GetWidth(),
                    tooltipImage.GetHeight()),
                new Vector2I(
                    ShareImagePadding + subjectWidth + contentGap,
                    (height - tooltipImage.GetHeight()) / 2));

            image = combined;
            return true;
        }
        finally
        {
            subjectImage?.Dispose();
        }
    }

    private static bool TryCaptureTexture(
        Texture2D? texture,
        out Image? image)
    {
        image = null;
        if (texture == null || !GodotObject.IsInstanceValid(texture))
            return false;

        try
        {
            using var source = texture.GetImage();
            if (source == null
                || source.GetWidth() <= 0
                || source.GetHeight() <= 0)
            {
                return false;
            }

            if (!TryNormalizeForComposition(source, out _))
                return false;

            var usedRect = source.GetUsedRect();
            if (usedRect.Size.X <= 0 || usedRect.Size.Y <= 0)
                return false;

            image = source.GetRegion(usedRect);
            return image.GetWidth() > 0 && image.GetHeight() > 0;
        }
        catch
        {
            image?.Dispose();
            image = null;
            return false;
        }
    }

    private static bool TryNormalizeForComposition(
        Image image,
        out string error)
    {
        error = string.Empty;
        if (image.IsCompressed())
        {
            var decompressError = image.Decompress();
            if (decompressError != Error.Ok)
            {
                error = $"Could not decompress the captured image ({decompressError}).";
                return false;
            }
        }

        if (image.GetFormat() != Image.Format.Rgba8)
            image.Convert(Image.Format.Rgba8);

        if (image.GetFormat() == Image.Format.Rgba8)
            return true;

        error = "Could not convert the captured image to RGBA pixels.";
        return false;
    }

    private static void ResizeSubject(Image subject, bool isolatedSubject)
    {
        var maxWidth = isolatedSubject
            ? MaxIsolatedSubjectSize
            : MaxRenderedSubjectWidth;
        var maxHeight = isolatedSubject
            ? MaxIsolatedSubjectSize
            : MaxRenderedSubjectHeight;
        var scale = Math.Min(
            1d,
            Math.Min(
                maxWidth / (double)subject.GetWidth(),
                maxHeight / (double)subject.GetHeight()));
        if (scale >= 1d) return;

        subject.Resize(
            Math.Max(
                1,
                (int)Math.Round(
                    subject.GetWidth() * scale,
                    MidpointRounding.AwayFromZero)),
            Math.Max(
                1,
                (int)Math.Round(
                    subject.GetHeight() * scale,
                    MidpointRounding.AwayFromZero)),
            Image.Interpolation.Lanczos);
    }

    private static bool TryGetVisibleBounds(
        IReadOnlyList<Control> controls,
        out Rect2 bounds)
    {
        bounds = default;
        var found = false;
        foreach (var control in controls)
        {
            if (control == null
                || !GodotObject.IsInstanceValid(control)
                || !control.IsVisibleInTree())
            {
                continue;
            }

            var rect = GetViewportRect(control);
            if (rect.Size.X <= 0f || rect.Size.Y <= 0f) continue;

            bounds = found ? Merge(bounds, rect) : rect;
            found = true;
        }

        return found;
    }

    internal static Rect2 GetViewportRect(Control control)
        => TransformRect(
            new Rect2(Vector2.Zero, control.Size),
            control.GetGlobalTransformWithCanvas());

    internal static Rect2 TransformRect(
        Rect2 localRect,
        Transform2D transform)
    {
        var result = TransformBounds(
            localRect.Position.X,
            localRect.Position.Y,
            localRect.Size.X,
            localRect.Size.Y,
            transform.X.X,
            transform.X.Y,
            transform.Y.X,
            transform.Y.Y,
            transform.Origin.X,
            transform.Origin.Y);
        return new Rect2(result.X, result.Y, result.Width, result.Height);
    }

    internal static CaptureFloatRect TransformBounds(
        float localX,
        float localY,
        float localWidth,
        float localHeight,
        float basisXX,
        float basisXY,
        float basisYX,
        float basisYY,
        float originX,
        float originY)
    {
        if (localWidth <= 0f || localHeight <= 0f)
            return default;

        static (float X, float Y) TransformPoint(
            float x,
            float y,
            float xx,
            float xy,
            float yx,
            float yy,
            float ox,
            float oy)
            => (xx * x + yx * y + ox, xy * x + yy * y + oy);

        var rightX = localX + localWidth;
        var bottomY = localY + localHeight;
        var topLeft = TransformPoint(
            localX, localY, basisXX, basisXY, basisYX, basisYY, originX, originY);
        var topRight = TransformPoint(
            rightX, localY, basisXX, basisXY, basisYX, basisYY, originX, originY);
        var bottomLeft = TransformPoint(
            localX, bottomY, basisXX, basisXY, basisYX, basisYY, originX, originY);
        var bottomRight = TransformPoint(
            rightX, bottomY, basisXX, basisXY, basisYX, basisYY, originX, originY);
        var left = Math.Min(
            Math.Min(topLeft.X, topRight.X),
            Math.Min(bottomLeft.X, bottomRight.X));
        var top = Math.Min(
            Math.Min(topLeft.Y, topRight.Y),
            Math.Min(bottomLeft.Y, bottomRight.Y));
        var right = Math.Max(
            Math.Max(topLeft.X, topRight.X),
            Math.Max(bottomLeft.X, bottomRight.X));
        var bottom = Math.Max(
            Math.Max(topLeft.Y, topRight.Y),
            Math.Max(bottomLeft.Y, bottomRight.Y));
        return new CaptureFloatRect(left, top, right - left, bottom - top);
    }

    private static Rect2 Merge(Rect2 left, Rect2 right)
    {
        var start = new Vector2(
            Math.Min(left.Position.X, right.Position.X),
            Math.Min(left.Position.Y, right.Position.Y));
        var end = new Vector2(
            Math.Max(left.End.X, right.End.X),
            Math.Max(left.End.Y, right.End.Y));
        return new Rect2(start, end - start);
    }

    private static Rect2 Grow(Rect2 rect, float amount)
        => new(
            rect.Position - new Vector2(amount, amount),
            rect.Size + new Vector2(amount * 2f, amount * 2f));

    internal static Rect2I CalculatePixelRect(
        Rect2 controlRect,
        Rect2 viewportRect,
        Vector2I imageSize)
    {
        var result = CalculatePixelBounds(
            controlRect.Position.X,
            controlRect.Position.Y,
            controlRect.Size.X,
            controlRect.Size.Y,
            viewportRect.Position.X,
            viewportRect.Position.Y,
            viewportRect.Size.X,
            viewportRect.Size.Y,
            imageSize.X,
            imageSize.Y);
        return new Rect2I(result.X, result.Y, result.Width, result.Height);
    }

    internal static CapturePixelRect CalculatePixelBounds(
        float controlX,
        float controlY,
        float controlWidth,
        float controlHeight,
        float viewportX,
        float viewportY,
        float viewportWidth,
        float viewportHeight,
        int imageWidth,
        int imageHeight)
    {
        if (controlWidth <= 0f
            || controlHeight <= 0f
            || viewportWidth <= 0f
            || viewportHeight <= 0f
            || imageWidth <= 0
            || imageHeight <= 0)
        {
            return default;
        }

        var scaleX = imageWidth / viewportWidth;
        var scaleY = imageHeight / viewportHeight;
        var relativeLeft = controlX - viewportX;
        var relativeTop = controlY - viewportY;
        var relativeRight = controlX + controlWidth - viewportX;
        var relativeBottom = controlY + controlHeight - viewportY;

        var left = Math.Clamp(
            (int)MathF.Floor(relativeLeft * scaleX),
            0,
            imageWidth);
        var top = Math.Clamp(
            (int)MathF.Floor(relativeTop * scaleY),
            0,
            imageHeight);
        var right = Math.Clamp(
            (int)MathF.Ceiling(relativeRight * scaleX),
            0,
            imageWidth);
        var bottom = Math.Clamp(
            (int)MathF.Ceiling(relativeBottom * scaleY),
            0,
            imageHeight);

        return new CapturePixelRect(
            left,
            top,
            Math.Max(0, right - left),
            Math.Max(0, bottom - top));
    }
}
