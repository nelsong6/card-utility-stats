using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.Core.Runs;
using SpireLens.Core.Patches;

namespace SpireLens.Core;

internal static class RunHistoryStatsContext
{
    private const string EnthralledDefinitionId = "CARD.ENTHRALLED";
    private const string CursedPearlCurseDefinitionId = "CARD.GREED";
    private const string BrightestFlameDefinitionId = "CARD.BRIGHTEST_FLAME";

    private static readonly FieldInfo? DeckHistoryAmountField =
        AccessTools.Field(typeof(NDeckHistoryEntry), "_amount");

    private static LoadedRunFile? _loaded;
    private static long? _gameStartTime;
    private static bool _loadAttempted;
    private static NDeckViewScreen? _historicalDeckViewer;
    private static readonly Dictionary<CardModel, string> HistoricalDeckKeys =
        new(ReferenceEqualityComparer.Instance);

    public static bool HasCurrent => EnsureLoaded()?.Data != null;

    internal static RunData? GetCurrentRunData() => EnsureLoaded()?.Data;

    public static void SetHistoricalDeckViewer(
        NDeckViewScreen viewer,
        IReadOnlyList<CardModel> cards)
    {
        _historicalDeckViewer = viewer;
        HistoricalDeckKeys.Clear();

        var run = EnsureLoaded()?.Data;
        if (run == null) return;

        var keys = SelectAggregateKeysForHistoricalDeck(
            run,
            cards.Select(card => card.Id.ToString()));
        for (var i = 0; i < cards.Count && i < keys.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(keys[i]))
                HistoricalDeckKeys[cards[i]] = keys[i]!;
        }
    }

    public static void ClearHistoricalDeckViewer()
    {
        _historicalDeckViewer = null;
        HistoricalDeckKeys.Clear();
    }

    public static bool TryBuildHistoricalDeckCardHoverTip(
        CardModel card,
        out HoverTip statsTip)
    {
        statsTip = default;
        if (_historicalDeckViewer == null
            || !GodotObject.IsInstanceValid(_historicalDeckViewer)
            || !HistoricalDeckKeys.TryGetValue(card, out var key))
        {
            return false;
        }

        var run = EnsureLoaded()?.Data;
        if (run == null || !run.Aggregates.TryGetValue(key, out var aggregate))
            return false;

        var title = BuildCardDisplayName(card, new[] { key }, amount: 1);
        var body = CardHoverShowPatch.BuildHistoricalBodyBBCode(
            card,
            aggregate,
            run.MetaStats,
            GetPermanentUpgradeEvents(run, key));
        statsTip = StatsTooltip.CreateNativeTip(title, body);
        return true;
    }

    public static bool TryBuildNativeCardHoverTip(
        NDeckHistoryEntry entry,
        out HoverTip statsTip)
    {
        statsTip = default;
        if (!CardHoverShowPatch.ResolveCardStatsEnabled(
                ViewStatsInjectorPatch.StatsVisibilityEnabled,
                ViewStatsInjectorPatch.CardStatsEnabled))
        {
            return false;
        }

        if (!HasCurrent || !TryBuildCardTooltip(entry, out var title, out var body))
            return false;

        statsTip = StatsTooltip.CreateNativeTip(title, body);
        return true;
    }

    public static bool TryBuildNativeRelicHoverTip(
        NRelicBasicHolder holder,
        out HoverTip statsTip)
    {
        statsTip = default;
        if (!ViewStatsInjectorPatch.StatsVisibilityEnabled
            || !HasCurrent
            || !HasAncestor<NRelicHistory>(holder)
            || !TryBuildRelicTooltip(holder, out var title, out var body))
        {
            return false;
        }

        statsTip = StatsTooltip.CreateNativeTip(
            title,
            body,
            stretchHorizontally:
                RelicHoverShowPatch.ShouldStretchStatsTooltip(
                    holder.Relic?.Model,
                    body));
        return true;
    }

    public static void SetRun(RunHistory? history)
    {
        if (history == null)
        {
            Clear();
            return;
        }

        if (_gameStartTime == history.StartTime)
            return;

        _gameStartTime = history.StartTime;
        _loaded = null;
        _loadAttempted = false;
    }

    private static LoadedRunFile? EnsureLoaded()
    {
        if (_loadAttempted || !_gameStartTime.HasValue)
            return _loaded;

        _loadAttempted = true;
        _loaded = RunStorage.FindHistoricalByGameStartTime(_gameStartTime.Value);
        if (_loaded?.Data != null)
        {
            CoreMain.LogDebug(
                $"RunHistoryStatsContext: loaded SpireLens run '{_loaded.Data.RunId}' for history start_time={_gameStartTime.Value}");
        }
        else
        {
            CoreMain.LogDebug(
                $"RunHistoryStatsContext: no SpireLens run found for history start_time={_gameStartTime.Value}");
        }

        return _loaded;
    }

    public static void Clear()
    {
        ClearHistoricalDeckViewer();
        _loaded = null;
        _gameStartTime = null;
        _loadAttempted = false;
    }

    public static bool TryBuildCardTooltip(
        NDeckHistoryEntry entry,
        out string title,
        out string body)
    {
        title = "";
        body = "";

        var run = EnsureLoaded()?.Data;
        if (entry == null) return false;

        var card = entry.Card;
        if (run == null || card == null) return false;

        var definitionId = card.Id.ToString();
        var amount = GetHistoryEntryAmount(entry);
        var floorsAdded = entry.FloorsAddedToDeck?
            .Where(f => f > 0)
            .Distinct()
            .OrderBy(f => f)
            .ToArray() ?? Array.Empty<int>();

        var keys = SelectAggregateKeysForHistoryEntry(
            run,
            definitionId,
            amount,
            card.CurrentUpgradeLevel,
            floorsAdded);

        var aggregate = CombineCardAggregates(run, keys);
        if (keys.Count == 0 && floorsAdded.Length == 1)
            aggregate.FloorAdded = floorsAdded[0];

        title = BuildCardDisplayName(card, keys, amount);
        var upgradeEvents = keys.Count == 0
            ? Enumerable.Empty<CardEvent>()
            : keys
                .SelectMany(key => GetPermanentUpgradeEvents(run, key))
                .OrderBy(e => e.Floor ?? int.MaxValue)
                .ThenBy(e => e.T)
                .ToList();

        body = CardHoverShowPatch.BuildHistoricalBodyBBCode(
            card,
            aggregate,
            run.MetaStats,
            upgradeEvents);
        return true;
    }

    public static bool TryBuildRelicTooltip(
        NRelicBasicHolder holder,
        out string title,
        out string body)
    {
        title = "";
        body = "";

        var run = EnsureLoaded()?.Data;
        var relicModel = holder?.Relic?.Model;
        if (run == null || relicModel == null) return false;

        var relicId = RelicHoverShowPatch.GetStatsAggregateId(relicModel);
        var aggregate = run.RelicAggregates.TryGetValue(relicId, out var saved)
            ? saved
            : new RelicAggregate();
        var bloodSoakedRoseCurseAgg = relicModel is BloodSoakedRose
            ? CardAggregatePooler.PoolByDefinition(run.Aggregates, EnthralledDefinitionId) ?? new CardAggregate()
            : null;
        var cursedPearlCurseAgg = relicModel is CursedPearl
            ? CardAggregatePooler.PoolByDefinition(run.Aggregates, CursedPearlCurseDefinitionId) ?? new CardAggregate()
            : null;
        var storybookBrightestFlameAgg = relicModel is Storybook
            ? CardAggregatePooler.PoolByDefinition(run.Aggregates, BrightestFlameDefinitionId) ?? new CardAggregate()
            : null;
        IReadOnlyDictionary<string, CardAggregate>? neowsBonesCurseAggs = null;
        if (relicModel is NeowsBones)
        {
            var curseAggs = new Dictionary<string, CardAggregate>(StringComparer.Ordinal);
            foreach (var card in aggregate.CardsGranted.Values)
            {
                if (card.Count <= 0 || string.IsNullOrWhiteSpace(card.CardId)) continue;
                curseAggs[card.CardId] =
                    CardAggregatePooler.PoolByDefinition(run.Aggregates, card.CardId) ?? new CardAggregate();
            }

            neowsBonesCurseAggs = curseAggs;
        }

        var built = RelicHoverShowPatch.TryBuildBodyBBCode(
            relicModel,
            aggregate,
            run.FloorReached,
            bloodSoakedRoseCurseAgg,
            cursedPearlCurseAgg,
            neowsBonesCurseAggs,
            storybookBrightestFlameAgg,
            out title,
            out body);
        if (relicModel.IsWax)
        {
            var toyBoxAggregate = run.RelicAggregates.TryGetValue(
                "RELIC.TOY_BOX",
                out var toyBoxSaved)
                ? toyBoxSaved
                : null;
            RelicHoverShowPatch.ApplyWaxRelicPresentation(
                relicModel,
                toyBoxAggregate,
                ref title,
                ref body);
            return true;
        }

        return built;
    }

    public static bool HasAncestor<T>(Node? node) where T : Node
    {
        for (var current = node; current != null; current = current.GetParent())
        {
            if (current is T) return true;
        }

        return false;
    }

    internal static IReadOnlyList<string> SelectAggregateKeysForHistoryEntry(
        RunData run,
        string definitionId,
        int amount,
        int? currentUpgradeLevel,
        IReadOnlyCollection<int>? floorsAdded)
    {
        if (run.Aggregates.ContainsKey(definitionId))
            return new[] { definitionId };

        var ordered = GetPerInstanceKeysForDefinition(run, definitionId);
        if (ordered.Count == 0)
            return Array.Empty<string>();

        var candidates = ordered;
        var floorFiltered = FilterByFloors(run, candidates, floorsAdded);
        if (floorFiltered.Count > 0)
            candidates = floorFiltered;

        var upgradeFiltered = FilterByFinalUpgrade(run, candidates, currentUpgradeLevel);
        if (upgradeFiltered.Count > 0)
            candidates = upgradeFiltered;

        var take = Math.Max(1, amount);
        if (candidates.Count < take && !ReferenceEquals(candidates, ordered))
        {
            var expanded = candidates.ToList();
            foreach (var key in ordered)
            {
                if (expanded.Count >= take) break;
                if (!expanded.Contains(key))
                    expanded.Add(key);
            }
            candidates = expanded;
        }

        return candidates.Take(take).ToArray();
    }

    internal static IReadOnlyList<string?> SelectAggregateKeysForHistoricalDeck(
        RunData run,
        IEnumerable<string> definitionIds)
    {
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var perDefinitionKeys = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.Ordinal);
        var result = new List<string?>();

        foreach (var definitionId in definitionIds)
        {
            occurrences.TryGetValue(definitionId, out var occurrence);
            occurrences[definitionId] = occurrence + 1;

            if (run.Aggregates.ContainsKey(definitionId))
            {
                result.Add(definitionId);
                continue;
            }

            if (!perDefinitionKeys.TryGetValue(definitionId, out var keys))
            {
                keys = GetPerInstanceKeysForDefinition(run, definitionId);
                perDefinitionKeys[definitionId] = keys;
            }

            result.Add(occurrence < keys.Count ? keys[occurrence] : null);
        }

        return result;
    }

    private static List<string> GetPerInstanceKeysForDefinition(RunData run, string definitionId)
    {
        var result = new List<string>();
        if (run.InstanceNumbersByDef.TryGetValue(definitionId, out var numbers))
        {
            foreach (var number in numbers)
            {
                var key = $"{definitionId}#{number}";
                if (run.Aggregates.ContainsKey(key))
                    result.Add(key);
            }
        }

        if (result.Count > 0)
            return result;

        return run.Aggregates.Keys
            .Select(key => TryParseAggregateKey(key, out var def, out var number)
                ? (Key: key, DefinitionId: def, Number: number)
                : (Key: key, DefinitionId: "", Number: 0))
            .Where(item => string.Equals(item.DefinitionId, definitionId, StringComparison.Ordinal))
            .OrderBy(item => item.Number)
            .Select(item => item.Key)
            .ToList();
    }

    private static List<string> FilterByFloors(
        RunData run,
        IReadOnlyList<string> keys,
        IReadOnlyCollection<int>? floorsAdded)
    {
        if (floorsAdded == null || floorsAdded.Count == 0)
            return new List<string>();

        var floors = floorsAdded.ToHashSet();
        return keys
            .Where(key => run.Aggregates.TryGetValue(key, out var agg)
                && agg.FloorAdded.HasValue
                && floors.Contains(agg.FloorAdded.Value))
            .ToList();
    }

    private static List<string> FilterByFinalUpgrade(
        RunData run,
        IReadOnlyList<string> keys,
        int? currentUpgradeLevel)
    {
        if (!currentUpgradeLevel.HasValue)
            return new List<string>();

        return keys
            .Where(key => run.Aggregates.TryGetValue(key, out var agg)
                && GetFinalUpgradeLevel(run, key, agg) == currentUpgradeLevel.Value)
            .ToList();
    }

    private static int GetFinalUpgradeLevel(RunData run, string key, CardAggregate agg)
    {
        var level = agg.InitialUpgradeLevel;
        foreach (var e in GetPermanentUpgradeEvents(run, key))
        {
            level = e.UpgradeLevel!.Value;
        }

        return level;
    }

    private static IReadOnlyList<CardEvent> GetPermanentUpgradeEvents(
        RunData run,
        string key)
    {
        var initialUpgradeLevel = run.Aggregates.TryGetValue(key, out var agg)
            ? agg.InitialUpgradeLevel
            : 0;
        return RunTracker.FilterPermanentUpgradeEvents(
            run.Events.Where(e =>
                string.Equals(e.Type, "card_upgraded", StringComparison.Ordinal)
                && string.Equals(e.CardId, key, StringComparison.Ordinal)),
            initialUpgradeLevel);
    }

    private static CardAggregate CombineCardAggregates(RunData run, IReadOnlyList<string> keys)
    {
        CardAggregate? combined = null;
        foreach (var key in keys)
        {
            if (!run.Aggregates.TryGetValue(key, out var agg))
                continue;

            if (combined == null)
                combined = RunTracker.CloneAggregate(agg);
            else
                RunTracker.MergeAggregateInto(combined, agg);
        }

        return combined ?? new CardAggregate();
    }

    private static int GetHistoryEntryAmount(NDeckHistoryEntry entry)
    {
        try
        {
            if (DeckHistoryAmountField?.GetValue(entry) is int amount && amount > 0)
                return amount;
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RunHistoryStatsContext.GetHistoryEntryAmount failed: {e.Message}");
        }

        return 1;
    }

    private static string BuildCardDisplayName(
        CardModel card,
        IReadOnlyList<string> keys,
        int amount)
    {
        var rawTitle = card.Title;
        if (card.CurrentUpgradeLevel > 0 && !string.IsNullOrEmpty(rawTitle))
            rawTitle = rawTitle.TrimEnd('+').TrimEnd();

        var title = !string.IsNullOrWhiteSpace(rawTitle)
            ? rawTitle
            : card.Id.ToString();

        if (amount > 1)
            return $"{title} x{amount}";

        if (keys.Count == 1 && TryParseAggregateKey(keys[0], out _, out var number))
            return $"{title} #{number}";

        return title;
    }

    private static bool TryParseAggregateKey(string key, out string definitionId, out int number)
    {
        definitionId = "";
        number = 0;

        var hash = key.LastIndexOf('#');
        if (hash <= 0 || hash == key.Length - 1)
            return false;

        if (!int.TryParse(key[(hash + 1)..], out number))
            return false;

        definitionId = key[..hash];
        return !string.IsNullOrWhiteSpace(definitionId);
    }
}

[HarmonyPatch(typeof(NRunHistory), "DisplayRun")]
public static class RunHistoryDisplayRunStatsContextPatch
{
    [HarmonyPostfix]
    public static void Postfix(NRunHistory __instance, RunHistory history)
    {
        PatchGuard.Run(nameof(RunHistoryDisplayRunStatsContextPatch), () =>
        {
            RunHistoryDeckViewer.Close();
            StatsTooltipPinManager.ClearPin();
            RunHistoryStatsContext.SetRun(history);
            StatsTooltipPinManager.AttachRunHistoryTargets(__instance);
            RunHistoryDeckViewer.InjectButton(__instance);
            RunHistoryDeckViewer.RestoreVisibleArrowHotkeys(__instance);
            var player = __instance._selectedPlayerIcon?.Player;
            if (player != null)
            {
                RunHistoryCampfireSummary.Refresh(__instance, history, player);
                RunHistoryHpTooltip.Refresh(__instance, player);
                RunHistoryGoldTooltip.Refresh(__instance, player);
            }
        });
    }
}

[HarmonyPatch(
    typeof(NRunHistory),
    "SelectPlayer",
    new[] { typeof(NRunHistoryPlayerIcon) })]
public static class RunHistorySelectPlayerCampfireSummaryPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        NRunHistory __instance,
        NRunHistoryPlayerIcon playerIcon)
    {
        PatchGuard.Run(nameof(RunHistorySelectPlayerCampfireSummaryPatch), () =>
        {
            if (__instance._history != null && playerIcon?.Player != null)
            {
                StatsTooltipPinManager.ClearPin();
                RunHistoryCampfireSummary.Refresh(
                    __instance,
                    __instance._history,
                    playerIcon.Player);
                RunHistoryHpTooltip.Refresh(__instance, playerIcon.Player);
                RunHistoryGoldTooltip.Refresh(__instance, playerIcon.Player);
            }
        });
    }
}

[HarmonyPatch(typeof(NRunHistory), "OnSubmenuHidden")]
public static class RunHistoryHiddenStatsContextPatch
{
    [HarmonyPostfix]
    public static void Postfix(NRunHistory __instance)
    {
        PatchGuard.Run(nameof(RunHistoryHiddenStatsContextPatch), () =>
        {
            RunHistoryDeckViewer.Close();
            RunHistoryDeckViewer.DisableArrowHotkeys(__instance);
            RunHistoryCampfireSummary.Remove(__instance);
            RunHistoryHpTooltip.Remove(__instance);
            RunHistoryGoldTooltip.Remove(__instance);
            StatsTooltipPinManager.ClearPin();
            RunHistoryStatsContext.Clear();
        });
    }
}
