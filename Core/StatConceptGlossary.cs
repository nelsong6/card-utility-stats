using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace SpireLens.Core;

internal enum StatConceptDisplayType
{
    StyledText,
    GameResource,
    GameResourceGroup,
    GameResourceOverlay,
}

internal sealed record StatConceptResource(
    string Path,
    decimal Scale);

internal enum StatConceptOverlayAnchor
{
    LowerRight,
}

internal sealed record StatConceptOverlay(
    string BasePath,
    string OverlayPath,
    decimal OverlayScale,
    StatConceptOverlayAnchor Anchor);

internal sealed record StatConceptDisplay(
    StatConceptDisplayType Type,
    string Value,
    string Color,
    bool Bold,
    int Size,
    IReadOnlyList<StatConceptResource> Resources,
    StatConceptOverlay? Overlay);

internal sealed record StatConcept(
    string Id,
    string Label,
    string Description,
    StatConceptDisplay Display);

/// <summary>
/// Cached vocabulary for reusable stat concepts. Definitions are loaded once
/// per hot-reloaded Core assembly and rendered both in stat rows and in the
/// compendium glossary, keeping the two surfaces from drifting.
/// </summary>
internal static class StatConceptGlossary
{
    private const string EmbeddedFileSuffix = "Config.stat-concepts.json";
    private const int SupportedSchemaVersion = 1;
    private const int DefaultGlyphSize = 20;
    private const string InformationColor = "#94A0AE";
    private const string GeneratedIconDirectory = "user://SpireLens/generated-icons";

    private static readonly IReadOnlyDictionary<string, StatConcept> ConceptsById =
        LoadConcepts();
    private static readonly Dictionary<string, string> GeneratedOverlayPaths =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<ImageTexture> GeneratedOverlayTextures = [];

    public static IReadOnlyList<StatConcept> Concepts { get; } =
        ConceptsById.Values
            .OrderBy(concept => concept.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(concept => concept.Id, StringComparer.Ordinal)
            .ToArray();

    public static void Initialize()
    {
        BuildOverlayResources();
        CoreMain.Logger.Info(
            $"Stat concept glossary loaded: concepts={Concepts.Count}, "
            + $"overlays={GeneratedOverlayPaths.Count}");
    }

    public static bool TryGet(string conceptId, out StatConcept concept)
    {
        if (string.IsNullOrWhiteSpace(conceptId))
        {
            concept = null!;
            return false;
        }

        if (ConceptsById.TryGetValue(conceptId, out var found))
        {
            concept = found;
            return true;
        }

        concept = null!;
        return false;
    }

    public static string RenderHintedGlyph(string conceptId, int? sizeOverride = null)
    {
        if (!TryGet(conceptId, out var concept))
        {
            var missingId = StatsTooltip.EscapeBbcode(conceptId);
            return $"[hint=\"Unknown stat concept: {EscapeHint(conceptId)}\"]"
                   + $"[font_size={DefaultGlyphSize}][color={InformationColor}][b]?"
                   + $"[/b][/color][/font_size][/hint]"
                   + $"[color={InformationColor}] {missingId}[/color]";
        }

        var size = Math.Clamp(sizeOverride ?? concept.Display.Size, 8, 64);
        var rawGlyph = concept.Display.Type switch
        {
            StatConceptDisplayType.StyledText =>
                RenderStyledText(concept.Display, size),
            StatConceptDisplayType.GameResource =>
                $"[img={size}x{size}]{concept.Display.Value}[/img]",
            StatConceptDisplayType.GameResourceGroup =>
                RenderGameResourceGroup(concept.Display, size),
            StatConceptDisplayType.GameResourceOverlay =>
                RenderGameResourceOverlay(concept, size),
            _ => StatsTooltip.EscapeBbcode(concept.Label),
        };
        var hint = EscapeHint($"{concept.Label}: {concept.Description}");
        return $"[hint=\"{hint}\"]{rawGlyph}[/hint]";
    }

    public static string RenderInformationHint(string rowDescription)
    {
        var hint = EscapeHint(rowDescription);
        return $"[hint=\"{hint}\"][font_size=16][color={InformationColor}]"
               + "[b]ⓘ[/b][/color][/font_size][/hint]";
    }

    private static string RenderStyledText(StatConceptDisplay display, int size)
    {
        var value = StatsTooltip.EscapeBbcode(display.Value);
        var styled = display.Bold ? $"[b]{value}[/b]" : value;
        return $"[font_size={size}][color={display.Color}]{styled}[/color][/font_size]";
    }

    private static string RenderGameResourceGroup(StatConceptDisplay display, int size)
    {
        return string.Concat(display.Resources.Select(resource =>
        {
            var resourceSize = Math.Clamp(
                (int)Math.Round(size * resource.Scale, MidpointRounding.AwayFromZero),
                8,
                64);
            return $"[img={resourceSize}x{resourceSize}]{resource.Path}[/img]";
        }));
    }

    private static string RenderGameResourceOverlay(StatConcept concept, int size)
    {
        var overlay = concept.Display.Overlay
            ?? throw new InvalidOperationException(
                $"Stat concept '{concept.Id}' has no overlay definition.");
        var path = GeneratedOverlayPaths.TryGetValue(concept.Id, out var generatedPath)
            ? generatedPath
            : overlay.BasePath;
        return $"[img={size}x{size}]{path}[/img]";
    }

    private static void BuildOverlayResources()
    {
        GeneratedOverlayPaths.Clear();
        GeneratedOverlayTextures.Clear();

        var overlayConcepts = Concepts
            .Where(concept => concept.Display.Type == StatConceptDisplayType.GameResourceOverlay)
            .ToArray();
        if (overlayConcepts.Length == 0) return;

        System.IO.Directory.CreateDirectory(
            ProjectSettings.GlobalizePath(GeneratedIconDirectory));
        foreach (var concept in overlayConcepts)
        {
            try
            {
                var overlay = concept.Display.Overlay
                    ?? throw new InvalidOperationException(
                        $"Stat concept '{concept.Id}' has no overlay definition.");
                var texture = CreateOverlayTexture(concept.Id, overlay);
                var path = $"{GeneratedIconDirectory}/{concept.Id}.tres";
                var saveError = ResourceSaver.Save(
                    texture,
                    path,
                    ResourceSaver.SaverFlags.ChangePath);
                if (saveError != Error.Ok)
                {
                    texture.Dispose();
                    throw new InvalidOperationException(
                        $"ResourceSaver returned {saveError} for '{path}'.");
                }

                texture.TakeOverPath(path);
                GeneratedOverlayTextures.Add(texture);
                GeneratedOverlayPaths.Add(concept.Id, path);
                CoreMain.Logger.Info(
                    $"Stat concept overlay generated: id={concept.Id}, "
                    + $"size={texture.GetWidth()}x{texture.GetHeight()}");
            }
            catch (Exception e)
            {
                CoreMain.Logger.Error(
                    $"Could not build stat concept overlay '{concept.Id}': {e.Message}");
            }
        }
    }

    private static ImageTexture CreateOverlayTexture(
        string conceptId,
        StatConceptOverlay overlay)
    {
        var baseTexture = ResourceLoader.Load<Texture2D>(
            overlay.BasePath,
            null,
            ResourceLoader.CacheMode.Reuse);
        var badgeTexture = ResourceLoader.Load<Texture2D>(
            overlay.OverlayPath,
            null,
            ResourceLoader.CacheMode.Reuse);
        if (baseTexture == null)
        {
            throw new InvalidOperationException(
                $"Could not load base texture '{overlay.BasePath}'.");
        }

        if (badgeTexture == null)
        {
            throw new InvalidOperationException(
                $"Could not load overlay texture '{overlay.OverlayPath}'.");
        }

        using var baseImage = ExtractTextureImage(baseTexture);
        using var badgeImage = ExtractTextureImage(badgeTexture);
        if (baseImage.IsEmpty() || badgeImage.IsEmpty())
        {
            throw new InvalidOperationException(
                $"One or more textures for '{conceptId}' returned an empty image.");
        }

        baseImage.Convert(Image.Format.Rgba8);
        badgeImage.Convert(Image.Format.Rgba8);
        var canvasSize = Math.Max(baseImage.GetWidth(), baseImage.GetHeight());
        using var canvas = Image.CreateEmpty(
            canvasSize,
            canvasSize,
            false,
            Image.Format.Rgba8);
        canvas.Fill(Colors.Transparent);

        var baseDestination = new Vector2I(
            (canvasSize - baseImage.GetWidth()) / 2,
            Math.Max(0, (canvasSize - baseImage.GetHeight()) / 6));
        canvas.BlendRect(
            baseImage,
            new Rect2I(Vector2I.Zero, baseImage.GetSize()),
            baseDestination);

        var badgeSize = Math.Clamp(
            (int)Math.Round(
                canvasSize * overlay.OverlayScale,
                MidpointRounding.AwayFromZero),
            1,
            canvasSize);
        badgeImage.Resize(
            badgeSize,
            badgeSize,
            Image.Interpolation.Lanczos);
        var badgeDestination = overlay.Anchor switch
        {
            StatConceptOverlayAnchor.LowerRight =>
                new Vector2I(canvasSize - badgeSize, canvasSize - badgeSize),
            _ => throw new InvalidOperationException(
                $"Unsupported overlay anchor '{overlay.Anchor}'."),
        };
        canvas.BlendRect(
            badgeImage,
            new Rect2I(Vector2I.Zero, badgeImage.GetSize()),
            badgeDestination);

        return ImageTexture.CreateFromImage(canvas);
    }

    private static Image ExtractTextureImage(Texture2D texture)
    {
        if (texture is not AtlasTexture atlasTexture)
            return texture.GetImage();

        var atlas = atlasTexture.Atlas
            ?? throw new InvalidOperationException(
                $"Atlas texture '{texture.ResourcePath}' has no source atlas.");
        using var atlasImage = ExtractTextureImage(atlas);
        var region = atlasTexture.Region;
        var sourceRect = new Rect2I(
            new Vector2I(
                (int)Math.Round(region.Position.X, MidpointRounding.AwayFromZero),
                (int)Math.Round(region.Position.Y, MidpointRounding.AwayFromZero)),
            new Vector2I(
                (int)Math.Round(region.Size.X, MidpointRounding.AwayFromZero),
                (int)Math.Round(region.Size.Y, MidpointRounding.AwayFromZero)));
        using var cropped = atlasImage.GetRegion(sourceRect);

        var margin = atlasTexture.Margin;
        var left = Math.Max(
            0,
            (int)Math.Round(margin.Position.X, MidpointRounding.AwayFromZero));
        var top = Math.Max(
            0,
            (int)Math.Round(margin.Position.Y, MidpointRounding.AwayFromZero));
        var right = Math.Max(
            0,
            (int)Math.Round(margin.Size.X, MidpointRounding.AwayFromZero));
        var bottom = Math.Max(
            0,
            (int)Math.Round(margin.Size.Y, MidpointRounding.AwayFromZero));
        var result = Image.CreateEmpty(
            cropped.GetWidth() + left + right,
            cropped.GetHeight() + top + bottom,
            false,
            Image.Format.Rgba8);
        result.Fill(Colors.Transparent);
        cropped.Convert(Image.Format.Rgba8);
        result.BlendRect(
            cropped,
            new Rect2I(Vector2I.Zero, cropped.GetSize()),
            new Vector2I(left, top));
        return result;
    }

    private static string EscapeHint(string? text)
    {
        return StatsTooltip.EscapeBbcode(text)
            .Replace('"', '”')
            .Replace('\r', ' ')
            .Replace('\n', ' ');
    }

    private static IReadOnlyDictionary<string, StatConcept> LoadConcepts()
    {
        try
        {
            var assembly = typeof(StatConceptGlossary).Assembly;
            var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(name =>
                name.EndsWith(EmbeddedFileSuffix, StringComparison.Ordinal));
            if (resourceName == null)
                throw new InvalidOperationException("Embedded stat concept glossary was not found.");

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    "Embedded stat concept glossary could not be opened.");
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            RequireObject(root, "Stat concept glossary root");
            RequireOnlyProperties(root, "root", "schema_version", "concepts");

            var schemaVersion = RequireInt(root, "schema_version", "root");
            if (schemaVersion != SupportedSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Stat concept glossary schema version {schemaVersion} is unsupported.");
            }

            var conceptsElement = RequireProperty(root, "concepts", "root");
            RequireObject(conceptsElement, "Stat concept glossary 'concepts'");

            var properties = conceptsElement.EnumerateObject().ToArray();
            var ids = properties.Select(property => property.Name).ToArray();
            if (!ids.SequenceEqual(ids.OrderBy(id => id, StringComparer.Ordinal), StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    "Stat concept glossary concept IDs must be alphabetically sorted.");
            }

            var concepts = new Dictionary<string, StatConcept>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var property in properties)
            {
                var id = property.Name.Trim();
                if (string.IsNullOrWhiteSpace(id))
                    throw new InvalidOperationException("Stat concept ID cannot be blank.");
                if (!concepts.TryAdd(id, ParseConcept(id, property.Value)))
                {
                    throw new InvalidOperationException(
                        $"Stat concept glossary contains duplicate ID '{id}'.");
                }
            }

            return concepts;
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"Could not read embedded stat concept glossary: {e.Message}");
            return new Dictionary<string, StatConcept>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static StatConcept ParseConcept(string id, JsonElement element)
    {
        RequireObject(element, $"Stat concept '{id}'");
        RequireOnlyProperties(element, id, "display", "help");

        var displayElement = RequireProperty(element, "display", id);
        var helpElement = RequireProperty(element, "help", id);
        var display = ParseDisplay(id, displayElement);

        RequireObject(helpElement, $"Stat concept '{id}' help");
        RequireOnlyProperties(helpElement, $"{id}.help", "label", "description");
        var label = RequireString(helpElement, "label", $"{id}.help");
        var description = RequireString(helpElement, "description", $"{id}.help");
        return new StatConcept(id, label, description, display);
    }

    private static StatConceptDisplay ParseDisplay(string id, JsonElement element)
    {
        RequireObject(element, $"Stat concept '{id}' display");
        var type = RequireString(element, "type", $"{id}.display");
        var size = RequireInt(element, "size", $"{id}.display");
        if (size is < 8 or > 64)
        {
            throw new InvalidOperationException(
                $"Stat concept '{id}' display size must be between 8 and 64.");
        }

        if (string.Equals(type, "styled_text", StringComparison.Ordinal))
        {
            RequireOnlyProperties(
                element,
                $"{id}.display",
                "type",
                "value",
                "color",
                "bold",
                "size");
            var value = RequireString(element, "value", $"{id}.display");
            var color = RequireString(element, "color", $"{id}.display");
            if (!IsHexColor(color))
            {
                throw new InvalidOperationException(
                    $"Stat concept '{id}' color '{color}' must be #RRGGBB or #RRGGBBAA.");
            }

            var boldElement = RequireProperty(element, "bold", $"{id}.display");
            if (boldElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            {
                throw new InvalidOperationException(
                    $"Stat concept '{id}' display 'bold' must be a boolean.");
            }

            return new StatConceptDisplay(
                StatConceptDisplayType.StyledText,
                value,
                color,
                boldElement.GetBoolean(),
                size,
                Array.Empty<StatConceptResource>(),
                null);
        }

        if (string.Equals(type, "game_resource", StringComparison.Ordinal))
        {
            RequireOnlyProperties(
                element,
                $"{id}.display",
                "type",
                "path",
                "size");
            var path = RequireString(element, "path", $"{id}.display");
            if (!path.StartsWith("res://", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Stat concept '{id}' game resource must use a res:// path.");
            }

            return new StatConceptDisplay(
                StatConceptDisplayType.GameResource,
                path,
                string.Empty,
                false,
                size,
                Array.Empty<StatConceptResource>(),
                null);
        }

        if (string.Equals(type, "game_resource_group", StringComparison.Ordinal))
        {
            RequireOnlyProperties(
                element,
                $"{id}.display",
                "type",
                "resources",
                "size");
            var resourcesElement = RequireProperty(element, "resources", $"{id}.display");
            if (resourcesElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    $"Stat concept '{id}' display 'resources' must be an array.");
            }

            var resources = resourcesElement.EnumerateArray()
                .Select((resource, index) => ParseResource(id, resource, index))
                .ToArray();
            if (resources.Length < 2)
            {
                throw new InvalidOperationException(
                    $"Stat concept '{id}' game resource group must contain at least two resources.");
            }

            return new StatConceptDisplay(
                StatConceptDisplayType.GameResourceGroup,
                string.Empty,
                string.Empty,
                false,
                size,
                resources,
                null);
        }

        if (string.Equals(type, "game_resource_overlay", StringComparison.Ordinal))
        {
            RequireOnlyProperties(
                element,
                $"{id}.display",
                "type",
                "base",
                "overlay",
                "size");
            var baseElement = RequireProperty(element, "base", $"{id}.display");
            RequireObject(baseElement, $"Stat concept '{id}' display base");
            RequireOnlyProperties(baseElement, $"{id}.display.base", "path");
            var basePath = RequireResourcePath(
                baseElement,
                "path",
                $"{id}.display.base");

            var overlayElement = RequireProperty(element, "overlay", $"{id}.display");
            RequireObject(overlayElement, $"Stat concept '{id}' display overlay");
            RequireOnlyProperties(
                overlayElement,
                $"{id}.display.overlay",
                "path",
                "scale",
                "anchor");
            var overlayPath = RequireResourcePath(
                overlayElement,
                "path",
                $"{id}.display.overlay");
            var scaleElement = RequireProperty(
                overlayElement,
                "scale",
                $"{id}.display.overlay");
            if (scaleElement.ValueKind != JsonValueKind.Number
                || !scaleElement.TryGetDecimal(out var overlayScale)
                || overlayScale is < 0.25m or > 1m)
            {
                throw new InvalidOperationException(
                    $"Stat concept '{id}.display.overlay.scale' must be between 0.25 and 1.");
            }

            var anchorValue = RequireString(
                overlayElement,
                "anchor",
                $"{id}.display.overlay");
            var anchor = anchorValue switch
            {
                "lower_right" => StatConceptOverlayAnchor.LowerRight,
                _ => throw new InvalidOperationException(
                    $"Stat concept '{id}' has unsupported overlay anchor '{anchorValue}'."),
            };

            return new StatConceptDisplay(
                StatConceptDisplayType.GameResourceOverlay,
                string.Empty,
                string.Empty,
                false,
                size,
                Array.Empty<StatConceptResource>(),
                new StatConceptOverlay(
                    basePath,
                    overlayPath,
                    overlayScale,
                    anchor));
        }

        throw new InvalidOperationException(
            $"Stat concept '{id}' has unsupported display type '{type}'.");
    }

    private static StatConceptResource ParseResource(
        string id,
        JsonElement element,
        int index)
    {
        var context = $"{id}.display.resources[{index}]";
        RequireObject(element, $"Stat concept '{context}'");
        RequireOnlyProperties(element, context, "path", "scale");

        var path = RequireString(element, "path", context);
        if (!path.StartsWith("res://", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Stat concept '{id}' game resource must use a res:// path.");
        }

        var scaleElement = RequireProperty(element, "scale", context);
        if (scaleElement.ValueKind != JsonValueKind.Number
            || !scaleElement.TryGetDecimal(out var scale)
            || scale is < 0.25m or > 1m)
        {
            throw new InvalidOperationException(
                $"Stat concept '{context}.scale' must be between 0.25 and 1.");
        }

        return new StatConceptResource(path, scale);
    }

    private static string RequireResourcePath(
        JsonElement element,
        string propertyName,
        string context)
    {
        var path = RequireString(element, propertyName, context);
        if (!path.StartsWith("res://", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Stat concept glossary '{context}.{propertyName}' must use a res:// path.");
        }

        return path;
    }

    private static bool IsHexColor(string value)
    {
        if (value.Length is not 7 and not 9 || value[0] != '#') return false;
        return value.Skip(1).All(Uri.IsHexDigit);
    }

    private static void RequireObject(JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"{context} must be an object.");
    }

    private static JsonElement RequireProperty(
        JsonElement element,
        string propertyName,
        string context)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            throw new InvalidOperationException(
                $"Stat concept glossary '{context}' is missing '{propertyName}'.");
        }

        return property;
    }

    private static string RequireString(
        JsonElement element,
        string propertyName,
        string context)
    {
        var property = RequireProperty(element, propertyName, context);
        if (property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidOperationException(
                $"Stat concept glossary '{context}.{propertyName}' must be a nonblank string.");
        }

        return property.GetString()!.Trim();
    }

    private static int RequireInt(
        JsonElement element,
        string propertyName,
        string context)
    {
        var property = RequireProperty(element, propertyName, context);
        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var value))
        {
            throw new InvalidOperationException(
                $"Stat concept glossary '{context}.{propertyName}' must be an integer.");
        }

        return value;
    }

    private static void RequireOnlyProperties(
        JsonElement element,
        string context,
        params string[] allowedNames)
    {
        var allowed = new HashSet<string>(allowedNames, StringComparer.Ordinal);
        var unknown = element.EnumerateObject()
            .Where(property => !allowed.Contains(property.Name))
            .Select(property => property.Name)
            .ToArray();
        if (unknown.Length > 0)
        {
            throw new InvalidOperationException(
                $"Stat concept glossary '{context}' has unknown field(s): "
                + string.Join(", ", unknown));
        }
    }
}
