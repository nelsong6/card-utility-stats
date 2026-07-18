using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MegaCrit.Sts2.Core.Models;

namespace SpireLens.Core;

internal sealed class RelicClassificationDocument
{
    [JsonPropertyName("combat")]
    public List<string> Combat { get; set; } = [];

    [JsonPropertyName("non_combat")]
    public List<string> NonCombat { get; set; } = [];

    [JsonPropertyName("combat_relevant_until_turn")]
    public Dictionary<string, int> CombatRelevantUntilTurn { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

internal static class RelicClassificationStore
{
    private const string UserFileUri = "user://SpireLens/relic-classifications.json";
    private const string EmbeddedFileSuffix = "Config.relic-classifications.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private static readonly HashSet<string> CombatIds = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> NonCombatIds = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, int> CombatRelevantUntilTurns =
        new(StringComparer.OrdinalIgnoreCase);
    private static bool _initialized;

    public static string UserFilePath => ProjectSettings.GlobalizePath(UserFileUri);

    public static void Initialize()
    {
        if (_initialized) return;

        var source = File.Exists(UserFilePath)
            ? LoadFromFile(UserFilePath)
            : LoadEmbeddedDefault();
        ApplyDocument(source ?? new RelicClassificationDocument());
        NormalizeAgainstCurrentRelics();
        _initialized = true;

        if (Save())
        {
            CoreMain.Logger.Info(
                $"Relic classifications loaded: combat={CombatIds.Count}, " +
                $"non_combat={NonCombatIds.Count}, finite_combat={CombatRelevantUntilTurns.Count}, " +
                $"file={UserFilePath}");
        }
    }

    public static void Shutdown()
    {
        CombatIds.Clear();
        NonCombatIds.Clear();
        CombatRelevantUntilTurns.Clear();
        _initialized = false;
    }

    public static bool IsNonCombat(RelicModel relicModel)
    {
        if (!_initialized) Initialize();
        return NonCombatIds.Contains(GetRelicId(relicModel));
    }

    public static bool SetNonCombat(RelicModel relicModel, bool isNonCombat)
    {
        if (!_initialized) Initialize();

        var relicId = GetRelicId(relicModel);
        var changed = false;

        if (isNonCombat)
        {
            changed |= CombatIds.Remove(relicId);
            changed |= NonCombatIds.Add(relicId);
            changed |= CombatRelevantUntilTurns.Remove(relicId);
        }
        else
        {
            changed |= NonCombatIds.Remove(relicId);
            changed |= CombatIds.Add(relicId);
        }

        if (!changed) return false;

        Save();
        CoreMain.Logger.Info(
            $"Relic classification changed: {relicId} => " +
            (isNonCombat ? "non-combat" : "combat"));
        Patches.RelicBarFilterPatch.RefreshAll("classification changed");
        return true;
    }

    public static int? GetCombatRelevantUntilTurn(RelicModel relicModel)
    {
        if (!_initialized) Initialize();
        return CombatRelevantUntilTurns.TryGetValue(GetRelicId(relicModel), out var turn)
            ? turn
            : null;
    }

    public static bool SetCombatRelevantUntilTurn(RelicModel relicModel, int? turn)
    {
        if (!_initialized) Initialize();

        var relicId = GetRelicId(relicModel);
        if (!CombatIds.Contains(relicId) || NonCombatIds.Contains(relicId)) return false;

        var normalizedTurn = turn is >= 1 and <= 3 ? turn : null;
        var changed = normalizedTurn.HasValue
            ? !CombatRelevantUntilTurns.TryGetValue(relicId, out var currentTurn)
              || currentTurn != normalizedTurn.Value
            : CombatRelevantUntilTurns.ContainsKey(relicId);
        if (!changed) return false;

        if (normalizedTurn.HasValue)
            CombatRelevantUntilTurns[relicId] = normalizedTurn.Value;
        else
            CombatRelevantUntilTurns.Remove(relicId);

        Save();
        CoreMain.Logger.Info(
            $"Relic combat relevance changed: {relicId} => " +
            (normalizedTurn.HasValue ? $"through turn {normalizedTurn.Value}" : "always"));
        Patches.RelicBarFilterPatch.RefreshAll("combat relevance duration changed");
        return true;
    }

    internal static string GetRelicId(RelicModel relicModel)
    {
        var id = relicModel.Id.ToString();
        return string.IsNullOrWhiteSpace(id)
            ? ModelDb.GetId(relicModel.GetType()).ToString()
            : id;
    }

    private static RelicClassificationDocument? LoadFromFile(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<RelicClassificationDocument>(
                File.ReadAllText(path),
                JsonOptions);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"Could not read relic classifications from {path}: {e.Message}");
            return LoadEmbeddedDefault();
        }
    }

    private static RelicClassificationDocument? LoadEmbeddedDefault()
    {
        try
        {
            var assembly = typeof(RelicClassificationStore).Assembly;
            var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(name =>
                name.EndsWith(EmbeddedFileSuffix, StringComparison.Ordinal));
            if (resourceName == null)
            {
                CoreMain.Logger.Error("Embedded relic classification defaults were not found.");
                return null;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return null;
            return JsonSerializer.Deserialize<RelicClassificationDocument>(stream, JsonOptions);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"Could not read embedded relic classifications: {e.Message}");
            return null;
        }
    }

    private static void ApplyDocument(RelicClassificationDocument document)
    {
        CombatIds.Clear();
        NonCombatIds.Clear();
        CombatRelevantUntilTurns.Clear();

        AddIds(CombatIds, document.Combat);
        AddIds(NonCombatIds, document.NonCombat);
        if (document.CombatRelevantUntilTurn != null)
        {
            foreach (var pair in document.CombatRelevantUntilTurn)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                    CombatRelevantUntilTurns[pair.Key.Trim()] = pair.Value;
            }
        }

        var duplicates = CombatIds.Where(NonCombatIds.Contains).ToArray();
        if (duplicates.Length == 0) return;

        foreach (var duplicate in duplicates)
            CombatIds.Remove(duplicate);
        CoreMain.Logger.Warn(
            $"Relic classification file listed {duplicates.Length} relic(s) in both lists; " +
            "non-combat won and the file was normalized.");
    }

    private static void NormalizeAgainstCurrentRelics()
    {
        try
        {
            var knownIds = ModelDb.AllRelics
                .Select(GetRelicId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (knownIds.Count == 0) return;

            CombatIds.IntersectWith(knownIds);
            NonCombatIds.IntersectWith(knownIds);
            foreach (var relicId in knownIds)
            {
                if (!NonCombatIds.Contains(relicId))
                    CombatIds.Add(relicId);
            }

            var invalidCutoffs = CombatRelevantUntilTurns
                .Where(pair => !CombatIds.Contains(pair.Key) || pair.Value is < 1 or > 3)
                .Select(pair => pair.Key)
                .ToArray();
            foreach (var relicId in invalidCutoffs)
                CombatRelevantUntilTurns.Remove(relicId);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"Could not enumerate current relics for classification: {e.Message}");
        }
    }

    private static void AddIds(ISet<string> destination, IEnumerable<string>? ids)
    {
        if (ids == null) return;
        foreach (var id in ids)
        {
            if (!string.IsNullOrWhiteSpace(id))
                destination.Add(id.Trim());
        }
    }

    private static bool Save()
    {
        try
        {
            var path = UserFilePath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var document = new RelicClassificationDocument
            {
                Combat = CombatIds.OrderBy(id => id, StringComparer.Ordinal).ToList(),
                NonCombat = NonCombatIds.OrderBy(id => id, StringComparer.Ordinal).ToList(),
                CombatRelevantUntilTurn = CombatRelevantUntilTurns
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value,
                        StringComparer.OrdinalIgnoreCase),
            };
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions) + System.Environment.NewLine);
            File.Move(temporaryPath, path, overwrite: true);
            return true;
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"Could not save relic classifications: {e.Message}");
            return false;
        }
    }
}
