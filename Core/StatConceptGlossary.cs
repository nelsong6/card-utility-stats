using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace SpireLens.Core;

internal enum StatConceptDisplayType
{
    GameResource,
    GameResourceGroup,
    EmbeddedImage,
}

internal sealed record StatConceptResource(
    string Path,
    decimal Scale);

internal sealed record StatConceptDisplay(
    StatConceptDisplayType Type,
    string Value,
    string Modulate,
    IReadOnlyList<StatConceptResource> Resources);

internal sealed record StatConcept(
    string Id,
    string Label,
    string Description,
    StatConceptDisplay Display);

internal sealed record StatConceptTexture(
    Texture2D Texture,
    Color Modulate);

/// <summary>
/// Cached vocabulary for reusable stat concepts. Definitions are loaded once
/// per hot-reloaded Core assembly and rendered both in stat rows and in the
/// compendium glossary, keeping the two surfaces from drifting.
/// </summary>
internal static class StatConceptGlossary
{
    private const string EmbeddedFileSuffix = "Config.stat-concepts.json";
    private const int SupportedSchemaVersion = 1;
    internal const int IconSlotSize = 20;
    private const int IconArtworkSize = 16;
    private const string InformationColor = "#94A0AE";
    private const string GeneratedIconDirectory = "user://SpireLens/generated-icons";
    private static readonly string GeneratedIconGeneration =
        Guid.NewGuid().ToString("N");
    private static readonly string[] RelicInlineGameResources =
    [
        "res://images/atlases/potion_atlas.sprites/energy_potion.tres",
        "res://images/atlases/power_atlas.sprites/draw_cards_next_turn_power.tres",
        "res://images/atlases/power_atlas.sprites/vigor_power.tres",
        "res://images/atlases/power_atlas.sprites/vulnerable_power.tres",
        "res://images/atlases/power_atlas.sprites/weak_power.tres",
        "res://images/packed/sprite_fonts/star_icon.png",
        "res://images/ui/combat/block.png",
    ];

    private static readonly IReadOnlyDictionary<string, StatConcept> ConceptsById =
        LoadConcepts();
    private static readonly Dictionary<string, string> GeneratedImagePaths =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ImageTexture> GeneratedImageTextures =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Rect2I> GameResourceRegions =
        new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Texture2D> GlossaryTextures =
        new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<StatConcept> Concepts { get; } =
        ConceptsById.Values
            .OrderBy(concept => concept.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(concept => concept.Id, StringComparer.Ordinal)
            .ToArray();

    public static void Initialize()
    {
        GlossaryTextures.Clear();
        BuildGeneratedConceptImages();
        BuildGameResourceRegions();
        CoreMain.Logger.Info(
            $"Stat concept glossary loaded: concepts={Concepts.Count}, "
            + $"generated_images={GeneratedImagePaths.Count}, "
            + $"slot={IconSlotSize}x{IconSlotSize}, "
            + $"artwork={IconArtworkSize}x{IconArtworkSize}");
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

    public static bool TryGetGlossaryTexture(
        string conceptId,
        out StatConceptTexture display)
    {
        if (!TryGet(conceptId, out var concept))
        {
            display = null!;
            return false;
        }

        if (!GlossaryTextures.TryGetValue(concept.Id, out var texture))
        {
            texture = BuildGlossaryTexture(concept);
            if (texture == null)
            {
                display = null!;
                return false;
            }

            GlossaryTextures.Add(concept.Id, texture);
        }

        display = new StatConceptTexture(
            texture,
            Color.FromHtml(concept.Display.Modulate));
        return true;
    }

    public static string RenderHintedGlyph(string conceptId, int? sizeOverride = null)
    {
        if (!TryGet(conceptId, out var concept))
        {
            var missingId = StatsTooltip.EscapeBbcode(conceptId);
            return $"[hint=\"Unknown stat concept: {EscapeHint(conceptId)}\"]"
                   + $"[font_size={IconSlotSize}][color={InformationColor}][b]?"
                   + $"[/b][/color][/font_size][/hint]"
                   + $"[color={InformationColor}] {missingId}[/color]";
        }

        var size = Math.Clamp(sizeOverride ?? IconSlotSize, 8, 64);
        var rawGlyph = RenderGlyph(concept, size);
        var hint = EscapeHint($"{concept.Label}: {concept.Description}");
        return $"[hint=\"{hint}\"]{rawGlyph}[/hint]";
    }

    public static string RenderInformationHint(string rowDescription)
    {
        var hint = EscapeHint(rowDescription);
        if (!TryGet("information", out var concept))
        {
            return $"[hint=\"{hint}\"][font_size=16][color={InformationColor}]"
                   + "[b]ⓘ[/b][/color][/font_size][/hint]";
        }

        return $"[hint=\"{hint}\"]{RenderGlyph(concept, IconSlotSize)}[/hint]";
    }

    private static string RenderGlyph(StatConcept concept, int size)
    {
        return concept.Display.Type switch
        {
            StatConceptDisplayType.GameResource =>
                RenderGameResource(concept, size),
            StatConceptDisplayType.GameResourceGroup =>
                RenderGeneratedConceptImage(concept, size),
            StatConceptDisplayType.EmbeddedImage =>
                RenderGeneratedConceptImage(concept, size),
            _ => StatsTooltip.EscapeBbcode(concept.Label),
        };
    }

    private static string RenderGameResource(StatConcept concept, int size)
    {
        return RenderImage(
            concept.Display.Value,
            size,
            concept.Display.Modulate);
    }

    private static string RenderGeneratedConceptImage(StatConcept concept, int size)
    {
        var path = GeneratedImagePaths.TryGetValue(concept.Id, out var generatedPath)
            ? generatedPath
            : GetGeneratedImagePath(concept.Id);
        return RenderImage(path, size);
    }

    internal static string RenderImage(string path, int size)
        => RenderImage(path, size, "#FFFFFF");

    private static string RenderImage(string path, int size, string modulate)
    {
        return GameResourceRegions.TryGetValue(path, out var region)
            ? RenderImage(path, size, region, modulate)
            : $"[img width={size} height={size} color={modulate} align=center]"
              + $"{path}[/img]";
    }

    private static string RenderImage(
        string path,
        int size,
        Rect2I region,
        string modulate)
    {
        var regionValue = string.Join(
            ",",
            region.Position.X,
            region.Position.Y,
            region.Size.X,
            region.Size.Y);
        return $"[img width={size} height={size} region={regionValue} "
               + $"color={modulate} align=center]{path}[/img]";
    }

    internal static string RenderInlineImage(string path)
        => RenderImage(path, IconSlotSize);

    private static void BuildGeneratedConceptImages()
    {
        GeneratedImagePaths.Clear();
        GeneratedImageTextures.Clear();

        var generatedConcepts = Concepts
            .Where(concept =>
                concept.Display.Type is StatConceptDisplayType.EmbeddedImage
                    or StatConceptDisplayType.GameResourceGroup)
            .ToArray();
        if (generatedConcepts.Length == 0) return;

        System.IO.Directory.CreateDirectory(
            ProjectSettings.GlobalizePath(GeneratedIconDirectory));

        var assembly = typeof(StatConceptGlossary).Assembly;
        var manifestNames = assembly.GetManifestResourceNames();
        foreach (var concept in generatedConcepts)
        {
            try
            {
                using var sourceImage = concept.Display.Type switch
                {
                    StatConceptDisplayType.EmbeddedImage =>
                        LoadEmbeddedImage(concept, assembly, manifestNames),
                    StatConceptDisplayType.GameResourceGroup =>
                        BuildGroupedSourceImage(concept),
                    _ => throw new InvalidOperationException(
                        $"Concept '{concept.Id}' does not require a generated image."),
                };
                using var normalizedImage = NormalizeToIconSlot(sourceImage);
                SaveGeneratedImage(concept.Id, normalizedImage, concept.Display.Type.ToString());
            }
            catch (Exception e)
            {
                CoreMain.Logger.Error(
                    $"Could not build stat concept image '{concept.Id}': {e.Message}");
            }
        }
    }

    private static Image LoadEmbeddedImage(
        StatConcept concept,
        System.Reflection.Assembly assembly,
        IReadOnlyList<string> manifestNames)
    {
        var manifestName = manifestNames.FirstOrDefault(name =>
            name.EndsWith(concept.Display.Value, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Embedded image '{concept.Display.Value}' was not found.");
        using var stream = assembly.GetManifestResourceStream(manifestName)
            ?? throw new InvalidOperationException(
                $"Embedded image '{manifestName}' could not be opened.");
        using var buffer = new System.IO.MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        var loadError = concept.Display.Value.EndsWith(
            ".svg",
            StringComparison.OrdinalIgnoreCase)
            ? image.LoadSvgFromBuffer(bytes, 1f)
            : image.LoadPngFromBuffer(bytes);
        if (loadError == Error.Ok) return image;

        image.Dispose();
        throw new InvalidOperationException(
            $"Image loader returned {loadError} for '{manifestName}'.");
    }

    private static Image BuildGroupedSourceImage(StatConcept concept)
    {
        var loaded = new List<(Image Image, decimal Scale)>();
        try
        {
            foreach (var resource in concept.Display.Resources)
            {
                var texture = ResourceLoader.Load<Texture2D>(
                    resource.Path,
                    null,
                    ResourceLoader.CacheMode.Reuse)
                    ?? throw new InvalidOperationException(
                        $"ResourceLoader could not load '{resource.Path}'.");
                var image = texture.GetImage();
                EnsureImageReadable(image);
                loaded.Add((image, resource.Scale));
            }

            var targetHeight = loaded.Max(entry =>
                Math.Max(
                    1,
                    (int)Math.Round(
                        entry.Image.GetHeight() * (double)entry.Scale,
                        MidpointRounding.AwayFromZero)));
            var widths = loaded.Select(entry =>
                Math.Max(
                    1,
                    (int)Math.Round(
                        entry.Image.GetWidth() * (double)entry.Scale,
                        MidpointRounding.AwayFromZero)))
                .ToArray();
            var combined = Image.CreateEmpty(
                widths.Sum(),
                targetHeight,
                false,
                Image.Format.Rgba8);
            combined.Fill(new Color(0f, 0f, 0f, 0f));

            var x = 0;
            for (var index = 0; index < loaded.Count; index++)
            {
                var entry = loaded[index];
                var targetWidth = widths[index];
                var targetImageHeight = Math.Max(
                    1,
                    (int)Math.Round(
                        entry.Image.GetHeight() * (double)entry.Scale,
                        MidpointRounding.AwayFromZero));
                entry.Image.Resize(
                    targetWidth,
                    targetImageHeight,
                    Image.Interpolation.Lanczos);
                combined.BlitRect(
                    entry.Image,
                    new Rect2I(0, 0, targetWidth, targetImageHeight),
                    new Vector2I(x, (targetHeight - targetImageHeight) / 2));
                x += targetWidth;
            }

            return combined;
        }
        finally
        {
            foreach (var entry in loaded)
                entry.Image.Dispose();
        }
    }

    private static Image NormalizeToIconSlot(Image source)
    {
        var usedRect = FindVisiblePixelBounds(source, 0.02f);
        if (usedRect.Size.X <= 0 || usedRect.Size.Y <= 0)
        {
            throw new InvalidOperationException(
                "Source image does not contain visible pixels.");
        }

        using var cropped = Image.CreateEmpty(
            usedRect.Size.X,
            usedRect.Size.Y,
            false,
            Image.Format.Rgba8);
        cropped.Fill(new Color(0f, 0f, 0f, 0f));
        cropped.BlitRect(source, usedRect, Vector2I.Zero);

        var scale = Math.Min(
            IconArtworkSize / (double)usedRect.Size.X,
            IconArtworkSize / (double)usedRect.Size.Y);
        var width = Math.Max(
            1,
            (int)Math.Round(usedRect.Size.X * scale, MidpointRounding.AwayFromZero));
        var height = Math.Max(
            1,
            (int)Math.Round(usedRect.Size.Y * scale, MidpointRounding.AwayFromZero));
        cropped.Resize(width, height, Image.Interpolation.Lanczos);

        var normalized = Image.CreateEmpty(
            IconSlotSize,
            IconSlotSize,
            false,
            Image.Format.Rgba8);
        normalized.Fill(new Color(0f, 0f, 0f, 0f));
        normalized.BlitRect(
            cropped,
            new Rect2I(0, 0, width, height),
            new Vector2I(
                (IconSlotSize - width) / 2,
                (IconSlotSize - height) / 2));
        return normalized;
    }

    private static Rect2I FindVisiblePixelBounds(Image image, float alphaThreshold)
    {
        EnsureImageReadable(image);
        var minX = image.GetWidth();
        var minY = image.GetHeight();
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < image.GetHeight(); y++)
        {
            for (var x = 0; x < image.GetWidth(); x++)
            {
                if (image.GetPixel(x, y).A < alphaThreshold) continue;

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return maxX < minX || maxY < minY
            ? new Rect2I()
            : new Rect2I(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static void EnsureImageReadable(Image image)
    {
        if (!image.IsCompressed()) return;

        var error = image.Decompress();
        if (error != Error.Ok)
        {
            throw new InvalidOperationException(
                $"Image.Decompress returned {error}.");
        }
    }

    private static void BuildGameResourceRegions()
    {
        GameResourceRegions.Clear();
        var resources = Concepts
            .Where(concept =>
                concept.Display.Type == StatConceptDisplayType.GameResource)
            .Select(concept => concept.Display.Value)
            .Concat(Concepts
                .Where(concept =>
                    concept.Display.Type == StatConceptDisplayType.GameResourceGroup)
                .SelectMany(concept =>
                    concept.Display.Resources.Select(resource => resource.Path)))
            .Concat(RelicInlineGameResources)
            .Distinct(StringComparer.Ordinal);

        foreach (var path in resources)
        {
            try
            {
                var texture = ResourceLoader.Load<Texture2D>(
                    path,
                    null,
                    ResourceLoader.CacheMode.Reuse)
                    ?? throw new InvalidOperationException(
                        $"ResourceLoader could not load '{path}'.");
                var region = FindSquareContentRegion(texture);
                GameResourceRegions[path] = region;
            }
            catch (Exception e)
            {
                CoreMain.Logger.Error(
                    $"Could not normalize stat concept game resource '{path}': {e.Message}");
            }
        }
    }

    private static Rect2I FindSquareContentRegion(Texture2D texture)
    {
        Rect2I content;
        if (texture is AtlasTexture atlas)
        {
            content = new Rect2I(
                (int)Math.Round(atlas.Margin.Position.X),
                (int)Math.Round(atlas.Margin.Position.Y),
                Math.Max(1, (int)Math.Round(atlas.Region.Size.X)),
                Math.Max(1, (int)Math.Round(atlas.Region.Size.Y)));
        }
        else
        {
            content = new Rect2I(
                0,
                0,
                texture.GetWidth(),
                texture.GetHeight());
        }

        if (content.Size.X <= 0 || content.Size.Y <= 0)
        {
            content = new Rect2I(
                0,
                0,
                texture.GetWidth(),
                texture.GetHeight());
        }

        var contentSide = Math.Max(content.Size.X, content.Size.Y);
        var side = Math.Min(
            Math.Min(texture.GetWidth(), texture.GetHeight()),
            Math.Max(
                contentSide,
                (int)Math.Ceiling(
                    contentSide * (IconSlotSize / (double)IconArtworkSize))));
        var centerX = content.Position.X + (content.Size.X / 2d);
        var centerY = content.Position.Y + (content.Size.Y / 2d);
        var left = Math.Clamp(
            (int)Math.Round(centerX - (side / 2d)),
            0,
            Math.Max(0, texture.GetWidth() - side));
        var top = Math.Clamp(
            (int)Math.Round(centerY - (side / 2d)),
            0,
            Math.Max(0, texture.GetHeight() - side));
        return new Rect2I(left, top, side, side);
    }

    private static Texture2D? BuildGlossaryTexture(StatConcept concept)
    {
        if (concept.Display.Type is StatConceptDisplayType.EmbeddedImage
            or StatConceptDisplayType.GameResourceGroup)
        {
            return GeneratedImageTextures.TryGetValue(concept.Id, out var generated)
                ? generated
                : null;
        }

        if (concept.Display.Type != StatConceptDisplayType.GameResource)
            return null;

        var source = ResourceLoader.Load<Texture2D>(
            concept.Display.Value,
            null,
            ResourceLoader.CacheMode.Reuse);
        if (source == null)
        {
            CoreMain.Logger.Error(
                $"Could not load glossary texture '{concept.Display.Value}'.");
            return null;
        }

        if (!GameResourceRegions.TryGetValue(concept.Display.Value, out var region))
            return source;

        return new AtlasTexture
        {
            Atlas = source,
            Region = new Rect2(
                region.Position.X,
                region.Position.Y,
                region.Size.X,
                region.Size.Y),
            FilterClip = true,
        };
    }

    private static void SaveGeneratedImage(string conceptId, Image image, string sourceKind)
    {
        var texture = ImageTexture.CreateFromImage(image);
        var path = GetGeneratedImagePath(conceptId);
        texture.TakeOverPath(path);
        GeneratedImageTextures.Add(conceptId, texture);
        GeneratedImagePaths.Add(conceptId, path);
        CoreMain.Logger.Info(
            $"Stat concept generated image loaded: id={conceptId}, "
            + $"source={sourceKind}, size={texture.GetWidth()}x{texture.GetHeight()}");
    }

    private static string GetGeneratedImagePath(string conceptId)
    {
        return $"{GeneratedIconDirectory}/{conceptId}-{GeneratedIconGeneration}.tres";
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

        if (string.Equals(type, "game_resource", StringComparison.Ordinal))
        {
            RequireOnlyProperties(
                element,
                $"{id}.display",
                "type",
                "path",
                "modulate");
            var path = RequireString(element, "path", $"{id}.display");
            if (!path.StartsWith("res://", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Stat concept '{id}' game resource must use a res:// path.");
            }

            return new StatConceptDisplay(
                StatConceptDisplayType.GameResource,
                path,
                ReadOptionalColor(element, "modulate", "#FFFFFF", $"{id}.display"),
                Array.Empty<StatConceptResource>());
        }

        if (string.Equals(type, "game_resource_group", StringComparison.Ordinal))
        {
            RequireOnlyProperties(
                element,
                $"{id}.display",
                "type",
                "resources");
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
                "#FFFFFF",
                resources);
        }

        if (string.Equals(type, "embedded_image", StringComparison.Ordinal))
        {
            RequireOnlyProperties(
                element,
                $"{id}.display",
                "type",
                "resource");
            var resource = RequireString(element, "resource", $"{id}.display");

            return new StatConceptDisplay(
                StatConceptDisplayType.EmbeddedImage,
                resource,
                "#FFFFFF",
                Array.Empty<StatConceptResource>());
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

    private static string ReadOptionalColor(
        JsonElement element,
        string propertyName,
        string defaultValue,
        string context)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return defaultValue;
        if (property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidOperationException(
                $"Stat concept glossary '{context}.{propertyName}' "
                + "must be a nonblank color.");
        }

        var color = property.GetString()!.Trim();
        if (!IsHexColor(color))
        {
            throw new InvalidOperationException(
                $"Stat concept glossary '{context}.{propertyName}' "
                + $"must be #RRGGBB or #RRGGBBAA, got '{color}'.");
        }

        return color;
    }

    private static bool IsHexColor(string value)
    {
        return value.Length is 7 or 9
               && value[0] == '#'
               && value.Skip(1).All(Uri.IsHexDigit);
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
