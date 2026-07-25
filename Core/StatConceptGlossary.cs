using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SpireLens.Core;

internal enum StatConceptDisplayType
{
    StyledText,
    GameResource,
}

internal sealed record StatConceptDisplay(
    StatConceptDisplayType Type,
    string Value,
    string Color,
    bool Bold,
    int Size);

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

    private static readonly IReadOnlyDictionary<string, StatConcept> ConceptsById =
        LoadConcepts();

    public static IReadOnlyList<StatConcept> Concepts { get; } =
        ConceptsById.Values
            .OrderBy(concept => concept.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(concept => concept.Id, StringComparer.Ordinal)
            .ToArray();

    public static void Initialize()
    {
        CoreMain.Logger.Info($"Stat concept glossary loaded: concepts={Concepts.Count}");
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
                size);
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
                size);
        }

        throw new InvalidOperationException(
            $"Stat concept '{id}' has unsupported display type '{type}'.");
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
