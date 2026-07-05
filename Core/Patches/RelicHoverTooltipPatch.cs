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
    private const string VigorIconPath = "res://images/atlases/power_atlas.sprites/vigor_power.tres";
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

            RelicAggregate RelicAgg(string relicId) =>
                RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

            if (relicNode.Model is BagOfMarbles)
            {
                const string relicId = "RELIC.BAG_OF_MARBLES";
                var agg = RelicAgg(relicId);

                var body = BuildBagOfMarblesBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Bag of Marbles", "SpireLens", body);
                return;
            }

            if (relicNode.Model is RedMask)
            {
                const string relicId = "RELIC.RED_MASK";
                var agg = RelicAgg(relicId);

                var body = BuildRedMaskBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Red Mask", "SpireLens", body);
                return;
            }

            if (relicNode.Model is Pocketwatch)
            {
                const string relicId = "RELIC.POCKETWATCH";
                var agg = RelicAgg(relicId);

                var body = BuildPocketwatchBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Pocketwatch", "SpireLens", body);
                return;
            }

            if (relicNode.Model is Orichalcum)
            {
                const string relicId = "RELIC.ORICHALCUM";
                var agg = RelicAgg(relicId);

                var body = BuildOrichalcumBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Orichalcum", "SpireLens", body);
                return;
            }

            if (relicNode.Model is Permafrost)
            {
                const string relicId = "RELIC.PERMAFROST";
                var agg = RelicAgg(relicId);

                var body = BuildPermafrostBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Permafrost", "SpireLens", body);
                return;
            }

            if (relicNode.Model is TheAbacus)
            {
                const string relicId = "RELIC.THE_ABACUS";
                var agg = RelicAgg(relicId);

                var body = BuildTheAbacusBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "The Abacus", "SpireLens", body);
                return;
            }

            if (IsAnchorStatsRelicModel(relicNode.Model))
            {
                const string relicId = "RELIC.ANCHOR";
                var agg = RelicAgg(relicId);

                var body = BuildAnchorBodyBBCode(agg);
                var title = IsFakeAnchorRelicModel(relicNode.Model) ? "???" : "Anchor";
                StatsTooltip.Show(tree, __instance, title, "SpireLens", body);
                return;
            }

            if (IsRelicModel(relicNode.Model, "MegaCrit.Sts2.Core.Models.Relics.LetterOpener"))
            {
                const string relicId = "RELIC.LETTER_OPENER";
                var agg = RelicAgg(relicId);

                var body = BuildLetterOpenerBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Letter Opener", "SpireLens", body);
                return;
            }

            if (IsRelicModel(relicNode.Model, "MegaCrit.Sts2.Core.Models.Relics.Akabeko"))
            {
                const string relicId = "RELIC.AKABEKO";
                var agg = RelicAgg(relicId);

                var body = BuildAkabekoBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Akabeko", "SpireLens", body);
                return;
            }

            if (relicNode.Model is BookRepairKnife)
            {
                const string relicId = "RELIC.BOOK_REPAIR_KNIFE";
                var agg = RelicAgg(relicId);

                var body = BuildBookRepairKnifeBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Book Repair Knife", "SpireLens", body);
                return;
            }

            if (relicNode.Model is EternalFeather)
            {
                const string relicId = "RELIC.ETERNAL_FEATHER";
                var agg = RelicAgg(relicId);

                var body = BuildEternalFeatherBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Eternal Feather", "SpireLens", body);
                return;
            }

            if (relicNode.Model is BoneFlute)
            {
                const string relicId = "RELIC.BONE_FLUTE";
                var agg = RelicAgg(relicId);

                var body = BuildBoneFluteBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Bone Flute", "SpireLens", body);
                return;
            }

            if (relicNode.Model is HappyFlower)
            {
                const string relicId = "RELIC.HAPPY_FLOWER";
                var agg = RelicAgg(relicId);

                var body = BuildHappyFlowerBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Happy Flower", "SpireLens", body);
                return;
            }

            if (IsRelicModel(relicNode.Model, "MegaCrit.Sts2.Core.Models.Relics.BoomingConch"))
            {
                const string relicId = "RELIC.BOOMING_CONCH";
                var agg = RelicAgg(relicId);

                var body = BuildBoomingConchBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Booming Conch", "SpireLens", body);
                return;
            }

            if (relicNode.Model is GremlinHorn)
            {
                const string relicId = "RELIC.GREMLIN_HORN";
                var agg = RelicAgg(relicId);

                var body = BuildGremlinHornBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Gremlin Horn", "SpireLens", body);
                return;
            }

            if (relicNode.Model is Pendulum)
            {
                const string relicId = "RELIC.PENDULUM";
                var agg = RelicAgg(relicId);

                var body = BuildPendulumBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Pendulum", "SpireLens", body);
                return;
            }

            if (relicNode.Model is MercuryHourglass)
            {
                const string relicId = "RELIC.MERCURY_HOURGLASS";
                var agg = RelicAgg(relicId);

                var body = BuildMercuryHourglassBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Mercury Hourglass", "SpireLens", body);
                return;
            }

            if (relicNode.Model is ParryingShield)
            {
                const string relicId = "RELIC.PARRYING_SHIELD";
                var agg = RelicAgg(relicId);

                var body = BuildParryingShieldBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Parrying Shield", "SpireLens", body);
                return;
            }

            if (relicNode.Model is FestivePopper)
            {
                const string relicId = "RELIC.FESTIVE_POPPER";
                var agg = RelicAgg(relicId);

                var body = BuildFestivePopperBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Festive Popper", "SpireLens", body);
                return;
            }

            if (relicNode.Model is PenNib)
            {
                const string relicId = "RELIC.PEN_NIB";
                var agg = RelicAgg(relicId);

                var body = BuildPenNibBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Pen Nib", "SpireLens", body);
                return;
            }

            if (relicNode.Model is HornCleat)
            {
                const string relicId = "RELIC.HORN_CLEAT";
                var agg = RelicAgg(relicId);

                var body = BuildHornCleatBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Horn Cleat", "SpireLens", body);
                return;
            }

            if (relicNode.Model is PrismaticGem)
            {
                const string relicId = "RELIC.PRISMATIC_GEM";
                var agg = RelicAgg(relicId);

                var body = BuildPrismaticGemBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Prismatic Gem", "SpireLens", body);
                return;
            }

            if (relicNode.Model is BloodSoakedRose)
            {
                var relicId = relicNode.Model.Id.ToString();
                var agg = RelicAgg(relicId);
                var curseAgg = RunTracker.GetEnthralledCurseAggregate();

                var body = BuildBloodSoakedRoseBodyBBCode(agg, curseAgg);
                StatsTooltip.Show(tree, __instance, "Blood-Soaked Rose", "SpireLens", body);
                return;
            }

            if (relicNode.Model is CloakClasp)
            {
                const string relicId = "RELIC.CLOAK_CLASP";
                var agg = RelicAgg(relicId);

                var body = BuildCloakClaspBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Cloak Clasp", "SpireLens", body);
                return;
            }

            if (relicNode.Model is ReptileTrinket)
            {
                const string relicId = "RELIC.REPTILE_TRINKET";
                var agg = RelicAgg(relicId);

                var body = BuildReptileTrinketBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Reptile Trinket", "SpireLens", body);
                return;
            }

            if (relicNode.Model is Gorget)
            {
                const string relicId = "RELIC.GORGET";
                var agg = RelicAgg(relicId);

                var body = BuildGorgetBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Gorget", "SpireLens", body);
                return;
            }

            if (relicNode.Model is StoneCracker)
            {
                const string relicId = "RELIC.STONE_CRACKER";
                var agg = RelicAgg(relicId);

                var body = BuildStoneCrackerBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Stone Cracker", "SpireLens", body);
                return;
            }

            if (relicNode.Model is SandCastle)
            {
                const string relicId = "RELIC.SAND_CASTLE";
                var agg = RelicAgg(relicId);

                var body = BuildSandCastleBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Sand Castle", "SpireLens", body);
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

            if (relicNode.Model is Planisphere)
            {
                const string relicId = "RELIC.PLANISPHERE";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildPlanisphereBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Planisphere", "SpireLens", body);
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

            if (relicNode.Model is LeesWaffle)
            {
                const string relicId = "RELIC.LEES_WAFFLE";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildLeesWaffleBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Lee's Waffle", "SpireLens", body);
                return;
            }

            if (relicNode.Model is RegalPillow)
            {
                const string relicId = "RELIC.REGAL_PILLOW";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildRegalPillowBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Regal Pillow", "SpireLens", body);
                return;
            }

            if (relicNode.Model is PrecariousShears)
            {
                const string relicId = "RELIC.PRECARIOUS_SHEARS";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildPrecariousShearsBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Precarious Shears", "SpireLens", body);
                return;
            }

            if (IsRelicModel(relicNode.Model, "MegaCrit.Sts2.Core.Models.Relics.BloodVial"))
            {
                const string relicId = "RELIC.BLOOD_VIAL";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildBloodVialBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Blood Vial", "SpireLens", body);
                return;
            }

            if (IsRelicModel(relicNode.Model, "MegaCrit.Sts2.Core.Models.Relics.Toolbox"))
            {
                const string relicId = "RELIC.TOOLBOX";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildToolboxBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Toolbox", "SpireLens", body);
                return;
            }

            if (relicNode.Model is PaelsWing)
            {
                const string relicId = "RELIC.PAELS_WING";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildPaelsWingBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Pael's Wing", "SpireLens", body);
                return;
            }

            if (IsStrikeDummyStatsRelicModel(relicNode.Model))
            {
                var agg = RunTracker.GetStrikeDummyAggregate();

                var body = BuildStrikeDummyBodyBBCode(agg);
                var title = IsFakeStrikeDummyRelicModel(relicNode.Model) ? "???" : "Strike Dummy";
                StatsTooltip.Show(tree, __instance, title, "SpireLens", body);
                return;
            }

            if (relicNode.Model is BrilliantScarf)
            {
                const string relicId = "RELIC.BRILLIANT_SCARF";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildBrilliantScarfBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Brilliant Scarf", "SpireLens", body);
                return;
            }

            if (relicNode.Model is GamblingChip)
            {
                const string relicId = "RELIC.GAMBLING_CHIP";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildGamblingChipBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Gambling Chip", "SpireLens", body);
                return;
            }

            if (relicNode.Model is CentennialPuzzle)
            {
                const string relicId = "RELIC.CENTENNIAL_PUZZLE";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildCentennialPuzzleBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Centennial Puzzle", "SpireLens", body);
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
        Row3(sb, "Triggers blocked", agg.BlockedTriggers.ToString(), "");
        return sb.ToString();
    }

    private static string BuildPermafrostBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var blockPerCombat = agg.Activations <= 0
            ? 0m
            : (decimal)agg.AdditionalBlockGained / agg.Activations;
        Row3(sb, "Combats triggered", agg.Activations.ToString(), "");
        Row3(sb, BlockLabel("block gained"), agg.AdditionalBlockGained.ToString(), "");
        Row3(sb, BlockLabel("block gained per combat"), FormatDecimal(blockPerCombat), "");
        return sb.ToString();
    }

    private static string BuildTheAbacusBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, BlockLabel("block gained"), agg.AdditionalBlockGained.ToString(), "");
        return sb.ToString();
    }

    private static string BuildAnchorBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        Row3(sb, BlockLabel("block gained"), agg.AdditionalBlockGained.ToString(), "");
        return sb.ToString();
    }

    private static string BuildLetterOpenerBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        Row3(sb, "Damage attempted", agg.TotalDamageAttempted.ToString(), "");
        Row3(sb, "Targets hit", agg.TotalTargets.ToString(), "");
        return sb.ToString();
    }

    private static string BuildAkabekoBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, VigorLabel("vigor gained"), agg.VigorGained.ToString(), "");
        return sb.ToString();
    }

    private static string BuildBookRepairKnifeBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Doom kills", agg.DoomKills.ToString(), "");
        AppendHealingStats(sb, agg);
        return sb.ToString();
    }

    private static string BuildEternalFeatherBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Activations", agg.Activations.ToString(), "");
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
        AppendEnergyGeneratedStats(sb, agg);
        return sb.ToString();
    }

    private static string BuildBoomingConchBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        AppendEnergyGeneratedStats(sb, agg);
        Row3(sb, "Cards drawn", agg.AdditionalCardsDrawn.ToString(), "");
        return sb.ToString();
    }

    private static string BuildGremlinHornBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        AppendEnergyGeneratedStats(sb, agg);
        Row3(sb, "Cards drawn", agg.AdditionalCardsDrawn.ToString(), "");
        return sb.ToString();
    }

    private static string BuildPendulumBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        Row3(sb, "Cards drawn", agg.AdditionalCardsDrawn.ToString(), "");
        return sb.ToString();
    }

    private static string BuildMercuryHourglassBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var damagePerCombat = agg.Activations <= 0
            ? 0m
            : (decimal)agg.TotalDamageDealt / agg.Activations;
        Row3(sb, "Combats triggered", agg.Activations.ToString(), "");
        Row3(sb, "Damage dealt", agg.TotalDamageDealt.ToString(), "");
        Row3(sb, "Damage per combat", FormatDecimal(damagePerCombat), "");
        return sb.ToString();
    }

    private static string BuildParryingShieldBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        Row3(sb, "Damage attempted", agg.TotalDamageAttempted.ToString(), "");
        Row3(sb, "Damage dealt", agg.TotalDamageDealt.ToString(), "");
        Row3(sb, "Damage blocked", agg.TotalDamageBlocked.ToString(), "");
        Row3(sb, "Overkill", agg.TotalDamageOverkill.ToString(), "");
        Row3(sb, "Kills", agg.Kills.ToString(), "");
        return sb.ToString();
    }

    private static string BuildFestivePopperBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var damagePerCombat = agg.Activations <= 0
            ? 0m
            : (decimal)agg.TotalDamageDealt / agg.Activations;
        Row3(sb, "Combats triggered", agg.Activations.ToString(), "");
        Row3(sb, "Damage dealt", agg.TotalDamageDealt.ToString(), "");
        Row3(sb, "Damage per combat", FormatDecimal(damagePerCombat), "");
        return sb.ToString();
    }

    private static string BuildPenNibBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Base damage added", agg.TotalDamageAttempted.ToString(), "");
        return sb.ToString();
    }

    private static string BuildHornCleatBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        Row3(sb, BlockLabel("block gained"), agg.AdditionalBlockGained.ToString(), "");
        return sb.ToString();
    }

    private static string BuildPrismaticGemBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        AppendEnergyGeneratedStats(sb, agg);
        Row3(sb, "Card rewards affected", agg.CardRewardsAffected.ToString(), "");
        foreach (var category in agg.CardRewardCategories
            .Where(kvp => kvp.Value.Count > 0)
            .OrderBy(kvp => kvp.Key == "colorless" ? 1 : 0)
            .ThenBy(kvp => kvp.Value.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            Row3(sb, $"{StatsTooltip.EscapeBbcode(category.Value.DisplayName)} rewards", category.Value.Count.ToString(), "");
        }
        return sb.ToString();
    }

    private static string BuildBloodSoakedRoseBodyBBCode(RelicAggregate agg, CardAggregate curseAgg)
    {
        var sb = new StringBuilder();
        AppendEnergyGeneratedStats(
            sb,
            agg,
            totalLabel: "Energy gained total",
            includeAveragePerCombat: true);

        curseAgg ??= new CardAggregate();
        Row3(sb, "Enthralled combats", curseAgg.CombatsInDeck.ToString(), "");
        Row3(sb, "Enthralled drawn", curseAgg.TimesDrawn.ToString(), "");
        Row3(sb, "Enthralled discarded", curseAgg.TimesDiscarded.ToString(), "");
        Row3(sb, "Enthralled played", curseAgg.Plays.ToString(), "");
        Row3(sb, "Enthralled exhausted", curseAgg.TimesExhausted.ToString(), "");
        return sb.ToString();
    }

    private static string BuildCloakClaspBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, BlockLabel("Block gained"), agg.AdditionalBlockGained.ToString(), "");
        return sb.ToString();
    }

    private static string BuildReptileTrinketBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        Row3(sb, "Strength added", FormatDecimal(agg.StrengthAdded), "");
        return sb.ToString();
    }

    private static string BuildGorgetBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        Row3(sb, "Plating added", FormatDecimal(agg.PlatingAdded), "");
        return sb.ToString();
    }

    private static string BuildStoneCrackerBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        Row3(sb, "Cards upgraded", agg.CardsUpgraded.ToString(), "");
        return sb.ToString();
    }

    private static string BuildSandCastleBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var upgradedCards = (agg.UpgradedCards ?? new System.Collections.Generic.List<string>())
            .Where(card => !string.IsNullOrWhiteSpace(card))
            .ToList();

        Row3(sb, "Cards upgraded", agg.CardsUpgraded.ToString(), "");
        foreach (var card in upgradedCards)
            Row3(sb, "Upgraded card", StatsTooltip.EscapeBbcode(card), "");

        return sb.ToString();
    }

    private static string BuildMealTicketBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        AppendHealingStats(sb, agg);
        return sb.ToString();
    }

    private static string BuildPlanisphereBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "? floors gained", agg.Activations.ToString(), "");
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

    private static string BuildLeesWaffleBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "HP gained", FormatDecimal(agg.TotalHealingRestored), "");
        return sb.ToString();
    }

    private static string BuildRegalPillowBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        AppendHealingStats(sb, agg);
        return sb.ToString();
    }

    private static string BuildPrecariousShearsBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var cardsRemoved = (agg.CardsRemoved ?? new System.Collections.Generic.List<string>())
            .Where(card => !string.IsNullOrWhiteSpace(card))
            .ToList();

        Row3(sb, "Cards removed", cardsRemoved.Count.ToString(), "");
        foreach (var card in cardsRemoved)
            Row3(sb, "Removed card", StatsTooltip.EscapeBbcode(card), "");

        Row3(sb, "Starting max HP", FormatDecimal(agg.StartingMaxHp ?? 0m), "");
        Row3(sb, "Resulting max HP", FormatDecimal(agg.ResultingMaxHp ?? 0m), "");
        return sb.ToString();
    }

    private static string BuildBloodVialBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        AppendHealingStats(sb, agg);
        return sb.ToString();
    }

    private static string BuildToolboxBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        Row3(sb, "Uncommon cards offered", agg.UncommonCardsOffered.ToString(), "");
        Row3(sb, "Rare cards offered", agg.RareCardsOffered.ToString(), "");
        Row3(sb, "Uncommon cards taken", agg.UncommonCardsTaken.ToString(), "");
        Row3(sb, "Rare cards taken", agg.RareCardsTaken.ToString(), "");
        return sb.ToString();
    }

    private static string BuildPaelsWingBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "common cards consumed", agg.CommonCardsConsumed.ToString(), "");
        Row3(sb, "uncommon cards consumed", agg.UncommonCardsConsumed.ToString(), "");
        Row3(sb, "rare cards consumed", agg.RareCardsConsumed.ToString(), "");
        Row3(sb, "Sacrifices made", agg.SacrificesMade.ToString(), "");
        Row3(sb, "Sacrifices skipped", agg.SacrificesSkipped.ToString(), "");
        var floorCount = RunTracker.GetCurrentFloorForRateStats();
        var rate = floorCount <= 0 ? 0m : (decimal)agg.SacrificesMade / floorCount;
        Row3(sb, "Sacrifice rate", FormatDecimal(rate), "/floor");
        return sb.ToString();
    }

    private static string BuildStrikeDummyBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Strikes played", agg.StrikeDummyStrikesPlayed.ToString(), "");
        Row3(sb, "Base Strikes in deck", agg.StrikeDummyBaseStrikesInDeck.ToString(), "");
        Row3(sb, "Non-base Strike cards in deck", agg.StrikeDummyNonBaseStrikeCardsInDeck.ToString(), "");
        return sb.ToString();
    }

    private static string BuildBrilliantScarfBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Discounts offered", agg.DiscountsOffered.ToString(), "");
        Row3(sb, "Discounts taken", agg.DiscountsTaken.ToString(), "");
        Row3(sb, EnergyLabel("Energy saved"), agg.EnergySavedByDiscount.ToString(), "");
        return sb.ToString();
    }

    private static string BuildGamblingChipBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var averageDiscarded = agg.Activations <= 0
            ? 0m
            : (decimal)agg.CardsDiscarded / agg.Activations;
        Row3(sb, "Combats held", agg.Activations.ToString(), "");
        Row3(sb, "Cards discarded", agg.CardsDiscarded.ToString(), "");
        Row3(sb, "Avg discarded per combat", FormatDecimal(averageDiscarded), "");
        return sb.ToString();
    }

    private static string BuildCentennialPuzzleBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var averageDrawn = agg.Activations <= 0
            ? 0m
            : (decimal)agg.AdditionalCardsDrawn / agg.Activations;
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        Row3(sb, "Cards drawn total", agg.AdditionalCardsDrawn.ToString(), "");
        Row3(sb, "Avg cards drawn per combat", FormatDecimal(averageDrawn), "");
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
        Row3(sb, "HP healed", FormatDecimal(agg.TotalHealingRestored), "");
        Row3(sb, "healing lost", FormatDecimal(agg.TotalHealingLost), "");

        if (agg.TotalHealingLost <= 0m) return;

        foreach (var reason in agg.HealingLostReasons.Values
                     .OrderByDescending(r => r.Amount)
                     .ThenBy(r => r.DisplayName))
        {
            if (reason.Amount <= 0m) continue;
            var label = string.IsNullOrWhiteSpace(reason.DisplayName)
                ? "lost to other/prevented"
                : $"lost to {StatsTooltip.EscapeBbcode(reason.DisplayName)}";
            Row3(sb, label, FormatDecimal(reason.Amount), "");
        }
    }

    private static void AppendEnergyGeneratedStats(
        StringBuilder sb,
        RelicAggregate agg,
        string totalLabel = "Energy generated",
        bool includeAveragePerCombat = false,
        string averageLabel = "Avg energy gained per combat")
    {
        Row3(sb, EnergyLabel(totalLabel), agg.EnergyGenerated.ToString(), "");
        if (!includeAveragePerCombat) return;

        var average = agg.Activations <= 0
            ? 0m
            : (decimal)agg.EnergyGenerated / agg.Activations;
        Row3(sb, EnergyLabel(averageLabel), FormatDecimal(average), "");
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

    private static string VigorLabel(string suffix)
    {
        var path = NormalizeResourcePath(VigorIconPath);
        return $"[img={InlineIconSize}x{InlineIconSize}]{path}[/img] {suffix}";
    }

    private static bool IsRelicModel(object model, string typeName)
    {
        for (var type = model.GetType(); type != null; type = type.BaseType)
        {
            if (string.Equals(type.FullName, typeName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAnchorStatsRelicModel(object model)
    {
        return IsRelicModel(model, "MegaCrit.Sts2.Core.Models.Relics.Anchor")
            || IsFakeAnchorRelicModel(model);
    }

    private static bool IsFakeAnchorRelicModel(object model)
    {
        return IsRelicModel(model, "MegaCrit.Sts2.Core.Models.Relics.FakeAnchor");
    }

    private static bool IsStrikeDummyStatsRelicModel(object model)
    {
        return IsRelicModel(model, "MegaCrit.Sts2.Core.Models.Relics.StrikeDummy")
            || IsFakeStrikeDummyRelicModel(model);
    }

    private static bool IsFakeStrikeDummyRelicModel(object model)
    {
        return IsRelicModel(model, "MegaCrit.Sts2.Core.Models.Relics.FakeStrikeDummy");
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
