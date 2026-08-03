using System.Globalization;
using System.Text;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace SpireLens.Core.Patches;

/// <summary>
/// Run summaries appended to the game's six native map-legend hover tips.
/// The legend item keeps ownership of focus/unfocus and positioning.
/// </summary>
internal static class MapLegendStatsTooltip
{
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
        var average = Icon("average");
        var floor = Icon("floor");
        var combat = Icon(pointType == MapPointType.Elite ? "elite" : "combat");
        var gold = Icon("gold");
        var goldGained = Icon("gold_gained");
        var healing = Icon("healing_gained");
        var damage = Icon("damage");
        var card = Icon("card");
        var relic = Icon("relic");
        var potion = Icon("potion");
        var offered = Icon("offered");
        var upgraded = Icon("upgraded");
        var maxHp = Icon("max_hp");
        var maxHpGainedIcon = Icon("max_hp_gained");
        var kill = Icon("kill");

        AppendRow(body, LegendIcon(pointType), VisitLabel(pointType), category.Visits);
        AppendRow(
            body,
            $"{average} {floor}",
            $"Avg floors between {VisitNoun(pointType)}",
            Divide(category.FloorsBetweenVisitsTotal, category.FloorsBetweenVisitsSamples));

        if (pointType == MapPointType.Unknown)
        {
            AppendRow(body, Icon("unknown_room"), "Events found", category.ResolvedEvents);
            AppendRow(body, Icon("combat"), "Combats found", category.ResolvedCombats);
            AppendRow(body, Icon("elite"), "Elites found", category.ResolvedElites);
            AppendRow(body, gold, "Merchants found", category.ResolvedShops);
            AppendRow(body, relic, "Treasure rooms found", category.ResolvedTreasures);
            AppendRow(body, Icon("campfire"), "Rest sites found", category.ResolvedRestSites);
        }

        if (pointType is MapPointType.Monster or MapPointType.Elite)
        {
            AppendRow(body, combat, "Combats completed", category.CombatsCompleted);
            AppendRow(body, kill, pointType == MapPointType.Elite ? "Elites slain" : "Combats won", category.CombatsWon);
            AppendRow(
                body,
                $"{average} {kill}",
                "Win rate",
                Percent(category.CombatsWon, category.CombatsCompleted));
            AppendRow(body, $"{combat} {healing}", "Perfect combats", category.PerfectCombats);
            AppendRow(
                body,
                $"{average} {Icon("turn")}",
                "Avg turns per combat",
                Divide(category.CombatTurns, category.CombatsCompleted));
        }

        if (category.HpLost != 0m
            || pointType is MapPointType.Unknown or MapPointType.Monster or MapPointType.Elite)
        {
            AppendRow(body, damage, "HP lost", category.HpLost);
        }

        if (pointType is MapPointType.Monster or MapPointType.Elite)
        {
            AppendRow(
                body,
                $"{average} {damage}",
                "Avg HP lost per combat",
                Divide(category.HpLost, category.CombatsCompleted));
        }

        if (category.HpHealed != 0m
            || pointType is MapPointType.Shop or MapPointType.RestSite
                or MapPointType.Monster or MapPointType.Elite)
        {
            AppendRow(body, healing, "HP restored", category.HpHealed);
        }

        if (pointType == MapPointType.RestSite)
        {
            AppendRow(
                body,
                $"{average} {healing}",
                "Avg HP restored per rest site",
                Divide(category.HpHealed, category.Visits));
        }

        if (category.CardsUpgraded != 0 || pointType == MapPointType.RestSite)
            AppendRow(body, upgraded, "Cards upgraded", category.CardsUpgraded);

        if (category.GoldGained != 0
            || pointType is MapPointType.Unknown or MapPointType.Shop
                or MapPointType.Treasure or MapPointType.Monster or MapPointType.Elite)
        {
            AppendRow(body, goldGained, "Gold gained", category.GoldGained);
        }

        if (pointType is MapPointType.Monster or MapPointType.Elite)
        {
            AppendRow(
                body,
                $"{average} {goldGained}",
                "Avg gold gained per combat",
                Divide(category.GoldGained, category.CombatsCompleted));
        }

        if (category.GoldSpent != 0 || pointType is MapPointType.Unknown or MapPointType.Shop)
            AppendRow(body, gold, "Gold spent", category.GoldSpent);

        if (pointType == MapPointType.Shop)
        {
            AppendRow(
                body,
                $"{average} {gold}",
                "Avg gold spent per merchant",
                Divide(category.GoldSpent, category.Visits));
        }

        AppendRow(body, card, "Cards gained", category.CardsGained);
        AppendRow(body, relic, "Relics gained", category.RelicsGained);
        AppendRow(body, $"{offered} {potion}", "Potions offered", potionsOffered);
        AppendRow(body, potion, "Potions gained", potionsGained);
        AppendRow(body, maxHpGainedIcon, "Max HP gained", maxHpGained);
        AppendRow(body, maxHp, "Max HP lost", maxHpLost);

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

    private static string LegendIcon(MapPointType pointType)
        => Icon(pointType switch
        {
            MapPointType.Unknown => "unknown_room",
            MapPointType.Shop => "gold",
            MapPointType.Treasure => "relic",
            MapPointType.RestSite => "campfire",
            MapPointType.Monster => "combat",
            MapPointType.Elite => "elite",
            _ => "floor",
        });

    private static string Icon(string id)
        => StatConceptGlossary.RenderHintedGlyph(id);

    private static void AppendRow(
        StringBuilder body,
        string icon,
        string label,
        int value)
        => AppendRow(body, icon, label, value.ToString(CultureInfo.InvariantCulture));

    private static void AppendRow(
        StringBuilder body,
        string icon,
        string label,
        decimal value)
        => AppendRow(body, icon, label, FormatDecimal(value));

    private static void AppendRow(
        StringBuilder body,
        string icon,
        string label,
        string value)
    {
        if (body.Length > 0) body.Append('\n');
        body.Append(icon)
            .Append(' ')
            .Append(label)
            .Append("   [b]")
            .Append(value)
            .Append("[/b]");
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
