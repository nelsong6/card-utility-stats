using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.RelicCollection;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace SpireLens.Core.Patches;

/// <summary>
/// Adds SpireLens relic stat hovers to the relic compendium. In-run hovers use
/// the live tracker, including pending combat data; main-menu hovers use the
/// saved continue-run start time to load the matching in-progress run file,
/// or render the tracked relic's empty stat layout when no run is available.
/// </summary>
[HarmonyPatch(typeof(NRelicCollectionEntry), "OnFocus")]
public static class RelicCompendiumStatsShowPatch
{
    [HarmonyPostfix]
    public static void Postfix(NRelicCollectionEntry __instance)
    {
        PatchGuard.Run(nameof(RelicCompendiumStatsShowPatch), () =>
        {
            CompendiumRelicStatsContext.ShowForEntry(__instance);
        });
    }
}

[HarmonyPatch(typeof(NRelicCollectionEntry), "_Ready")]
public static class RelicCompendiumStatsReadyPatch
{
    [HarmonyPostfix]
    public static void Postfix(NRelicCollectionEntry __instance)
    {
        PatchGuard.Run(nameof(RelicCompendiumStatsReadyPatch), () =>
        {
            RelicCompendiumStatsSignals.Attach(__instance);
            RelicCompendiumFilterUi.ApplyToEntry(__instance);
        });
    }
}

[HarmonyPatch(typeof(NRelicCollectionEntry), "OnUnfocus")]
public static class RelicCompendiumStatsHidePatch
{
    [HarmonyPostfix]
    public static void Postfix(NRelicCollectionEntry __instance)
    {
        PatchGuard.Run(nameof(RelicCompendiumStatsHidePatch), () =>
        {
            StatsTooltip.HideIfAnchoredTo(__instance);
        });
    }
}

[HarmonyPatch(typeof(NContinueRunInfo), "ShowInfo")]
public static class MainMenuContinueRunStatsContextShowInfoPatch
{
    [HarmonyPostfix]
    public static void Postfix(SerializableRun save)
    {
        PatchGuard.Run(nameof(MainMenuContinueRunStatsContextShowInfoPatch), () =>
        {
            MainMenuContinueRunStatsContext.SetContinueRun(save);
        });
    }
}

[HarmonyPatch(typeof(NContinueRunInfo), "ShowError")]
public static class MainMenuContinueRunStatsContextShowErrorPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        PatchGuard.Run(nameof(MainMenuContinueRunStatsContextShowErrorPatch), MainMenuContinueRunStatsContext.Clear);
    }
}

internal static class CompendiumRelicStatsContext
{
    private const string EnthralledDefinitionId = "CARD.ENTHRALLED";
    private const string CursedPearlCurseDefinitionId = "CARD.GREED";
    private const string BrightestFlameDefinitionId = "CARD.BRIGHTEST_FLAME";
    private static readonly RelicAggregate EmptyRelicAggregate = new();
    private static readonly Lazy<FieldInfo?> RelicField = new(
        () => AccessTools.Field(typeof(NRelicCollectionEntry), "relic"));

    public static bool ShouldShowStatsForVisibility(ModelVisibility visibility)
        => visibility == ModelVisibility.Visible;

    public static bool ShouldShowStats(bool statsVisibilityEnabled, ModelVisibility visibility)
        => statsVisibilityEnabled && ShouldShowStatsForVisibility(visibility);

    public static void ShowForEntry(NRelicCollectionEntry? entry)
    {
        if (entry == null || !GodotObject.IsInstanceValid(entry)) return;
        if (!ShouldShowStats(
                ViewStatsInjectorPatch.StatsVisibilityEnabled,
                entry.ModelVisibility)) return;
        if (!TryGetRelicModel(entry, out var relicModel)) return;

        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null) return;

        if (!TryBuildRelicTooltip(relicModel, out var title, out var body))
            return;

        StatsTooltip.Show(
            tree,
            entry,
            title,
            "SpireLens",
            body,
            panelWidth: RelicHoverShowPatch.GetPreferredStatsTooltipWidth(relicModel));
    }

    internal static bool TryGetRelicModel(
        NRelicCollectionEntry? entry,
        out RelicModel relicModel)
    {
        relicModel = null!;
        if (entry == null || !GodotObject.IsInstanceValid(entry)) return false;
        if (RelicField.Value?.GetValue(entry) is not RelicModel found) return false;

        relicModel = found;
        return true;
    }

    public static bool TryBuildRelicTooltip(
        RelicModel relicModel,
        out string title,
        out string body)
    {
        title = "";
        body = "";
        if (relicModel == null) return false;

        if (RunTracker.Current != null)
            return RelicHoverShowPatch.TryBuildInventoryBodyBBCode(null, relicModel, out title, out body);

        if (MainMenuContinueRunStatsContext.TryGetCurrentRun(out var run))
            return TryBuildRelicTooltipForRun(relicModel, run, out title, out body);

        return TryBuildEmptyRelicTooltip(relicModel, out title, out body);
    }

    internal static bool TryBuildEmptyRelicTooltip(
        RelicModel relicModel,
        out string title,
        out string body)
        => RelicHoverShowPatch.TryBuildBodyBBCode(
            relicModel,
            EmptyRelicAggregate,
            floorCount: null,
            out title,
            out body);

    internal static bool TryBuildRelicTooltipForRun(
        RelicModel relicModel,
        RunData run,
        out string title,
        out string body)
    {
        title = "";
        body = "";
        if (relicModel == null || run == null) return false;

        var relicId = RelicHoverShowPatch.GetStatsAggregateId(relicModel);
        var aggregate = CloneRelicAggregate(run.RelicAggregates.TryGetValue(relicId, out var saved)
            ? saved
            : null);

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
            neowsBonesCurseAggs = BuildGrantedCurseAggregates(run, aggregate);

        return RelicHoverShowPatch.TryBuildBodyBBCode(
            relicModel,
            aggregate,
            run.FloorReached,
            bloodSoakedRoseCurseAgg,
            cursedPearlCurseAgg,
            neowsBonesCurseAggs,
            storybookBrightestFlameAgg,
            out title,
            out body);
    }

    private static RelicAggregate CloneRelicAggregate(RelicAggregate? source)
    {
        var result = new RelicAggregate();
        if (source != null)
            RunTracker.MergeRelicAggregateInto(result, source);
        return result;
    }

    private static IReadOnlyDictionary<string, CardAggregate> BuildGrantedCurseAggregates(
        RunData run,
        RelicAggregate aggregate)
    {
        var result = new Dictionary<string, CardAggregate>(StringComparer.Ordinal);
        foreach (var card in aggregate.CardsGranted.Values)
        {
            if (card.Count <= 0 || string.IsNullOrWhiteSpace(card.CardId)) continue;
            result[card.CardId] =
                CardAggregatePooler.PoolByDefinition(run.Aggregates, card.CardId) ?? new CardAggregate();
        }

        return result;
    }
}

internal static class RelicCompendiumStatsSignals
{
    private static readonly List<AttachedHandlers> AttachedEntries = new();

    public static void Attach(NRelicCollectionEntry? entry)
    {
        if (entry == null || !GodotObject.IsInstanceValid(entry)) return;
        CleanupInvalidHandlers();

        foreach (var handlers in AttachedEntries)
        {
            if (handlers.IsFor(entry))
                return;
        }

        var attached = new AttachedHandlers(entry);
        attached.Attach();
        AttachedEntries.Add(attached);
    }

    public static void Detach(NRelicCollectionEntry? entry)
    {
        if (entry == null) return;

        for (var i = AttachedEntries.Count - 1; i >= 0; i--)
        {
            var handlers = AttachedEntries[i];
            if (!handlers.IsFor(entry)) continue;

            handlers.Detach();
            AttachedEntries.RemoveAt(i);
        }
    }

    public static void ReattachToActiveEntries()
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null) return;

            foreach (var entry in FindEntries(tree.Root))
                Attach(entry);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"RelicCompendiumStatsSignals.ReattachToActiveEntries failed: {e}");
        }
    }

    public static void TeardownAttachedSignals()
    {
        foreach (var handlers in AttachedEntries.ToArray())
            handlers.Detach();
        AttachedEntries.Clear();
    }

    private static void CleanupInvalidHandlers()
    {
        for (var i = AttachedEntries.Count - 1; i >= 0; i--)
        {
            if (AttachedEntries[i].IsValid) continue;
            AttachedEntries[i].Detach();
            AttachedEntries.RemoveAt(i);
        }
    }

    private static IEnumerable<NRelicCollectionEntry> FindEntries(Node? node)
    {
        if (node == null) yield break;
        if (node is NRelicCollectionEntry entry && GodotObject.IsInstanceValid(entry))
            yield return entry;

        var count = node.GetChildCount();
        for (var i = 0; i < count; i++)
        {
            foreach (var childEntry in FindEntries(node.GetChild(i)))
                yield return childEntry;
        }
    }

    private sealed class AttachedHandlers
    {
        private readonly NRelicCollectionEntry _entry;
        private bool _attached;

        public AttachedHandlers(NRelicCollectionEntry entry)
        {
            _entry = entry;
        }

        public bool IsValid => GodotObject.IsInstanceValid(_entry);

        public bool IsFor(NRelicCollectionEntry entry) => ReferenceEquals(_entry, entry);

        public void Attach()
        {
            if (_attached || !IsValid) return;

            _entry.MouseEntered += OnMouseEntered;
            _entry.MouseExited += OnMouseExited;
            _entry.TreeExiting += OnTreeExiting;
            _attached = true;
        }

        public void Detach()
        {
            if (!_attached) return;
            _attached = false;

            if (!IsValid) return;
            try { _entry.MouseEntered -= OnMouseEntered; }
            catch { }
            try { _entry.MouseExited -= OnMouseExited; }
            catch { }
            try { _entry.TreeExiting -= OnTreeExiting; }
            catch { }
        }

        private void OnMouseEntered()
        {
            PatchGuard.Run("RelicCompendiumStatsSignals.MouseEntered", () =>
            {
                CompendiumRelicStatsContext.ShowForEntry(_entry);
            });
        }

        private void OnMouseExited()
        {
            PatchGuard.Run("RelicCompendiumStatsSignals.MouseExited", () =>
            {
                StatsTooltip.HideIfAnchoredTo(_entry);
            });
        }

        private void OnTreeExiting()
        {
            PatchGuard.Run("RelicCompendiumStatsSignals.TreeExiting", () =>
            {
                RelicCompendiumStatsSignals.Detach(_entry);
            });
        }
    }
}

internal static class MainMenuContinueRunStatsContext
{
    private static long? _gameStartTime;
    private static RunData? _cachedRun;
    private static long? _cachedGameStartTime;

    public static void SetContinueRun(SerializableRun? save)
    {
        var startTime = save?.StartTime ?? 0;
        if (startTime <= 0)
        {
            Clear();
            return;
        }

        _gameStartTime = startTime;
        _cachedRun = null;
        _cachedGameStartTime = null;
    }

    public static void Clear()
    {
        _gameStartTime = null;
        _cachedRun = null;
        _cachedGameStartTime = null;
    }

    public static bool TryGetCurrentRun(out RunData run)
    {
        run = null!;
        var gameStartTime = _gameStartTime ?? TryGetLiveGameStartTime();
        if (!gameStartTime.HasValue) return false;

        if (_cachedGameStartTime == gameStartTime)
        {
            if (_cachedRun == null) return false;

            run = _cachedRun;
            return true;
        }

        var loaded = RunStorage.FindByGameStartTime(
            gameStartTime.Value,
            out _,
            requireInProgress: true);

        // Cache misses as well as hits so each hover after an ended run does
        // not repeat the bounded on-disk run search for the same start time.
        _cachedRun = loaded;
        _cachedGameStartTime = gameStartTime;
        if (loaded == null) return false;

        run = loaded;
        return true;
    }

    private static long? TryGetLiveGameStartTime()
    {
        try
        {
            var startTime = RunManager.Instance?._startTime ?? 0;
            return startTime > 0 ? startTime : null;
        }
        catch
        {
            return null;
        }
    }
}
