using System.Globalization;
using System.Collections.Generic;
using System.Text;
using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace SpireLens.Core.Patches;

/// <summary>
/// Run summaries appended to the game's six native map-legend hover tips.
/// The legend item keeps ownership of focus/unfocus and native positioning;
/// SpireLens only clamps the resulting stack to the visible viewport.
/// </summary>
internal static class MapLegendStatsTooltip
{
    private const float ViewportMargin = 8f;

    internal static bool TryBuildNativeHoverTip(
        NMapLegendItem owner,
        out HoverTip tip)
    {
        tip = default;
        if (owner == null
            || !RunTracker.TryGetEffectiveMapLegendStats(
                out var mapStats,
                out var potionHistory,
                out var maxHpHistory))
        {
            return false;
        }

        var pointType = owner._pointType;
        var category = GetCategory(mapStats, pointType);
        if (category == null) return false;

        var locationKinds = LocationKinds(pointType);
        var potionsOffered = Math.Max(
            category.PotionsOffered,
            potionHistory.Count(entry =>
                entry != null && MatchesLocation(entry.SeenLocationKind, locationKinds)));
        var potionsGained = Math.Max(
            category.PotionsGained,
            potionHistory.Count(entry =>
                entry != null
                && entry.Acquired
                && MatchesLocation(entry.AcquiredLocationKind, locationKinds)));
        var maxHpGained = maxHpHistory
            .Where(entry => entry != null && MatchesLocation(entry.LocationKind, locationKinds))
            .Sum(entry => Math.Max(0, entry.NewMaxHp - entry.PreviousMaxHp));
        var maxHpLost = maxHpHistory
            .Where(entry => entry != null && MatchesLocation(entry.LocationKind, locationKinds))
            .Sum(entry => Math.Max(0, entry.PreviousMaxHp - entry.NewMaxHp));

        tip = StatsTooltip.CreateNativeTip(
            Title(pointType),
            BuildBodyBBCode(
                pointType,
                category,
                potionsOffered,
                potionsGained,
                maxHpGained,
                maxHpLost),
            stretchHorizontally: true);
        return true;
    }

    /// <summary>
    /// Native map-legend tips are anchored below the legend sheet. Once the
    /// appended stats page makes that stack wider or taller, the native anchor
    /// can leave part of it outside the viewport. Clamp the measured text-tip
    /// container now and once more after Godot's deferred layout pass.
    /// </summary>
    internal static void KeepInsideViewport(
        NMapLegendItem owner,
        NHoverTipSet tipSet)
    {
        ApplyViewportBounds(owner, tipSet);
        Callable.From(() =>
        {
            if (IsLive(owner) && IsLive(tipSet))
                ApplyViewportBounds(owner, tipSet);
        }).CallDeferred();
    }

    private static void ApplyViewportBounds(
        NMapLegendItem owner,
        NHoverTipSet tipSet)
    {
        var container = tipSet._textHoverTipContainer;
        if (!IsLive(owner) || !IsLive(container)) return;

        var viewportRect = owner.GetViewport()?.GetVisibleRect() ?? default;
        if (viewportRect.Size.X <= 0f || viewportRect.Size.Y <= 0f) return;

        var size = container.Size;
        if (size.X <= 0f || size.Y <= 0f) return;

        container.GlobalPosition = ClampInsideViewport(
            container.GlobalPosition,
            size,
            viewportRect);
    }

    internal static Vector2 ClampInsideViewport(
        Vector2 position,
        Vector2 size,
        Rect2 viewportRect)
    {
        var result = ClampInsideViewportBounds(
            position.X,
            position.Y,
            size.X,
            size.Y,
            viewportRect.Position.X,
            viewportRect.Position.Y,
            viewportRect.Size.X,
            viewportRect.Size.Y);

        return new Vector2(result.X, result.Y);
    }

    internal static CaptureFloatPoint ClampInsideViewportBounds(
        float positionX,
        float positionY,
        float width,
        float height,
        float viewportX,
        float viewportY,
        float viewportWidth,
        float viewportHeight)
    {
        var minimumX = viewportX + ViewportMargin;
        var minimumY = viewportY + ViewportMargin;
        var maximumX = Math.Max(
            minimumX,
            viewportX + viewportWidth - ViewportMargin - width);
        var maximumY = Math.Max(
            minimumY,
            viewportY + viewportHeight - ViewportMargin - height);

        return new CaptureFloatPoint(
            Math.Clamp(positionX, minimumX, maximumX),
            Math.Clamp(positionY, minimumY, maximumY));
    }

    private static bool IsLive(GodotObject? value)
        => value != null && GodotObject.IsInstanceValid(value);

    internal static string BuildBodyBBCode(
        MapPointType pointType,
        MapLegendCategoryStats? category,
        int potionsOffered,
        int potionsGained,
        int maxHpGained,
        int maxHpLost)
    {
        category ??= new MapLegendCategoryStats();
        var body = new StringBuilder();

        AppendRow(
            body,
            [LegendConcept(pointType)],
            [],
            VisitLabel(pointType),
            category.Visits);
        AppendRow(
            body,
            ["average", "floor"],
            ["floor"],
            $"Avg floors between {VisitNoun(pointType)}",
            Divide(category.FloorsBetweenVisitsTotal, category.FloorsBetweenVisitsSamples));

        if (pointType == MapPointType.Unknown)
        {
            AppendRow(body, ["unknown_room"], [], "Events found", category.ResolvedEvents);
            AppendRow(body, ["combat"], [], "Combats found", category.ResolvedCombats);
            AppendRow(body, ["elite"], [], "Elites found", category.ResolvedElites);
            AppendRow(body, ["shop"], [], "Merchants found", category.ResolvedShops);
            AppendRow(body, ["relic"], [], "Treasure rooms found", category.ResolvedTreasures);
            AppendRow(body, ["campfire"], [], "Rest sites found", category.ResolvedRestSites);
        }

        if (pointType is MapPointType.Monster or MapPointType.Elite)
        {
            var combatConcept = pointType == MapPointType.Elite ? "elite" : "combat";
            AppendRow(body, [combatConcept], [], "Combats completed", category.CombatsCompleted);
            AppendRow(
                body,
                [combatConcept, "kill"],
                [],
                pointType == MapPointType.Elite ? "Elites slain" : "Combats won",
                category.CombatsWon);
            AppendRow(
                body,
                ["average", "kill"],
                [],
                "Win rate",
                Percent(category.CombatsWon, category.CombatsCompleted));
            AppendRow(
                body,
                [combatConcept, "healing_gained"],
                [],
                "Perfect combats",
                category.PerfectCombats);
            AppendRow(
                body,
                ["average", "turn", combatConcept],
                [combatConcept],
                "Avg turns per combat",
                Divide(category.CombatTurns, category.CombatsCompleted));
        }

        if (category.HpLost != 0m
            || pointType is MapPointType.Unknown or MapPointType.Monster or MapPointType.Elite)
        {
            AppendRow(body, ["damage"], [], "HP lost", category.HpLost);
        }

        if (pointType is MapPointType.Monster or MapPointType.Elite)
        {
            AppendRow(
                body,
                ["average", "damage", pointType == MapPointType.Elite ? "elite" : "combat"],
                [pointType == MapPointType.Elite ? "elite" : "combat"],
                "Avg HP lost per combat",
                Divide(category.HpLost, category.CombatsCompleted));
        }

        if (category.HpHealed != 0m
            || pointType is MapPointType.Shop or MapPointType.RestSite
                or MapPointType.Monster or MapPointType.Elite)
        {
            AppendRow(body, ["healing_gained"], [], "HP restored", category.HpHealed);
        }

        if (pointType == MapPointType.RestSite)
        {
            AppendRow(
                body,
                ["average", "healing_gained", "campfire"],
                ["campfire"],
                "Avg HP restored per rest site",
                Divide(category.HpHealed, category.Visits));
        }

        if (category.CardsUpgraded != 0 || pointType == MapPointType.RestSite)
            AppendRow(body, ["upgraded"], [], "Cards upgraded", category.CardsUpgraded);

        if (category.GoldGained != 0
            || pointType is MapPointType.Unknown or MapPointType.Shop
                or MapPointType.Treasure or MapPointType.Monster or MapPointType.Elite)
        {
            AppendRow(body, ["gold_gained"], [], "Gold gained", category.GoldGained);
        }

        if (pointType is MapPointType.Monster or MapPointType.Elite)
        {
            AppendRow(
                body,
                ["average", "gold_gained", pointType == MapPointType.Elite ? "elite" : "combat"],
                [pointType == MapPointType.Elite ? "elite" : "combat"],
                "Avg gold gained per combat",
                Divide(category.GoldGained, category.CombatsCompleted));
        }

        if (category.GoldSpent != 0 || pointType is MapPointType.Unknown or MapPointType.Shop)
            AppendRow(body, ["gold"], [], "Gold spent", category.GoldSpent);

        if (pointType == MapPointType.Shop)
        {
            AppendRow(
                body,
                ["average", "gold", "shop"],
                ["shop"],
                "Avg gold spent per merchant",
                Divide(category.GoldSpent, category.Visits));
        }

        AppendRow(body, ["card"], [], "Cards gained", category.CardsGained);
        AppendRow(body, ["relic_gained"], [], "Relics gained", category.RelicsGained);
        AppendRow(body, ["offered", "potion"], [], "Potions offered", potionsOffered);
        AppendRow(body, ["potion_gained"], [], "Potions gained", potionsGained);
        AppendRow(body, ["max_hp_gained"], [], "Max HP gained", maxHpGained);
        AppendRow(body, ["max_hp"], [], "Max HP lost", maxHpLost);

        return body.ToString();
    }

    private static MapLegendCategoryStats? GetCategory(
        RunMapLegendStats stats,
        MapPointType pointType)
        => pointType switch
        {
            MapPointType.Unknown => stats.Unknown,
            MapPointType.Shop => stats.Shop,
            MapPointType.Treasure => stats.Treasure,
            MapPointType.RestSite => stats.RestSite,
            MapPointType.Monster => stats.Monster,
            MapPointType.Elite => stats.Elite,
            _ => null,
        };

    private static string[] LocationKinds(MapPointType pointType)
        => pointType switch
        {
            MapPointType.Unknown => ["Event"],
            MapPointType.Shop => ["Shop"],
            MapPointType.Treasure => ["Treasure"],
            MapPointType.RestSite => ["Rest site"],
            MapPointType.Monster => ["Combat"],
            MapPointType.Elite => ["Elite combat"],
            _ => [],
        };

    private static bool MatchesLocation(string? location, string[] expected)
        => !string.IsNullOrWhiteSpace(location)
            && expected.Any(value => string.Equals(
                location,
                value,
                StringComparison.OrdinalIgnoreCase));

    private static string Title(MapPointType pointType)
        => pointType switch
        {
            MapPointType.Unknown => "Unknown-site stats",
            MapPointType.Shop => "Merchant stats",
            MapPointType.Treasure => "Treasure stats",
            MapPointType.RestSite => "Rest-site stats",
            MapPointType.Monster => "Combat stats",
            MapPointType.Elite => "Elite stats",
            _ => "Map stats",
        };

    private static string VisitLabel(MapPointType pointType)
        => pointType switch
        {
            MapPointType.Unknown => "Unknown sites entered",
            MapPointType.Shop => "Merchants visited",
            MapPointType.Treasure => "Treasure rooms visited",
            MapPointType.RestSite => "Rest sites visited",
            MapPointType.Monster => "Combat nodes entered",
            MapPointType.Elite => "Elite nodes entered",
            _ => "Rooms visited",
        };

    private static string VisitNoun(MapPointType pointType)
        => pointType switch
        {
            MapPointType.Unknown => "unknown sites",
            MapPointType.Shop => "merchants",
            MapPointType.Treasure => "treasure rooms",
            MapPointType.RestSite => "rest sites",
            MapPointType.Monster => "combat nodes",
            MapPointType.Elite => "elite nodes",
            _ => "visits",
        };

    private static string LegendConcept(MapPointType pointType)
        => pointType switch
        {
            MapPointType.Unknown => "unknown_room",
            MapPointType.Shop => "shop",
            MapPointType.Treasure => "relic",
            MapPointType.RestSite => "campfire",
            MapPointType.Monster => "combat",
            MapPointType.Elite => "elite",
            _ => "floor",
        };

    private static void AppendRow(
        StringBuilder body,
        IReadOnlyList<string> conceptIds,
        IReadOnlyList<string> denominatorConceptIds,
        string label,
        int value)
        => AppendRow(
            body,
            conceptIds,
            denominatorConceptIds,
            label,
            value.ToString(CultureInfo.InvariantCulture));

    private static void AppendRow(
        StringBuilder body,
        IReadOnlyList<string> conceptIds,
        IReadOnlyList<string> denominatorConceptIds,
        string label,
        decimal value)
        => AppendRow(
            body,
            conceptIds,
            denominatorConceptIds,
            label,
            FormatDecimal(value));

    private static void AppendRow(
        StringBuilder body,
        IReadOnlyList<string> conceptIds,
        IReadOnlyList<string> denominatorConceptIds,
        string label,
        string value)
    {
        StatsTooltip.AppendInlineStatRow(
            body,
            conceptIds,
            denominatorConceptIds,
            label,
            value,
            DescribeRow(label));
    }

    private static string DescribeRow(string label)
    {
        if (label.StartsWith("Avg floors between ", StringComparison.Ordinal))
            return "Average number of floors traveled between visits to this map location type.";

        return label switch
        {
            "Win rate" => "Share of completed combats at this map location type that were won.",
            "Perfect combats" => "Combats won at this map location type without losing HP.",
            "Avg turns per combat" => "Average player turns taken per combat at this map location type.",
            "Avg HP lost per combat" => "Average HP lost per combat at this map location type.",
            "Avg HP restored per rest site" => "Average HP restored per rest-site visit.",
            "Avg gold gained per combat" => "Average gold gained per combat at this map location type.",
            "Avg gold spent per merchant" => "Average gold spent per merchant visited.",
            _ => $"{label} at this map location type.",
        };
    }

    private static decimal Divide(int numerator, int denominator)
        => denominator > 0 ? (decimal)numerator / denominator : 0m;

    private static decimal Divide(decimal numerator, int denominator)
        => denominator > 0 ? numerator / denominator : 0m;

    private static string Percent(int numerator, int denominator)
        => denominator > 0
            ? ((decimal)numerator / denominator).ToString("0%", CultureInfo.InvariantCulture)
            : "0%";

    private static string FormatDecimal(decimal value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);
}
