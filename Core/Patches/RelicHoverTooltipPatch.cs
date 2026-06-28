using System;
using System.Linq;
using System.Text;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Relics;


namespace SpireLens.Core.Patches;

/// <summary>
/// Shows per-relic SpireLens stats below the game's relic hover tooltip
/// when the player hovers a relic in the inventory bar.
/// </summary>
[HarmonyPatch(typeof(NRelicInventoryHolder), "OnFocus")]
public static class RelicHoverShowPatch
{
    private const string VulnerableIconPath = "res://images/atlases/power_atlas.sprites/vulnerable_power.tres";
    private const string WeakIconPath = "res://images/atlases/power_atlas.sprites/weak_power.tres";
    private const string BlockIconPath = "res://images/ui/combat/block.png";
    private const string EnergyIconPath = "res://images/atlases/potion_atlas.sprites/energy_potion.tres";
    private const int InlineIconSize = 16;

    [HarmonyPostfix]
    public static void Postfix(NRelicInventoryHolder __instance)
    {
        try
        {
            var tickbox = ViewStatsInjectorPatch.LastInjectedTickbox;
            var viewStatsEnabled = tickbox?.IsTicked ?? RuntimeOptionsProvider.Current.ViewStatsToggleEnabled;
            if (!viewStatsEnabled) return;

            var relicNode = __instance.Relic;
            if (relicNode?.Model == null) return;

            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null) return;

            if (relicNode.Model is BagOfMarbles)
            {
                const string relicId = "RELIC.BAG_OF_MARBLES";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildBagOfMarblesBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Bag of Marbles", "SpireLens", body);
                return;
            }

            if (relicNode.Model is RedMask)
            {
                const string relicId = "RELIC.RED_MASK";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildRedMaskBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Red Mask", "SpireLens", body);
                return;
            }

            if (relicNode.Model is Pocketwatch)
            {
                const string relicId = "RELIC.POCKETWATCH";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildPocketwatchBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Pocketwatch", "SpireLens", body);
                return;
            }

            if (relicNode.Model is Orichalcum)
            {
                const string relicId = "RELIC.ORICHALCUM";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildOrichalcumBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Orichalcum", "SpireLens", body);
                return;
            }

            if (relicNode.Model is TheAbacus)
            {
                const string relicId = "RELIC.THE_ABACUS";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildTheAbacusBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "The Abacus", "SpireLens", body);
                return;
            }

            if (relicNode.Model is BookRepairKnife)
            {
                const string relicId = "RELIC.BOOK_REPAIR_KNIFE";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildBookRepairKnifeBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Book Repair Knife", "SpireLens", body);
                return;
            }

            if (relicNode.Model is BoneFlute)
            {
                const string relicId = "RELIC.BONE_FLUTE";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildBoneFluteBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Bone Flute", "SpireLens", body);
                return;
            }

            if (relicNode.Model is HappyFlower)
            {
                const string relicId = "RELIC.HAPPY_FLOWER";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildHappyFlowerBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Happy Flower", "SpireLens", body);
                return;
            }

            if (relicNode.Model is PrismaticGem)
            {
                const string relicId = "RELIC.PRISMATIC_GEM";
                var agg = RunTracker.GetRelicAggregate(relicId);
                if (agg == null || (agg.EnergyGenerated == 0 && agg.CardRewardsAffected == 0)) return;

                var body = BuildPrismaticGemBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Prismatic Gem", "SpireLens", body);
                return;
            }

            if (relicNode.Model is CloakClasp)
            {
                const string relicId = "RELIC.CLOAK_CLASP";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildCloakClaspBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Cloak Clasp", "SpireLens", body);
                return;
            }

            if (relicNode.Model is MealTicket)
            {
                const string relicId = "RELIC.MEAL_TICKET";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildMealTicketBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Meal Ticket", "SpireLens", body);
                return;
            }

            if (relicNode.Model is BurningBlood)
            {
                const string relicId = "RELIC.BURNING_BLOOD";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildBurningBloodBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Burning Blood", "SpireLens", body);
                return;
            }

            if (relicNode.Model is WhiteBeastStatue)
            {
                const string relicId = "RELIC.WHITE_BEAST_STATUE";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildWhiteBeastStatueBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "White Beast Statue", "SpireLens", body);
                return;
            }

            if (relicNode.Model is BoundPhylactery)
            {
                const string relicId = "RELIC.BOUND_PHYLACTERY";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildPhylacteryBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Bound Phylactery", "SpireLens", body);
                return;
            }

            if (relicNode.Model is PhylacteryUnbound)
            {
                const string relicId = "RELIC.PHYLACTERY_UNBOUND";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildPhylacteryBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Phylactery Unbound", "SpireLens", body);
                return;
            }
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"RelicHoverShowPatch failed: {e.Message}");
        }
    }

    private static string BuildBagOfMarblesBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, VulnerableLabel("enemies affected"), agg.EnemiesAffected.ToString(), "");
        return sb.ToString();
    }

    private static string BuildRedMaskBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, WeakLabel("enemies affected"), agg.EnemiesAffected.ToString(), "");
        Row3(sb, WeakLabel("weak applied"), agg.WeakApplied.ToString(), "");
        return sb.ToString();
    }

    private static string BuildPocketwatchBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "additional cards drawn", agg.AdditionalCardsDrawn.ToString(), "");
        return sb.ToString();
    }

    private static string BuildOrichalcumBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, BlockLabel("block gained"), agg.AdditionalBlockGained.ToString(), "");
        return sb.ToString();
    }

    private static string BuildTheAbacusBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, BlockLabel("block gained"), agg.AdditionalBlockGained.ToString(), "");
        return sb.ToString();
    }

    private static string BuildBookRepairKnifeBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Doom kills", agg.DoomKills.ToString(), "");
        AppendHealingStats(sb, agg);
        return sb.ToString();
    }

    private static string BuildBoneFluteBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Times triggered", agg.BoneFluteTriggers.ToString(), "");
        Row3(sb, BlockLabel("block gained"), agg.AdditionalBlockGained.ToString(), "");
        return sb.ToString();
    }

    private static string BuildHappyFlowerBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, EnergyLabel("Energy generated"), agg.EnergyGenerated.ToString(), "");
        return sb.ToString();
    }

    private static string BuildPrismaticGemBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, EnergyLabel("Energy generated"), agg.EnergyGenerated.ToString(), "");
        Row3(sb, "Card rewards affected", agg.CardRewardsAffected.ToString(), "");
        return sb.ToString();
    }

    private static string BuildCloakClaspBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, BlockLabel("Block gained"), agg.AdditionalBlockGained.ToString(), "");
        return sb.ToString();
    }

    private static string BuildMealTicketBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        AppendHealingStats(sb, agg);
        return sb.ToString();
    }

    private static string BuildBurningBloodBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        AppendHealingStats(sb, agg);
        return sb.ToString();
    }

    private static string BuildWhiteBeastStatueBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Potions gained", agg.PotionsGained.ToString(), "");
        Row3(sb, "Potions skipped", agg.PotionsSkipped.ToString(), "");
        Row3(sb, "common potions", agg.CommonPotionsGained.ToString(), "");
        Row3(sb, "uncommon potions", agg.UncommonPotionsGained.ToString(), "");
        Row3(sb, "rare potions", agg.RarePotionsGained.ToString(), "");
        return sb.ToString();
    }

    private static string BuildPhylacteryBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        Row3(sb, "Osty summon gained", FormatDecimal(agg.TotalOstyHpSummoned), "");
        return sb.ToString();
    }

    private static string VulnerableLabel(string suffix)
    {
        var path = NormalizeResourcePath(VulnerableIconPath);
        return $"[img={InlineIconSize}x{InlineIconSize}]{path}[/img] {suffix}";
    }

    private static string WeakLabel(string suffix)
    {
        var path = NormalizeResourcePath(WeakIconPath);
        return $"[img={InlineIconSize}x{InlineIconSize}]{path}[/img] {suffix}";
    }

    private static string BlockLabel(string suffix)
    {
        var path = NormalizeResourcePath(BlockIconPath);
        return $"[img={InlineIconSize}x{InlineIconSize}]{path}[/img] {suffix}";
    }

    private static void AppendHealingStats(StringBuilder sb, RelicAggregate agg)
    {
        if (agg.TotalHealingRestored > 0m)
            Row3(sb, "HP healed", FormatDecimal(agg.TotalHealingRestored), "");

        if (agg.TotalHealingLost <= 0m) return;

        Row3(sb, "healing lost", FormatDecimal(agg.TotalHealingLost), "");
        foreach (var reason in agg.HealingLostReasons.Values
                     .OrderByDescending(r => r.Amount)
                     .ThenBy(r => r.DisplayName))
        {
            if (reason.Amount <= 0m) continue;
            var label = string.IsNullOrWhiteSpace(reason.DisplayName)
                ? "lost to other/prevented"
                : $"lost to {reason.DisplayName}";
            Row3(sb, label, FormatDecimal(reason.Amount), "");
        }
    }

    private static string FormatDecimal(decimal value)
    {
        return decimal.Truncate(value) == value
            ? value.ToString("0")
            : value.ToString("0.##");
    }

    private static string EnergyLabel(string suffix)
    {
        var path = NormalizeResourcePath(EnergyIconPath);
        return $"[img={InlineIconSize}x{InlineIconSize}]{path}[/img] {suffix}";
    }

    private static string NormalizeResourcePath(string path)
    {
        return path.StartsWith("res://", StringComparison.Ordinal)
            ? path
            : $"res://{path.TrimStart('/')}";
    }

    private static void Row3(StringBuilder sb, string label, string value, string pct)
    {
        sb.Append("[table=3]");
        sb.Append($"[cell expand=4 padding=0,0,12,0][color=#e0e0e0]{label}[/color][/cell]");
        sb.Append($"[cell expand=1 padding=0,0,12,0][right][b]{value}[/b][/right][/cell]");
        sb.Append($"[cell expand=1 padding=0,0,4,0][right][color=#b5b5b5]{pct}[/color][/right][/cell]");
        sb.Append("[/table]\n");
    }
}

[HarmonyPatch(typeof(NRelicInventoryHolder), "OnUnfocus")]
public static class RelicHoverHidePatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        try { StatsTooltip.Hide(); }
        catch (Exception e) { CoreMain.Logger.Error($"RelicHoverHidePatch failed: {e.Message}"); }
    }
}
