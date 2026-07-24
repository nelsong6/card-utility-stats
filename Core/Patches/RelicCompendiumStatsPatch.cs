using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.RelicCollection;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace SpireLens.Core.Patches;

[HarmonyPatch(typeof(NRelicCollectionEntry), "_Ready")]
public static class RelicCompendiumStatsReadyPatch
{
    [HarmonyPostfix]
    public static void Postfix(NRelicCollectionEntry __instance)
    {
        PatchGuard.Run(nameof(RelicCompendiumStatsReadyPatch), () =>
        {
            RelicCompendiumFilterUi.ApplyToEntry(__instance);
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

    public static bool TryBuildNativeHoverTip(
        NRelicCollectionEntry? entry,
        out HoverTip statsTip)
    {
        statsTip = default;
        if (entry == null || !GodotObject.IsInstanceValid(entry)) return false;
        if (!ShouldShowStats(
                ViewStatsInjectorPatch.StatsVisibilityEnabled,
                entry.ModelVisibility)) return false;
        if (!TryGetRelicModel(entry, out var relicModel)) return false;

        if (!TryBuildRelicTooltip(relicModel, out var title, out var body))
            return false;

        statsTip = StatsTooltip.CreateNativeTip(
            title,
            body,
            stretchHorizontally:
                RelicHoverShowPatch.GetPreferredStatsTooltipWidth(relicModel).HasValue);
        return true;
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
