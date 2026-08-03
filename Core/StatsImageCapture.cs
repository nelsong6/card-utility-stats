using System;
using System.Collections.Generic;
using Godot;

namespace SpireLens.Core;

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
        if (localRect.Size.X <= 0f || localRect.Size.Y <= 0f)
            return new Rect2();

        var topLeft = transform * localRect.Position;
        var topRight = transform * new Vector2(
            localRect.End.X,
            localRect.Position.Y);
        var bottomLeft = transform * new Vector2(
            localRect.Position.X,
            localRect.End.Y);
        var bottomRight = transform * localRect.End;
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
        return new Rect2(left, top, right - left, bottom - top);
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
