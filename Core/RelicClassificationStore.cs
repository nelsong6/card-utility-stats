using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;

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
    private static bool _normalizedAgainstLiveRelics;

    public static string UserFilePath => ProjectSettings.GlobalizePath(UserFileUri);

    public static void Initialize()
    {
        if (_initialized) return;

        var source = File.Exists(UserFilePath)
            ? LoadFromFile(UserFilePath)
            : LoadEmbeddedDefault();
        ApplyDocument(source ?? new RelicClassificationDocument());
        _initialized = true;
        var normalized = TryNormalizeAgainstLiveRelics(out _);

        if (Save())
        {
            CoreMain.Logger.Info(
                $"Relic classifications loaded: combat={CombatIds.Count}, " +
                $"non_combat={NonCombatIds.Count}, finite_combat={CombatRelevantUntilTurns.Count}, " +
                $"file={UserFilePath}" +
                (normalized
                    ? string.Empty
                    : " (normalization deferred — the game's model database is not populated yet)"));
        }
    }

    public static void Shutdown()
    {
        CombatIds.Clear();
        NonCombatIds.Clear();
        CombatRelevantUntilTurns.Clear();
        _initialized = false;
        _normalizedAgainstLiveRelics = false;
    }

    public static bool IsNonCombat(RelicModel relicModel)
    {
        EnsureReady();
        return NonCombatIds.Contains(GetRelicId(relicModel));
    }

    public static bool SetNonCombat(RelicModel relicModel, bool isNonCombat)
    {
        EnsureReady();

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
        EnsureReady();
        return CombatRelevantUntilTurns.TryGetValue(GetRelicId(relicModel), out var turn)
            ? turn
            : null;
    }

    public static bool SetCombatRelevantUntilTurn(RelicModel relicModel, int? turn)
    {
        EnsureReady();

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
            (normalizedTurn.HasValue ? $"until turn {normalizedTurn.Value}" : "always"));
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

    /// <summary>
    /// Brings the store up to date before it answers. Split from
    /// <see cref="Initialize"/> because on a cold start the relic database does
    /// not exist yet when the Core first loads — see
    /// <see cref="IsLiveRelicDatabaseReady"/>.
    /// </summary>
    private static void EnsureReady()
    {
        if (!_initialized) Initialize();
        if (_normalizedAgainstLiveRelics) return;
        if (!TryNormalizeAgainstLiveRelics(out var changed)) return;

        CoreMain.Logger.Info(
            "Relic classifications normalized against the live relic database (deferred from " +
            $"startup): combat={CombatIds.Count}, non_combat={NonCombatIds.Count}, " +
            $"finite_combat={CombatRelevantUntilTurns.Count}, changed={changed}.");
        if (changed) Save();
    }

    /// <summary>
    /// The game populates <c>ModelDb</c> in
    /// <c>OneTimeInitialization.ExecuteEssential</c>, but it loads mods (and so
    /// runs our <c>[ModInitializer]</c>, the Loader, and <c>CoreMain.Initialize</c>)
    /// one step earlier, from <c>ExecuteVeryEarly</c>. Until <c>ModelDb.Init</c>
    /// has run, its backing dictionary is empty and the first thing
    /// <c>ModelDb.AllRelics</c> touches — the characters the relic pools hang off
    /// — throws <c>KeyNotFoundException: The given key 'CHARACTER.IRONCLAD' was
    /// not present in the dictionary</c>.
    ///
    /// A hot reload never sees this, because by then the game is long past
    /// <c>ExecuteEssential</c>. That asymmetry is why a degraded classification
    /// store only ever showed up in real play sessions and never in dev.
    ///
    /// Probing is deliberate: catching the exception instead would cost one
    /// throw per relic on the bar-refresh path for as long as the database
    /// stays empty.
    /// </summary>
    internal static bool IsLiveRelicDatabaseReady() => ModelDb.Contains(typeof(Ironclad));

    /// <summary>
    /// Runs the reconciliation pass if the live relic database is available.
    /// </summary>
    /// <param name="changed">Whether the pass altered the in-memory sets.</param>
    /// <returns>
    /// <c>true</c> when the database was available and the pass ran (even if it
    /// changed nothing); <c>false</c> while the database is still unpopulated,
    /// leaving the store to retry on the next call.
    /// </returns>
    private static bool TryNormalizeAgainstLiveRelics(out bool changed)
    {
        changed = false;
        if (!IsLiveRelicDatabaseReady()) return false;

        // Claim the flag before the work: a genuine enumeration failure must not
        // leave every later read retrying it on the relic-bar refresh path.
        _normalizedAgainstLiveRelics = true;
        try
        {
            var knownIds = ModelDb.AllRelics
                .Select(GetRelicId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (knownIds.Count == 0) return true;

            changed = NormalizeClassifications(
                CombatIds,
                NonCombatIds,
                CombatRelevantUntilTurns,
                knownIds);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"Could not enumerate current relics for classification: {e.Message}");
        }

        return true;
    }

    /// <summary>
    /// Reconciles a loaded document against the relics the game actually
    /// defines: drops ids the game no longer has, defaults every unlisted relic
    /// to combat, and drops finite-combat cutoffs that no longer name a combat
    /// relic or fall outside the supported 1-3 turn range. Kept pure over the
    /// collections it is handed so the rules stay testable without a live game.
    /// </summary>
    internal static bool NormalizeClassifications(
        ISet<string> combatIds,
        ISet<string> nonCombatIds,
        IDictionary<string, int> combatRelevantUntilTurns,
        IReadOnlyCollection<string> knownRelicIds)
    {
        // Rebuilt unconditionally so the membership tests below always match the
        // case-insensitive comparer the classification sets themselves use.
        var knownIds = new HashSet<string>(knownRelicIds, StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var relicId in combatIds.Where(id => !knownIds.Contains(id)).ToArray())
            changed |= combatIds.Remove(relicId);
        foreach (var relicId in nonCombatIds.Where(id => !knownIds.Contains(id)).ToArray())
            changed |= nonCombatIds.Remove(relicId);
        foreach (var relicId in knownIds)
        {
            if (!nonCombatIds.Contains(relicId))
                changed |= combatIds.Add(relicId);
        }

        var invalidCutoffs = combatRelevantUntilTurns
            .Where(pair => !combatIds.Contains(pair.Key) || pair.Value is < 1 or > 3)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var relicId in invalidCutoffs)
            changed |= combatRelevantUntilTurns.Remove(relicId);

        return changed;
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
