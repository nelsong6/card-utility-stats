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
/// saved continue-run start time to load the matching in-progress run file.
/// </summary>
[HarmonyPatch(typeof(NRelicCollectionEntry), "OnFocus")]
public static class RelicCompendiumStatsShowPatch
{
    private static readonly FieldInfo? RelicField =
        AccessTools.Field(typeof(NRelicCollectionEntry), "relic");

    [HarmonyPostfix]
    public static void Postfix(NRelicCollectionEntry __instance)
    {
        PatchGuard.Run(nameof(RelicCompendiumStatsShowPatch), () =>
        {
            if (!CompendiumRelicStatsContext.ShouldShowStatsForVisibility(__instance.ModelVisibility))
                return;

            if (RelicField?.GetValue(__instance) is not RelicModel relicModel)
                return;

            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null) return;

            if (!CompendiumRelicStatsContext.TryBuildRelicTooltip(relicModel, out var title, out var body))
                return;

            StatsTooltip.Show(tree, __instance, title, "SpireLens", body);
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

    public static bool ShouldShowStatsForVisibility(ModelVisibility visibility)
        => visibility == ModelVisibility.Visible;

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

        if (!MainMenuContinueRunStatsContext.TryGetCurrentRun(out var run))
            return false;

        return TryBuildRelicTooltipForRun(relicModel, run, out title, out body);
    }

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

        if (_gameStartTime == startTime) return;

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

        if (_cachedRun != null && _cachedGameStartTime == gameStartTime)
        {
            run = _cachedRun;
            return true;
        }

        var loaded = RunStorage.FindByGameStartTime(
            gameStartTime.Value,
            out _,
            requireInProgress: true);
        if (loaded == null) return false;

        _cachedRun = loaded;
        _cachedGameStartTime = gameStartTime;
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
