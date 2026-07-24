using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
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
    private const string EnthralledDefinitionId = "CARD.ENTHRALLED";
    private const string CursedPearlCurseDefinitionId = "CARD.GREED";
    private const string BrightestFlameDefinitionId = "CARD.BRIGHTEST_FLAME";
    private const string GameOverScreenNamespace = "MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen";
    private const string VulnerableIconPath = "res://images/atlases/power_atlas.sprites/vulnerable_power.tres";
    private const string WeakIconPath = "res://images/atlases/power_atlas.sprites/weak_power.tres";
    private const string BlockIconPath = "res://images/ui/combat/block.png";
    private const string DrawIconPath = "res://images/atlases/power_atlas.sprites/draw_cards_next_turn_power.tres";
    private const string EnergyIconPath = "res://images/atlases/potion_atlas.sprites/energy_potion.tres";
    private const string StarIconPath = "res://images/packed/sprite_fonts/star_icon.png";
    private const string VigorIconPath = "res://images/atlases/power_atlas.sprites/vigor_power.tres";
    private const int SealOfGoldLossPerTrigger = 5;
    private const int InlineIconSize = 16;
    private const int MaxTableLabelVisibleChars = 28;
    private const float SturdyClampTooltipWidth = 420f;
    private static readonly System.Reflection.FieldInfo? VambraceBlockGainedThisCombatField =
        AccessTools.Field(typeof(Vambrace), "_blockGainedThisCombat");
    private static readonly System.Reflection.FieldInfo? PermafrostActivatedThisCombatField =
        AccessTools.Field(typeof(Permafrost), "_activatedThisCombat");

    [HarmonyPostfix]
    public static void Postfix(NRelicInventoryHolder __instance)
    {
        try
        {
            if (!ViewStatsInjectorPatch.StatsVisibilityEnabled) return;

            var relicNode = __instance.Relic;
            if (relicNode?.Model == null) return;

            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null) return;

            if (TryBuildInventoryBodyBBCode(__instance, relicNode.Model, out var statsTitle, out var statsBody))
            {
                StatsTooltip.Show(
                    tree,
                    __instance,
                    statsTitle,
                    "SpireLens",
                    statsBody,
                    panelWidth: GetPreferredStatsTooltipWidth(relicNode.Model));
                return;
            }

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

            if (relicNode.Model is Permafrost permafrost)
            {
                const string relicId = "RELIC.PERMAFROST";
                var agg = RelicAgg(relicId);

                var body = BuildPermafrostBodyBBCode(agg, IsPermafrostActivatedThisCombat(permafrost));
                StatsTooltip.Show(tree, __instance, "Permafrost", "SpireLens", body);
                return;
            }

            if (relicNode.Model is Vambrace vambrace)
            {
                const string relicId = "RELIC.VAMBRACE";
                var agg = RelicAgg(relicId);

                var body = BuildVambraceBodyBBCode(agg, IsVambraceUsedThisCombat(vambrace));
                StatsTooltip.Show(tree, __instance, "Vambrace", "SpireLens", body);
                return;
            }

            if (relicNode.Model is TuningFork)
            {
                const string relicId = "RELIC.TUNING_FORK";
                var agg = RelicAgg(relicId);

                var body = BuildTuningForkBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Tuning Fork", "SpireLens", body);
                return;
            }

            if (relicNode.Model is RippleBasin)
            {
                const string relicId = "RELIC.RIPPLE_BASIN";
                var agg = RelicAgg(relicId);

                var body = BuildRippleBasinBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Ripple Basin", "SpireLens", body);
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

            if (relicNode.Model is Nunchaku)
            {
                const string relicId = "RELIC.NUNCHAKU";
                var agg = RelicAgg(relicId);

                var body = BuildNunchakuBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Nunchaku", "SpireLens", body);
                return;
            }

            if (relicNode.Model is IronClub)
            {
                const string relicId = "RELIC.IRON_CLUB";
                var agg = RelicAgg(relicId);

                var body = BuildIronClubBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Iron Club", "SpireLens", body);
                return;
            }

            if (relicNode.Model is Vajra)
            {
                const string relicId = "RELIC.VAJRA";
                var agg = RelicAgg(relicId);

                var body = BuildVajraBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Vajra", "SpireLens", body);
                return;
            }

            if (relicNode.Model is Kunai)
            {
                const string relicId = "RELIC.KUNAI";
                var agg = RelicAgg(relicId);

                var body = BuildKunaiBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Kunai", "SpireLens", body);
                return;
            }

            if (relicNode.Model is Lantern)
            {
                const string relicId = "RELIC.LANTERN";
                var agg = RelicAgg(relicId);

                var body = BuildLanternBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Lantern", "SpireLens", body);
                return;
            }

            if (relicNode.Model is VeryHotCocoa)
            {
                const string relicId = "RELIC.VERY_HOT_COCOA";
                var agg = RelicAgg(relicId);

                var body = BuildVeryHotCocoaBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Very Hot Cocoa", "SpireLens", body);
                return;
            }

            if (relicNode.Model is Candelabra)
            {
                const string relicId = "RELIC.CANDELABRA";
                var agg = RelicAgg(relicId);

                var body = BuildCandelabraBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Candelabra", "SpireLens", body);
                return;
            }

            if (relicNode.Model is Chandelier)
            {
                const string relicId = "RELIC.CHANDELIER";
                var agg = RelicAgg(relicId);

                var body = BuildChandelierBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Chandelier", "SpireLens", body);
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

            if (relicNode.Model is BronzeScales)
            {
                const string relicId = "RELIC.BRONZE_SCALES";
                var agg = RelicAgg(relicId);

                var body = BuildBronzeScalesBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Bronze Scales", "SpireLens", body);
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

            if (relicNode.Model is SealOfGold)
            {
                const string relicId = "RELIC.SEAL_OF_GOLD";
                var agg = RelicAgg(relicId);

                var body = BuildSealOfGoldBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Seal of Gold", "SpireLens", body);
                return;
            }

            if (relicNode.Model is FresnelLens)
            {
                const string relicId = "RELIC.FRESNEL_LENS";
                var agg = RelicAgg(relicId);

                var body = BuildFresnelLensBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Fresnel Lens", "SpireLens", body);
                return;
            }

            if (relicNode.Model is SilverCrucible)
            {
                const string relicId = "RELIC.SILVER_CRUCIBLE";
                var agg = RelicAgg(relicId);

                var body = BuildSilverCrucibleBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Silver Crucible", "SpireLens", body);
                return;
            }

            if (relicNode.Model is Orrery)
            {
                const string relicId = "RELIC.ORRERY";
                var agg = RelicAgg(relicId);

                var body = BuildOrreryBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Orrery", "SpireLens", body);
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

            if (relicNode.Model is Regalite)
            {
                const string relicId = "RELIC.REGALITE";
                var agg = RelicAgg(relicId);

                var body = BuildRegaliteBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Regalite", "SpireLens", body);
                return;
            }

            if (relicNode.Model is IntimidatingHelmet)
            {
                const string relicId = "RELIC.INTIMIDATING_HELMET";
                var agg = RelicAgg(relicId);

                var body = BuildIntimidatingHelmetBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Intimidating Helmet", "SpireLens", body);
                return;
            }

            if (relicNode.Model is SturdyClamp)
            {
                const string relicId = "RELIC.STURDY_CLAMP";
                var agg = RelicAgg(relicId);

                var body = BuildSturdyClampBodyBBCode(agg);
                StatsTooltip.Show(
                    tree,
                    __instance,
                    "Sturdy Clamp",
                    "SpireLens",
                    body,
                    panelWidth: GetPreferredStatsTooltipWidth(relicNode.Model));
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

            if (relicNode.Model is RazorTooth)
            {
                const string relicId = "RELIC.RAZOR_TOOTH";
                var agg = RelicAgg(relicId);

                var body = BuildRazorToothBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Razor Tooth", "SpireLens", body);
                return;
            }

            if (relicNode.Model is Whetstone)
            {
                const string relicId = "RELIC.WHETSTONE";
                var agg = RelicAgg(relicId);

                var body = BuildWhetstoneBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Whetstone", "SpireLens", body);
                return;
            }

            if (relicNode.Model is WarPaint)
            {
                const string relicId = "RELIC.WAR_PAINT";
                var agg = RelicAgg(relicId);

                var body = BuildWarPaintBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "War Paint", "SpireLens", body);
                return;
            }

            if (relicNode.Model is FragrantMushroom)
            {
                const string relicId = "RELIC.FRAGRANT_MUSHROOM";
                var agg = RelicAgg(relicId);

                var body = BuildFragrantMushroomBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Fragrant Mushroom", "SpireLens", body);
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

            if (relicNode.Model is LizardTail)
            {
                const string relicId = "RELIC.LIZARD_TAIL";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildLizardTailBodyBBCode(agg, RelicFloorAddedToDeck(relicNode.Model));
                StatsTooltip.Show(tree, __instance, "Lizard Tail", "SpireLens", body);
                return;
            }

            if (relicNode.Model is Pantograph)
            {
                const string relicId = "RELIC.PANTOGRAPH";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildPantographBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Pantograph", "SpireLens", body);
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

            if (relicNode.Model is Strawberry)
            {
                const string relicId = "RELIC.STRAWBERRY";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildStrawberryBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Strawberry", "SpireLens", body);
                return;
            }

            if (relicNode.Model is Pear)
            {
                const string relicId = "RELIC.PEAR";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildPearBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Pear", "SpireLens", body);
                return;
            }

            if (relicNode.Model is NutritiousOyster)
            {
                const string relicId = "RELIC.NUTRITIOUS_OYSTER";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildNutritiousOysterBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Nutritious Oyster", "SpireLens", body);
                return;
            }

            if (relicNode.Model is Mango)
            {
                const string relicId = "RELIC.MANGO";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildMangoBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Mango", "SpireLens", body);
                return;
            }

            if (relicNode.Model is ChosenCheese)
            {
                const string relicId = "RELIC.CHOSEN_CHEESE";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildChosenCheeseBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Chosen Cheese", "SpireLens", body);
                return;
            }

            if (relicNode.Model is DarkstonePeriapt)
            {
                const string relicId = "RELIC.DARKSTONE_PERIAPT";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildDarkstonePeriaptBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Darkstone Periapt", "SpireLens", body);
                return;
            }

            if (relicNode.Model is LuckyFysh)
            {
                const string relicId = "RELIC.LUCKY_FYSH";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildLuckyFyshBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Lucky Fysh", "SpireLens", body);
                return;
            }

            if (relicNode.Model is BookOfFiveRings bookOfFiveRings)
            {
                const string relicId = "RELIC.BOOK_OF_FIVE_RINGS";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildBookOfFiveRingsBodyBBCode(
                    agg,
                    RunTracker.GetCurrentFloorForRateStats(),
                    RelicFloorAddedToDeck(bookOfFiveRings));
                StatsTooltip.Show(tree, __instance, "Book of Five Rings", "SpireLens", body);
                return;
            }

            if (relicNode.Model is SignetRing)
            {
                const string relicId = "RELIC.SIGNET_RING";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildSignetRingBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Signet Ring", "SpireLens", body);
                return;
            }

            if (relicNode.Model is LeafyPoultice)
            {
                const string relicId = "RELIC.LEAFY_POULTICE";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildLeafyPoulticeBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Leafy Poultice", "SpireLens", body);
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

            if (relicNode.Model is HeftyTablet)
            {
                const string relicId = "RELIC.HEFTY_TABLET";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildHeftyTabletBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Hefty Tablet", "SpireLens", body);
                return;
            }

            if (relicNode.Model is ArcaneScroll)
            {
                const string relicId = "RELIC.ARCANE_SCROLL";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildArcaneScrollBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Arcane Scroll", "SpireLens", body);
                return;
            }

            if (relicNode.Model is LargeCapsule)
            {
                const string relicId = "RELIC.LARGE_CAPSULE";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildLargeCapsuleBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Large Capsule", "SpireLens", body);
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

            if (relicNode.Model is PaelsEye paelsEye)
            {
                const string relicId = "RELIC.PAELS_EYE";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildPaelsEyeBodyBBCode(agg, paelsEye.UsedThisCombat);
                StatsTooltip.Show(tree, __instance, "Pael's Eye", "SpireLens", body);
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

            if (relicNode.Model is JuzuBracelet)
            {
                const string relicId = "RELIC.JUZU_BRACELET";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildJuzuBraceletBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Juzu Bracelet", "SpireLens", body);
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

            if (relicNode.Model is CentennialPuzzle centennialPuzzle)
            {
                const string relicId = "RELIC.CENTENNIAL_PUZZLE";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildCentennialPuzzleBodyBBCode(agg, centennialPuzzle.UsedThisCombat);
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

            if (relicNode.Model is Shovel)
            {
                const string relicId = "RELIC.SHOVEL";
                var agg = RunTracker.GetRelicAggregate(relicId) ?? new RelicAggregate();

                var body = BuildShovelBodyBBCode(agg);
                StatsTooltip.Show(tree, __instance, "Shovel", "SpireLens", body);
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

    internal static bool TryBuildInventoryBodyBBCode(
        Node? hoverNode,
        RelicModel relicModel,
        out string title,
        out string body)
    {
        title = "";
        body = "";

        var relicId = GetStatsAggregateId(relicModel);
        var useEndedRun = IsGameOverScreenHover(hoverNode);

        RelicAggregate? aggregate = null;
        CardAggregate? bloodSoakedRoseCurseAgg = null;
        CardAggregate? cursedPearlCurseAgg = null;
        CardAggregate? storybookBrightestFlameAgg = null;
        IReadOnlyDictionary<string, CardAggregate>? neowsBonesCurseAggs = null;
        int? floorCount = null;

        if (useEndedRun)
        {
            RunTracker.TryLoadLastEndedRunForCurrentGameStartTime();
            aggregate = RunTracker.GetLastEndedRelicAggregate(relicId);
            floorCount = RunTracker.GetLastEndedFloorForRateStats();
            if (relicModel is BloodSoakedRose)
            {
                bloodSoakedRoseCurseAgg =
                    RunTracker.GetLastEndedPooledCardAggregateByDefinition(EnthralledDefinitionId)
                    ?? new CardAggregate();
            }
            else if (relicModel is CursedPearl)
            {
                cursedPearlCurseAgg =
                    RunTracker.GetLastEndedPooledCardAggregateByDefinition(CursedPearlCurseDefinitionId)
                    ?? new CardAggregate();
            }
            else if (relicModel is Storybook)
            {
                storybookBrightestFlameAgg =
                    RunTracker.GetLastEndedPooledCardAggregateByDefinition(BrightestFlameDefinitionId)
                    ?? new CardAggregate();
            }
            else if (relicModel is NeowsBones && aggregate != null)
            {
                neowsBonesCurseAggs = BuildGrantedCurseAggregates(
                    aggregate,
                    definitionId => RunTracker.GetLastEndedPooledCardAggregateByDefinition(definitionId)
                                    ?? new CardAggregate());
            }
        }

        aggregate ??= IsStrikeDummyStatsRelicModel(relicModel)
            ? RunTracker.GetStrikeDummyAggregate()
            : IsMiniatureCannonStatsRelicModel(relicModel)
                ? RunTracker.GetMiniatureCannonAggregate()
            : RunTracker.GetRelicAggregate(relicId);

        if (!useEndedRun && relicModel is DowsingRod)
        {
            var liveRoomsRemaining = RunTracker.GetLiveDowsingRoomsRemaining();
            if (liveRoomsRemaining.HasValue)
            {
                aggregate ??= new RelicAggregate();
                aggregate.DowsingQuestionRoomsRemaining = liveRoomsRemaining.Value;
            }
        }

        if (relicModel is BloodSoakedRose && bloodSoakedRoseCurseAgg == null)
            bloodSoakedRoseCurseAgg = RunTracker.GetEnthralledCurseAggregate();
        if (relicModel is CursedPearl && cursedPearlCurseAgg == null)
            cursedPearlCurseAgg = RunTracker.GetCursedPearlCurseAggregate();
        if (relicModel is Storybook && storybookBrightestFlameAgg == null)
            storybookBrightestFlameAgg = RunTracker.GetPooledCardAggregateByDefinition(BrightestFlameDefinitionId);
        if (relicModel is NeowsBones && neowsBonesCurseAggs == null)
        {
            neowsBonesCurseAggs = BuildGrantedCurseAggregates(
                aggregate ?? new RelicAggregate(),
                RunTracker.GetPooledCardAggregateByDefinition);
        }

        floorCount ??= RunTracker.GetCurrentFloorForRateStats();

        return TryBuildBodyBBCode(
            relicModel,
            aggregate ?? new RelicAggregate(),
            floorCount,
            bloodSoakedRoseCurseAgg,
            cursedPearlCurseAgg,
            neowsBonesCurseAggs,
            storybookBrightestFlameAgg,
            out title,
            out body);
    }

    internal static string GetStatsAggregateId(RelicModel relicModel)
    {
        if (IsAnchorStatsRelicModel(relicModel))
            return "RELIC.ANCHOR";

        if (IsStrikeDummyStatsRelicModel(relicModel))
            return "RELIC.STRIKE_DUMMY";

        if (IsMiniatureCannonStatsRelicModel(relicModel))
            return "RELIC.MINIATURE_CANNON";

        if (IsMrStrugglesStatsRelicModel(relicModel))
            return "RELIC.MR_STRUGGLES";

        if (relicModel is Storybook)
            return "RELIC.STORYBOOK";

        return relicModel.Id.ToString();
    }

    private static int? RelicFloorAddedToDeck(RelicModel relicModel)
    {
        try
        {
            var floor = relicModel.FloorAddedToDeck;
            return floor > 0 ? floor : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsGameOverScreenHover(Node? node)
    {
        for (var current = node; current != null; current = current.GetParent())
        {
            for (var type = current.GetType(); type != null; type = type.BaseType)
            {
                if (string.Equals(type.Namespace, GameOverScreenNamespace, StringComparison.Ordinal)
                    && type.Name.StartsWith("NGameOverScreen", StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    internal static bool TryBuildBodyBBCode(
        RelicModel relicModel,
        RelicAggregate agg,
        int? floorCount,
        out string title,
        out string body)
    {
        return TryBuildBodyBBCode(relicModel, agg, floorCount, null, null, null, null, out title, out body);
    }

    internal static float? GetPreferredStatsTooltipWidth(RelicModel? relicModel)
        => relicModel is SturdyClamp ? SturdyClampTooltipWidth : null;

    internal static bool TryBuildBodyBBCode(
        RelicModel relicModel,
        RelicAggregate agg,
        int? floorCount,
        CardAggregate? bloodSoakedRoseCurseAgg,
        CardAggregate? cursedPearlCurseAgg,
        IReadOnlyDictionary<string, CardAggregate>? neowsBonesCurseAggs,
        out string title,
        out string body)
    {
        return TryBuildBodyBBCode(
            relicModel,
            agg,
            floorCount,
            bloodSoakedRoseCurseAgg,
            cursedPearlCurseAgg,
            neowsBonesCurseAggs,
            null,
            out title,
            out body);
    }

    internal static bool TryBuildBodyBBCode(
        RelicModel relicModel,
        RelicAggregate agg,
        int? floorCount,
        CardAggregate? bloodSoakedRoseCurseAgg,
        CardAggregate? cursedPearlCurseAgg,
        IReadOnlyDictionary<string, CardAggregate>? neowsBonesCurseAggs,
        CardAggregate? storybookBrightestFlameAgg,
        out string title,
        out string body)
    {
        title = "";
        body = "";
        agg ??= new RelicAggregate();

        if (relicModel is BagOfMarbles)
        {
            title = "Bag of Marbles";
            body = BuildBagOfMarblesBodyBBCode(agg);
            return true;
        }

        if (relicModel is RedMask)
        {
            title = "Red Mask";
            body = BuildRedMaskBodyBBCode(agg);
            return true;
        }

        if (relicModel is UnsettlingLamp)
        {
            title = "Unsettling Lamp";
            body = BuildUnsettlingLampBodyBBCode(agg);
            return true;
        }

        if (relicModel is Pocketwatch)
        {
            title = "Pocketwatch";
            body = BuildPocketwatchBodyBBCode(agg);
            return true;
        }

        if (relicModel is Orichalcum)
        {
            title = "Orichalcum";
            body = BuildOrichalcumBodyBBCode(agg);
            return true;
        }

        if (relicModel is Permafrost permafrost)
        {
            title = "Permafrost";
            body = BuildPermafrostBodyBBCode(agg, IsPermafrostActivatedThisCombat(permafrost));
            return true;
        }

        if (relicModel is Vambrace vambrace)
        {
            title = "Vambrace";
            body = BuildVambraceBodyBBCode(agg, IsVambraceUsedThisCombat(vambrace));
            return true;
        }

        if (relicModel is TuningFork)
        {
            title = "Tuning Fork";
            body = BuildTuningForkBodyBBCode(agg);
            return true;
        }

        if (relicModel is RippleBasin)
        {
            title = "Ripple Basin";
            body = BuildRippleBasinBodyBBCode(agg);
            return true;
        }

        if (relicModel is TheAbacus)
        {
            title = "The Abacus";
            body = BuildTheAbacusBodyBBCode(agg);
            return true;
        }

        if (IsAnchorStatsRelicModel(relicModel))
        {
            title = IsFakeAnchorRelicModel(relicModel) ? "???" : "Anchor";
            body = BuildAnchorBodyBBCode(agg);
            return true;
        }

        if (IsRelicModel(relicModel, "MegaCrit.Sts2.Core.Models.Relics.LetterOpener"))
        {
            title = "Letter Opener";
            body = BuildLetterOpenerBodyBBCode(agg);
            return true;
        }

        if (IsRelicModel(relicModel, "MegaCrit.Sts2.Core.Models.Relics.Akabeko"))
        {
            title = "Akabeko";
            body = BuildAkabekoBodyBBCode(agg);
            return true;
        }

        if (relicModel is BookRepairKnife)
        {
            title = "Book Repair Knife";
            body = BuildBookRepairKnifeBodyBBCode(agg);
            return true;
        }

        if (relicModel is EternalFeather)
        {
            title = "Eternal Feather";
            body = BuildEternalFeatherBodyBBCode(agg);
            return true;
        }

        if (relicModel is BoneFlute)
        {
            title = "Bone Flute";
            body = BuildBoneFluteBodyBBCode(agg);
            return true;
        }

        if (relicModel is ArtOfWar)
        {
            title = "Art of War";
            body = BuildArtOfWarBodyBBCode(agg);
            return true;
        }

        if (relicModel is CrackedCore)
        {
            title = "Cracked Core";
            body = BuildCrackedCoreBodyBBCode(agg);
            return true;
        }

        if (relicModel is HappyFlower)
        {
            title = "Happy Flower";
            body = BuildHappyFlowerBodyBBCode(agg);
            return true;
        }

        if (relicModel is Nunchaku)
        {
            title = "Nunchaku";
            body = BuildNunchakuBodyBBCode(agg);
            return true;
        }

        if (relicModel is IronClub)
        {
            title = "Iron Club";
            body = BuildIronClubBodyBBCode(agg);
            return true;
        }

        if (relicModel is Vajra)
        {
            title = "Vajra";
            body = BuildVajraBodyBBCode(agg);
            return true;
        }

        if (relicModel is Kunai)
        {
            title = "Kunai";
            body = BuildKunaiBodyBBCode(agg);
            return true;
        }

        if (relicModel is Kusarigama)
        {
            title = "Kusarigama";
            body = BuildKusarigamaBodyBBCode(agg);
            return true;
        }

        if (relicModel is OrnamentalFan)
        {
            title = "Ornamental Fan";
            body = BuildOrnamentalFanBodyBBCode(agg);
            return true;
        }

        if (relicModel is Shuriken)
        {
            title = "Shuriken";
            body = BuildShurikenBodyBBCode(agg);
            return true;
        }

        if (relicModel is RuinedHelmet)
        {
            title = "Ruined Helmet";
            body = BuildRuinedHelmetBodyBBCode(agg);
            return true;
        }

        if (relicModel is PaperPhrog)
        {
            title = "Paper Phrog";
            body = BuildPaperPhrogBodyBBCode(agg);
            return true;
        }

        if (relicModel is Lantern)
        {
            title = "Lantern";
            body = BuildLanternBodyBBCode(agg);
            return true;
        }

        if (relicModel is VeryHotCocoa)
        {
            title = "Very Hot Cocoa";
            body = BuildVeryHotCocoaBodyBBCode(agg);
            return true;
        }

        if (relicModel is Candelabra)
        {
            title = "Candelabra";
            body = BuildCandelabraBodyBBCode(agg);
            return true;
        }

        if (relicModel is Chandelier)
        {
            title = "Chandelier";
            body = BuildChandelierBodyBBCode(agg);
            return true;
        }

        if (IsRelicModel(relicModel, "MegaCrit.Sts2.Core.Models.Relics.BoomingConch"))
        {
            title = "Booming Conch";
            body = BuildBoomingConchBodyBBCode(agg);
            return true;
        }

        if (relicModel is GremlinHorn)
        {
            title = "Gremlin Horn";
            body = BuildGremlinHornBodyBBCode(agg);
            return true;
        }

        if (relicModel is Pendulum)
        {
            title = "Pendulum";
            body = BuildPendulumBodyBBCode(agg);
            return true;
        }

        if (relicModel is MercuryHourglass)
        {
            title = "Mercury Hourglass";
            body = BuildMercuryHourglassBodyBBCode(agg);
            return true;
        }

        if (relicModel is MrStruggles)
        {
            title = "Mr. Struggles";
            body = BuildMrStrugglesBodyBBCode(agg);
            return true;
        }

        if (relicModel is ParryingShield)
        {
            title = "Parrying Shield";
            body = BuildParryingShieldBodyBBCode(agg);
            return true;
        }

        if (relicModel is FestivePopper)
        {
            title = "Festive Popper";
            body = BuildFestivePopperBodyBBCode(agg);
            return true;
        }

        if (relicModel is BronzeScales)
        {
            title = "Bronze Scales";
            body = BuildBronzeScalesBodyBBCode(agg);
            return true;
        }

        if (relicModel is PenNib)
        {
            title = "Pen Nib";
            body = BuildPenNibBodyBBCode(agg);
            return true;
        }

        if (relicModel is HornCleat)
        {
            title = "Horn Cleat";
            body = BuildHornCleatBodyBBCode(agg);
            return true;
        }

        if (relicModel is PrismaticGem)
        {
            title = "Prismatic Gem";
            body = BuildPrismaticGemBodyBBCode(agg);
            return true;
        }

        if (relicModel is SealOfGold)
        {
            title = "Seal of Gold";
            body = BuildSealOfGoldBodyBBCode(agg);
            return true;
        }

        if (relicModel is FresnelLens)
        {
            title = "Fresnel Lens";
            body = BuildFresnelLensBodyBBCode(agg);
            return true;
        }

        if (relicModel is FishingRod)
        {
            title = "Fishing Rod";
            body = BuildFishingRodBodyBBCode(agg);
            return true;
        }

        if (relicModel is MoltenEgg)
        {
            title = "Molten Egg";
            body = BuildEggBodyBBCode(agg, "attacks");
            return true;
        }

        if (relicModel is ToxicEgg)
        {
            title = "Toxic Egg";
            body = BuildEggBodyBBCode(agg, "skills");
            return true;
        }

        if (relicModel is FrozenEgg)
        {
            title = "Frozen Egg";
            body = BuildEggBodyBBCode(agg, "powers");
            return true;
        }

        if (relicModel is SilverCrucible)
        {
            title = "Silver Crucible";
            body = BuildSilverCrucibleBodyBBCode(agg);
            return true;
        }

        if (relicModel is Orrery)
        {
            title = "Orrery";
            body = BuildOrreryBodyBBCode(agg);
            return true;
        }

        if (relicModel is BloodSoakedRose)
        {
            title = "Blood-Soaked Rose";
            body = BuildBloodSoakedRoseBodyBBCode(agg, bloodSoakedRoseCurseAgg ?? new CardAggregate());
            return true;
        }

        if (relicModel is Storybook)
        {
            title = "Storybook";
            body = BuildStorybookBodyBBCode(storybookBrightestFlameAgg ?? new CardAggregate());
            return true;
        }

        if (relicModel is Regalite)
        {
            title = "Regalite";
            body = BuildRegaliteBodyBBCode(agg);
            return true;
        }

        if (relicModel is IntimidatingHelmet)
        {
            title = "Intimidating Helmet";
            body = BuildIntimidatingHelmetBodyBBCode(agg);
            return true;
        }

        if (relicModel is DaughterOfTheWind)
        {
            title = "Daughter of the Wind";
            body = BuildDaughterOfTheWindBodyBBCode(agg);
            return true;
        }

        if (relicModel is SturdyClamp)
        {
            title = "Sturdy Clamp";
            body = BuildSturdyClampBodyBBCode(agg);
            return true;
        }

        if (relicModel is CursedPearl)
        {
            title = "Cursed Pearl";
            body = BuildCursedPearlBodyBBCode(agg, cursedPearlCurseAgg ?? new CardAggregate());
            return true;
        }

        if (relicModel is NeowsBones)
        {
            title = "Neow's Bones";
            body = BuildNeowsBonesBodyBBCode(agg, neowsBonesCurseAggs);
            return true;
        }

        if (relicModel is CloakClasp)
        {
            title = "Cloak Clasp";
            body = BuildCloakClaspBodyBBCode(agg);
            return true;
        }

        if (relicModel is ReptileTrinket)
        {
            title = "Reptile Trinket";
            body = BuildReptileTrinketBodyBBCode(agg);
            return true;
        }

        if (relicModel is Gorget)
        {
            title = "Gorget";
            body = BuildGorgetBodyBBCode(agg);
            return true;
        }

        if (relicModel is StoneCracker)
        {
            title = "Stone Cracker";
            body = BuildStoneCrackerBodyBBCode(agg);
            return true;
        }

        if (relicModel is RazorTooth)
        {
            title = "Razor Tooth";
            body = BuildRazorToothBodyBBCode(agg);
            return true;
        }

        if (relicModel is WarHammer)
        {
            title = "War Hammer";
            body = BuildWarHammerBodyBBCode(agg);
            return true;
        }

        if (relicModel is GnarledHammer)
        {
            title = "Gnarled Hammer";
            body = BuildGnarledHammerBodyBBCode(agg);
            return true;
        }

        if (relicModel is Whetstone)
        {
            title = "Whetstone";
            body = BuildWhetstoneBodyBBCode(agg);
            return true;
        }

        if (relicModel is WarPaint)
        {
            title = "War Paint";
            body = BuildWarPaintBodyBBCode(agg);
            return true;
        }

        if (relicModel is FragrantMushroom)
        {
            title = "Fragrant Mushroom";
            body = BuildFragrantMushroomBodyBBCode(agg);
            return true;
        }

        if (relicModel is SandCastle)
        {
            title = "Sand Castle";
            body = BuildSandCastleBodyBBCode(agg);
            return true;
        }

        if (relicModel is MealTicket)
        {
            title = "Meal Ticket";
            body = BuildMealTicketBodyBBCode(agg);
            return true;
        }

        if (relicModel is Planisphere)
        {
            title = "Planisphere";
            body = BuildPlanisphereBodyBBCode(agg);
            return true;
        }

        if (relicModel is LizardTail)
        {
            title = "Lizard Tail";
            body = BuildLizardTailBodyBBCode(agg, RelicFloorAddedToDeck(relicModel));
            return true;
        }

        if (relicModel is Pantograph)
        {
            title = "Pantograph";
            body = BuildPantographBodyBBCode(agg);
            return true;
        }

        if (relicModel is BurningBlood)
        {
            title = "Burning Blood";
            body = BuildBurningBloodBodyBBCode(agg);
            return true;
        }

        if (relicModel is LeesWaffle)
        {
            title = "Lee's Waffle";
            body = BuildLeesWaffleBodyBBCode(agg);
            return true;
        }

        if (relicModel is Strawberry)
        {
            title = "Strawberry";
            body = BuildStrawberryBodyBBCode(agg);
            return true;
        }

        if (relicModel is Pear)
        {
            title = "Pear";
            body = BuildPearBodyBBCode(agg);
            return true;
        }

        if (relicModel is NutritiousOyster)
        {
            title = "Nutritious Oyster";
            body = BuildNutritiousOysterBodyBBCode(agg);
            return true;
        }

        if (relicModel is Mango)
        {
            title = "Mango";
            body = BuildMangoBodyBBCode(agg);
            return true;
        }

        if (relicModel is StoneHumidifier)
        {
            title = "Stone Humidifier";
            body = BuildStoneHumidifierBodyBBCode(agg);
            return true;
        }

        if (relicModel is ChosenCheese)
        {
            title = "Chosen Cheese";
            body = BuildChosenCheeseBodyBBCode(agg);
            return true;
        }

        if (relicModel is DarkstonePeriapt)
        {
            title = "Darkstone Periapt";
            body = BuildDarkstonePeriaptBodyBBCode(agg);
            return true;
        }

        if (relicModel is LuckyFysh)
        {
            title = "Lucky Fysh";
            body = BuildLuckyFyshBodyBBCode(agg);
            return true;
        }

        if (relicModel is BookOfFiveRings)
        {
            title = "Book of Five Rings";
            body = BuildBookOfFiveRingsBodyBBCode(
                agg,
                floorCount,
                RelicFloorAddedToDeck(relicModel));
            return true;
        }

        if (relicModel is SignetRing)
        {
            title = "Signet Ring";
            body = BuildSignetRingBodyBBCode(agg);
            return true;
        }

        if (relicModel is LeafyPoultice)
        {
            title = "Leafy Poultice";
            body = BuildLeafyPoulticeBodyBBCode(agg);
            return true;
        }

        if (relicModel is RegalPillow)
        {
            title = "Regal Pillow";
            body = BuildRegalPillowBodyBBCode(agg);
            return true;
        }

        if (relicModel is PrecariousShears)
        {
            title = "Precarious Shears";
            body = BuildPrecariousShearsBodyBBCode(agg);
            return true;
        }

        if (IsRelicModel(relicModel, "MegaCrit.Sts2.Core.Models.Relics.BloodVial"))
        {
            title = "Blood Vial";
            body = BuildBloodVialBodyBBCode(agg);
            return true;
        }

        if (IsRelicModel(relicModel, "MegaCrit.Sts2.Core.Models.Relics.Toolbox"))
        {
            title = "Toolbox";
            body = BuildToolboxBodyBBCode(agg);
            return true;
        }

        if (relicModel is HeftyTablet)
        {
            title = "Hefty Tablet";
            body = BuildHeftyTabletBodyBBCode(agg);
            return true;
        }

        if (relicModel is ArcaneScroll)
        {
            title = "Arcane Scroll";
            body = BuildArcaneScrollBodyBBCode(agg);
            return true;
        }

        if (relicModel is LargeCapsule)
        {
            title = "Large Capsule";
            body = BuildLargeCapsuleBodyBBCode(agg);
            return true;
        }

        if (relicModel is PaelsTooth)
        {
            title = "Pael's Tooth";
            body = BuildPaelsToothBodyBBCode(agg);
            return true;
        }

        if (relicModel is PaelsClaw)
        {
            title = "Pael's Claw";
            body = BuildPaelsClawBodyBBCode(agg);
            return true;
        }

        if (relicModel is PaelsWing)
        {
            title = "Pael's Wing";
            body = BuildPaelsWingBodyBBCode(agg);
            return true;
        }

        if (relicModel is PaelsEye paelsEye)
        {
            title = "Pael's Eye";
            body = BuildPaelsEyeBodyBBCode(agg, paelsEye.UsedThisCombat);
            return true;
        }

        if (IsStrikeDummyStatsRelicModel(relicModel))
        {
            title = IsFakeStrikeDummyRelicModel(relicModel) ? "???" : "Strike Dummy";
            body = BuildStrikeDummyBodyBBCode(agg);
            return true;
        }

        if (relicModel is NutritiousSoup)
        {
            title = "Nutritious Soup";
            body = BuildNutritiousSoupBodyBBCode(agg);
            return true;
        }

        if (IsMiniatureCannonStatsRelicModel(relicModel))
        {
            title = "Miniature Cannon";
            body = BuildMiniatureCannonBodyBBCode(agg);
            return true;
        }

        if (relicModel is Bookmark)
        {
            title = "Bookmark";
            body = BuildBookmarkBodyBBCode(agg);
            return true;
        }

        if (relicModel is BrilliantScarf)
        {
            title = "Brilliant Scarf";
            body = BuildBrilliantScarfBodyBBCode(agg);
            return true;
        }

        if (relicModel is MummifiedHand)
        {
            title = "Mummified Hand";
            body = BuildMummifiedHandBodyBBCode(agg);
            return true;
        }

        if (relicModel is JuzuBracelet)
        {
            title = "Juzu Bracelet";
            body = BuildJuzuBraceletBodyBBCode(agg);
            return true;
        }

        if (relicModel is DowsingRod)
        {
            title = "Dowsing Rod";
            body = BuildDowsingRodBodyBBCode(agg);
            return true;
        }

        if (relicModel is GamblingChip)
        {
            title = "Gambling Chip";
            body = BuildGamblingChipBodyBBCode(agg);
            return true;
        }

        if (relicModel is CentennialPuzzle centennialPuzzle)
        {
            title = "Centennial Puzzle";
            body = BuildCentennialPuzzleBodyBBCode(agg, centennialPuzzle.UsedThisCombat);
            return true;
        }

        if (relicModel is WhiteBeastStatue)
        {
            title = "White Beast Statue";
            body = BuildWhiteBeastStatueBodyBBCode(agg);
            return true;
        }

        if (relicModel is Shovel)
        {
            title = "Shovel";
            body = BuildShovelBodyBBCode(agg);
            return true;
        }

        if (relicModel is BoundPhylactery)
        {
            title = "Bound Phylactery";
            body = BuildPhylacteryBodyBBCode(agg);
            return true;
        }

        if (relicModel is PhylacteryUnbound)
        {
            title = "Phylactery Unbound";
            body = BuildPhylacteryBodyBBCode(agg);
            return true;
        }

        return false;
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

    private static string BuildUnsettlingLampBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var combats = agg.Activations;
        var vulnerablePerCombat = combats <= 0
            ? 0m
            : (decimal)agg.VulnerableApplied / combats;
        var weakPerCombat = combats <= 0
            ? 0m
            : (decimal)agg.WeakApplied / combats;

        Row3(sb, "Combats held", combats.ToString(), "");
        Row3(sb, VulnerableLabel("vulnerable applied"), agg.VulnerableApplied.ToString(), "");
        Row3(sb, VulnerableLabel("avg vulnerable/combat"), FormatDecimal(vulnerablePerCombat), "");
        Row3(sb, WeakLabel("weak applied"), agg.WeakApplied.ToString(), "");
        Row3(sb, WeakLabel("avg weak/combat"), FormatDecimal(weakPerCombat), "");

        foreach (var effect in OtherUnsettlingLampDebuffs(agg))
        {
            var average = combats <= 0
                ? 0m
                : effect.TotalAmountApplied / combats;
            Row3(sb, RelicEffectLabel(effect, "applied"), FormatDecimal(effect.TotalAmountApplied), "");
            Row3(sb, RelicEffectLabel(effect, "avg/combat"), FormatDecimal(average), "");
        }

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

    private static string BuildPermafrostBodyBBCode(RelicAggregate agg, bool triggeredThisCombat)
    {
        var sb = new StringBuilder();
        var blockPerCombat = agg.Activations <= 0
            ? 0m
            : (decimal)agg.AdditionalBlockGained / agg.Activations;
        var effectivePermafrostCombats = Math.Max(agg.PermafrostCombats, agg.Activations);
        var triggersPerCombat = effectivePermafrostCombats <= 0
            ? 0m
            : (decimal)agg.Activations / effectivePermafrostCombats;
        Row3(sb, "Triggered this combat", triggeredThisCombat ? "true" : "false", "");
        Row3(sb, "Combats triggered", agg.Activations.ToString(), "");
        Row3(sb, "Avg times triggered per combat", FormatDecimal(triggersPerCombat), "");
        Row3(sb, BlockLabel("block gained"), agg.AdditionalBlockGained.ToString(), "");
        Row3(sb, BlockLabel("block gained per combat"), FormatDecimal(blockPerCombat), "");
        return sb.ToString();
    }

    private static bool IsPermafrostActivatedThisCombat(Permafrost permafrost)
    {
        try
        {
            return PermafrostActivatedThisCombatField?.GetValue(permafrost) is true;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildVambraceBodyBBCode(RelicAggregate agg, bool usedThisCombat = false)
    {
        var sb = new StringBuilder();
        var blockPerActivation = agg.Activations <= 0
            ? 0m
            : (decimal)agg.AdditionalBlockGained / agg.Activations;
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        Row3(sb, "Used this combat", usedThisCombat ? "true" : "false", "");
        Row3(sb, BlockLabel("extra block gained"), agg.AdditionalBlockGained.ToString(), "");
        Row3(sb, BlockLabel("extra block per activation"), FormatDecimal(blockPerActivation), "");
        return sb.ToString();
    }

    private static string BuildTuningForkBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var blockPerActivation = agg.Activations <= 0
            ? 0m
            : (decimal)agg.AdditionalBlockGained / agg.Activations;
        var skillsPerCombat = agg.TuningForkCombats <= 0
            ? 0m
            : (decimal)agg.TuningForkSkillsPlayed / agg.TuningForkCombats;
        var skillsPerTurn = agg.TuningForkTurns <= 0
            ? 0m
            : (decimal)agg.TuningForkSkillsPlayed / agg.TuningForkTurns;
        var averageEndCharge = agg.TuningForkTurnEndChargeCount <= 0
            ? 0m
            : (decimal)agg.TuningForkTurnEndChargeTotal / agg.TuningForkTurnEndChargeCount;

        Row3(sb, "Skills played", agg.TuningForkSkillsPlayed.ToString(), "");
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        Row3(sb, BlockLabel("block gained"), agg.AdditionalBlockGained.ToString(), "");
        Row3(sb, BlockLabel("block gained per activation"), FormatDecimal(blockPerActivation), "");
        Row3(sb, "Avg skills played per combat", FormatDecimal(skillsPerCombat), "");
        Row3(sb, "Avg skills played per turn", FormatDecimal(skillsPerTurn), "");
        Row3(sb, "Turns ended on 8 charges", agg.TuningForkTurnsEndedOn8Charges.ToString(), "");
        Row3(sb, "Turns ended on 9 charges", agg.TuningForkTurnsEndedOn9Charges.ToString(), "");
        Row3(sb, "Avg charge at turn end", FormatDecimal(averageEndCharge), "");
        return sb.ToString();
    }

    private static string BuildRippleBasinBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var blockPerActivation = agg.Activations <= 0
            ? 0m
            : (decimal)agg.AdditionalBlockGained / agg.Activations;
        var blockPerTurn = agg.RippleBasinTurns <= 0
            ? 0m
            : (decimal)agg.AdditionalBlockGained / agg.RippleBasinTurns;
        var blockPerCombat = agg.RippleBasinCombats <= 0
            ? 0m
            : (decimal)agg.AdditionalBlockGained / agg.RippleBasinCombats;

        Row3(sb, "Activations", agg.Activations.ToString(), "");
        Row3(sb, BlockLabel("block gained"), agg.AdditionalBlockGained.ToString(), "");
        Row3(sb, BlockLabel("block gained per activation"), FormatDecimal(blockPerActivation), "");
        Row3(sb, BlockLabel("avg block gained per turn"), FormatDecimal(blockPerTurn), "");
        Row3(sb, BlockLabel("avg block gained per combat"), FormatDecimal(blockPerCombat), "");
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
        var averageDamagePerCombat = agg.LetterOpenerCombats <= 0
            ? 0m
            : (decimal)agg.TotalDamageAttempted / agg.LetterOpenerCombats;
        var averageDamagePerTurn = agg.LetterOpenerTurns <= 0
            ? 0m
            : (decimal)agg.TotalDamageAttempted / agg.LetterOpenerTurns;
        var averageDamagePerSkill = agg.LetterOpenerSkillsPlayed <= 0
            ? 0m
            : (decimal)agg.TotalDamageAttempted / agg.LetterOpenerSkillsPlayed;
        var targetsHitPerActivation = agg.Activations <= 0
            ? 0m
            : (decimal)agg.TotalTargets / agg.Activations;

        Row3(sb, "Activations", agg.Activations.ToString(), "");
        Row3(sb, "Damage attempted", agg.TotalDamageAttempted.ToString(), "");
        Row3(sb, "Targets hit", agg.TotalTargets.ToString(), "");
        Row3(sb, "Targets hit per activation", FormatDecimal(targetsHitPerActivation), "");
        Row3(sb, "Avg damage per combat", FormatDecimal(averageDamagePerCombat), "");
        Row3(sb, "Avg damage per turn", FormatDecimal(averageDamagePerTurn), "");
        Row3(sb, "Turns ended at 1 charge", agg.LetterOpenerTurnsEndedAt1Charge.ToString(), "");
        Row3(sb, "Turns ended at 2 charges", agg.LetterOpenerTurnsEndedAt2Charges.ToString(), "");
        Row3(sb, "Avg damage per skill played", FormatDecimal(averageDamagePerSkill), "");
        return sb.ToString();
    }

    private static string BuildAkabekoBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, VigorLabel("vigor gained"), agg.VigorGained.ToString(), "");
        return sb.ToString();
    }

    private static string BuildBronzeScalesBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        AppendRelicDamageStats(
            sb,
            agg,
            triggerLabel: "Times triggered",
            averageLabel: "Damage per trigger",
            averageDenominator: agg.Activations);
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

    private static string BuildArtOfWarBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var energyPerTurn = agg.ArtOfWarTurns <= 0
            ? 0m
            : (decimal)agg.EnergyGenerated / agg.ArtOfWarTurns;
        var energyPerCombat = agg.EnergyGeneratedCombats <= 0
            ? 0m
            : (decimal)agg.EnergyGenerated / agg.EnergyGeneratedCombats;
        var energyPerTurnThisCombat = agg.ArtOfWarTurnsThisCombat <= 0
            ? 0m
            : (decimal)agg.ArtOfWarEnergyAddedThisCombat / agg.ArtOfWarTurnsThisCombat;

        Row3(sb, EnergyLabel("Total energy gained"), agg.EnergyGenerated.ToString(), "");
        Row3(sb, EnergyLabel("Avg energy gained per turn"), FormatDecimal(energyPerTurn), "");
        Row3(sb, EnergyLabel("Avg energy gained per combat"), FormatDecimal(energyPerCombat), "");
        Row3(
            sb,
            EnergyLabel("Energy added this combat"),
            agg.ArtOfWarEnergyAddedThisCombat.ToString(),
            "");
        Row3(
            sb,
            EnergyLabel("Energy added this turn"),
            agg.ArtOfWarEnergyAddedThisTurn.ToString(),
            "");
        Row3(
            sb,
            EnergyLabel("Avg energy added per turn this combat"),
            FormatDecimal(energyPerTurnThisCombat),
            "");
        return sb.ToString();
    }

    private static string BuildCrackedCoreBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Times orb was evoked", agg.CrackedCoreOrbEvokes.ToString(), "");
        Row3(
            sb,
            "Times orb passive triggered",
            agg.CrackedCoreOrbPassiveTriggers.ToString(),
            "");
        Row3(sb, "Times orb fizzled", agg.CrackedCoreOrbFizzles.ToString(), "");
        return sb.ToString();
    }

    private static string BuildHappyFlowerBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        AppendEnergyGeneratedStats(
            sb,
            agg,
            includeAveragePerCombat: true,
            averageLabel: "Avg energy generated per combat",
            combatCount: agg.EnergyGeneratedCombats,
            includeCombatsHeld: true);
        return sb.ToString();
    }

    private static string BuildNunchakuBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var combats = agg.EnergyGeneratedCombats;
        var averageAttacks = combats <= 0
            ? 0m
            : (decimal)agg.NunchakuAttacksPlayed / combats;
        var averageEnergy = combats <= 0
            ? 0m
            : (decimal)agg.EnergyGenerated / combats;
        var averageEndCharge = combats <= 0
            ? 0m
            : (decimal)agg.NunchakuCombatEndChargeTotal / combats;

        Row3(sb, "Attacks played", agg.NunchakuAttacksPlayed.ToString(), "");
        Row3(sb, "Avg attacks played per combat", FormatDecimal(averageAttacks), "");
        Row3(sb, EnergyLabel("Energy gained total"), agg.EnergyGenerated.ToString(), "");
        Row3(sb, EnergyLabel("Avg energy gained per combat"), FormatDecimal(averageEnergy), "");
        Row3(sb, "Combats ended on 8 charges", agg.NunchakuCombatsEndedOn8Charges.ToString(), "");
        Row3(sb, "Combats ended on 9 charges", agg.NunchakuCombatsEndedOn9Charges.ToString(), "");
        Row3(sb, "Avg charge at combat end", FormatDecimal(averageEndCharge), "");
        return sb.ToString();
    }

    private static string BuildIronClubBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var averageDrawn = agg.IronClubCombats <= 0
            ? 0m
            : (decimal)agg.AdditionalCardsDrawn / agg.IronClubCombats;
        var chargeSamples = agg.IronClubCombatEndChargeCount;
        var chargeTotal = agg.IronClubCombatEndChargeTotal;
        if (chargeSamples <= 0)
        {
            chargeSamples =
                agg.IronClubCombatsEndedOn0Charges
                + agg.IronClubCombatsEndedOn1Charges
                + agg.IronClubCombatsEndedOn2Charges
                + agg.IronClubCombatsEndedOn3Charges;
            chargeTotal =
                agg.IronClubCombatsEndedOn1Charges
                + (agg.IronClubCombatsEndedOn2Charges * 2)
                + (agg.IronClubCombatsEndedOn3Charges * 3);
        }
        var averageEndCharge = chargeSamples <= 0
            ? 0m
            : (decimal)chargeTotal / chargeSamples;

        Row3(sb, "Cards drawn total", agg.AdditionalCardsDrawn.ToString(), "");
        Row3(sb, "Avg cards drawn per combat", FormatDecimal(averageDrawn), "");
        Row3(sb, "Combat ends at 0 charges", agg.IronClubCombatsEndedOn0Charges.ToString(), "");
        Row3(sb, "Combat ends at 1 charge", agg.IronClubCombatsEndedOn1Charges.ToString(), "");
        Row3(sb, "Combat ends at 2 charges", agg.IronClubCombatsEndedOn2Charges.ToString(), "");
        Row3(sb, "Combat ends at 3 charges", agg.IronClubCombatsEndedOn3Charges.ToString(), "");
        Row3(sb, "Avg charge at combat end", FormatDecimal(averageEndCharge), "");
        return sb.ToString();
    }

    private static string BuildLanternBodyBBCode(RelicAggregate agg)
        => BuildTurnEnergyRelicBodyBBCode(
            agg,
            "1st turns ended with excess energy",
            agg.FirstTurnsEndedWithExcessEnergy,
            includeCombatsWithEnergyNotGained: false);

    private static string BuildVeryHotCocoaBodyBBCode(RelicAggregate agg)
        => BuildTurnEnergyRelicBodyBBCode(
            agg,
            "1st turns ended with excess energy",
            agg.FirstTurnsEndedWithExcessEnergy,
            includeCombatsWithEnergyNotGained: false);

    private static string BuildCandelabraBodyBBCode(RelicAggregate agg)
        => BuildTurnEnergyRelicBodyBBCode(
            agg,
            "2nd turns ended with excess energy",
            agg.SecondTurnsEndedWithExcessEnergy,
            includeCombatsWithEnergyNotGained: true);

    private static string BuildChandelierBodyBBCode(RelicAggregate agg)
        => BuildTurnEnergyRelicBodyBBCode(
            agg,
            "3rd turns ended with excess energy",
            agg.ThirdTurnsEndedWithExcessEnergy,
            includeCombatsWithEnergyNotGained: true);

    private static string BuildTurnEnergyRelicBodyBBCode(
        RelicAggregate agg,
        string excessEnergyLabel,
        int turnsEndedWithExcessEnergy,
        bool includeCombatsWithEnergyNotGained)
    {
        var sb = new StringBuilder();
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        AppendEnergyGeneratedStats(sb, agg);
        Row3(sb, excessEnergyLabel, turnsEndedWithExcessEnergy.ToString(), "");
        if (includeCombatsWithEnergyNotGained)
            Row3(sb, "Combats with energy not gained", agg.CombatsWithoutActivation.ToString(), "");
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
        var cardsDrawnPerCombat = agg.PendulumCombats <= 0
            ? 0m
            : (decimal)agg.AdditionalCardsDrawn / agg.PendulumCombats;

        Row3(sb, "Activations", agg.Activations.ToString(), "");
        Row3(sb, "Cards drawn", agg.AdditionalCardsDrawn.ToString(), "");
        Row3(sb, "Avg cards drawn per combat", FormatDecimal(cardsDrawnPerCombat), "");
        return sb.ToString();
    }

    private static string BuildMercuryHourglassBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        AppendRelicDamageStats(
            sb,
            agg,
            triggerLabel: "Combats triggered",
            averageLabel: "Damage per combat",
            averageDenominator: agg.Activations);
        return sb.ToString();
    }

    private static string BuildMrStrugglesBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        AppendRelicDamageStats(
            sb,
            agg,
            triggerLabel: "Activations",
            averageLabel: "Damage per activation",
            averageDenominator: agg.Activations);
        return sb.ToString();
    }

    private static string BuildParryingShieldBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        AppendRelicDamageStats(
            sb,
            agg,
            triggerLabel: "Activations",
            averageLabel: "Damage per activation",
            averageDenominator: agg.Activations);
        return sb.ToString();
    }

    private static string BuildFestivePopperBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        AppendRelicDamageStats(
            sb,
            agg,
            triggerLabel: "Combats triggered",
            averageLabel: "Damage per combat",
            averageDenominator: agg.Activations);
        return sb.ToString();
    }

    private static void AppendRelicDamageStats(
        StringBuilder sb,
        RelicAggregate agg,
        string triggerLabel,
        string? averageLabel = null,
        int averageDenominator = 0)
    {
        Row3(sb, triggerLabel, agg.Activations.ToString(), "");
        Row3(sb, "Damage attempted", agg.TotalDamageAttempted.ToString(), "");
        Row3(sb, "Damage dealt", agg.TotalDamageDealt.ToString(), "");
        Row3(sb, "Damage blocked", agg.TotalDamageBlocked.ToString(), "");
        Row3(sb, "Overkill", agg.TotalDamageOverkill.ToString(), "");
        Row3(sb, "Kills", agg.Kills.ToString(), "");
        Row3(sb, "Targets hit", agg.TotalTargets.ToString(), "");

        if (!string.IsNullOrWhiteSpace(averageLabel))
        {
            var average = averageDenominator <= 0
                ? 0m
                : (decimal)agg.TotalDamageDealt / averageDenominator;
            Row3(sb, averageLabel, FormatDecimal(average), "");
        }
    }

    private static string BuildPenNibBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var averageBaseDamage = agg.PenNibAttacksPlayed <= 0
            ? 0m
            : (decimal)agg.TotalDamageAttempted / agg.PenNibAttacksPlayed;
        var averageEndCharge = agg.PenNibTurnEndChargeCount <= 0
            ? 0m
            : (decimal)agg.PenNibTurnEndChargeTotal / agg.PenNibTurnEndChargeCount;

        Row3(sb, "Base damage added", agg.TotalDamageAttempted.ToString(), "");
        Row3(sb, "Avg base damage added per attack", FormatDecimal(averageBaseDamage), "");
        Row3(sb, "Attacks played", agg.PenNibAttacksPlayed.ToString(), "");
        Row3(sb, "Turns ended on 8 charges", agg.PenNibTurnsEndedOn8Charges.ToString(), "");
        Row3(sb, "Turns ended on 9 charges", agg.PenNibTurnsEndedOn9Charges.ToString(), "");
        Row3(sb, "Avg charge at turn end", FormatDecimal(averageEndCharge), "");
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

    private static string BuildSealOfGoldBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Times triggered", agg.Activations.ToString(), "");
        Row3(sb, "Gold loss attempted", (agg.Activations * SealOfGoldLossPerTrigger).ToString(), "");
        Row3(sb, "Gold lost", agg.GoldLost.ToString(), "");
        Row3(sb, "Gold loss blocked", agg.GoldLossBlocked.ToString(), "");
        AppendEnergyGeneratedStats(
            sb,
            agg,
            totalLabel: "Energy gained total",
            includeAveragePerCombat: true,
            combatCount: agg.EnergyGeneratedCombats);
        return sb.ToString();
    }

    private static string BuildFresnelLensBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        AppendMaxHpChangeRows(sb, agg, "Max HP lost to Drowning Beacon", MaxHpLost(agg));
        Row3(sb, "Nimble cards taken", agg.NimbleCardsTaken.ToString(), "");
        Row3(sb, "Reward screens with Nimble cards", agg.RewardScreensWithNimbleCards.ToString(), "");
        Row3(sb, "Reward screens with 2 Nimble cards", agg.RewardScreensWithTwoNimbleCards.ToString(), "");
        Row3(sb, "Reward screens with 3+ Nimble cards", agg.RewardScreensWithThreeOrMoreNimbleCards.ToString(), "");
        Row3(sb, "Reward screens with no Nimble cards", agg.RewardScreensWithoutNimbleCards.ToString(), "");
        Row3(sb, "Nimble offered, none taken", agg.RewardScreensWithNimbleCardsButNoneTaken.ToString(), "");
        return sb.ToString();
    }

    private static string BuildFishingRodBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        AppendUpgradedCardStats(sb, agg);
        return sb.ToString();
    }

    private static string BuildEggBodyBBCode(RelicAggregate agg, string cardType)
    {
        var sb = new StringBuilder();
        Row3(sb, $"Upgraded {cardType} offered", agg.UpgradedCardsOffered.ToString(), "");
        return sb.ToString();
    }

    private static string BuildSilverCrucibleBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var screens = agg.CardRewardScreens ?? new List<RelicCardRewardScreenAggregate>();

        for (var screenNumber = 1; screenNumber <= 3; screenNumber++)
        {
            var screen = screens.LastOrDefault(candidate =>
                candidate != null && candidate.ScreenNumber == screenNumber);
            if (screen == null)
            {
                Row3(sb, $"Card reward {screenNumber}", "not seen yet", "");
                continue;
            }

            var cards = screen.Cards ?? new List<RelicCardRewardOptionAggregate>();
            if (cards.Count == 0)
            {
                Row3(sb, $"Card reward {screenNumber}", "no cards offered", "");
                continue;
            }

            sb.Append($"[color=#e0e0e0]Card reward {screenNumber}[/color]\n");
            foreach (var card in cards)
            {
                if (card == null) continue;

                var displayName = !string.IsNullOrWhiteSpace(card.DisplayName)
                    ? card.DisplayName
                    : !string.IsNullOrWhiteSpace(card.CardId)
                        ? RunTracker.FormatCardIdForDisplay(card.CardId)
                        : "Unknown card";
                AppendSilverCrucibleCardRow(
                    sb,
                    StatsTooltip.EscapeBbcode(displayName),
                    !screen.Resolved ? "pending" : card.Taken ? "taken" : "not taken");
            }
        }

        return sb.ToString();
    }

    private static void AppendSilverCrucibleCardRow(
        StringBuilder sb,
        string displayName,
        string outcome)
    {
        // Card names need substantially more room than ordinary numeric stat
        // values. Keeping them out of Row3's narrow middle column prevents
        // multi-word names such as Grave Warden from wrapping and staggering.
        sb.Append("[table=2]");
        sb.Append($"[cell expand=4 padding=12,0,12,0][b]{displayName}[/b][/cell]");
        sb.Append($"[cell expand=2 padding=0,0,4,0][right][color=#b5b5b5]{outcome}[/color][/right][/cell]");
        sb.Append("[/table]\n");
    }

    private static string BuildOrreryBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var rewards = agg.OrreryRewards ?? new List<OrreryRewardAggregate>();

        for (var rewardNumber = 1; rewardNumber <= 5; rewardNumber++)
        {
            var reward = rewards.LastOrDefault(candidate =>
                candidate != null && candidate.RewardNumber == rewardNumber);
            Row3(
                sb,
                $"Reward {rewardNumber}",
                reward == null ? "not seen yet" : FormatOrreryRewardOutcome(reward),
                "");
        }

        return sb.ToString();
    }

    private static string FormatOrreryRewardOutcome(OrreryRewardAggregate reward)
    {
        if (string.Equals(reward.Outcome, "skipped", StringComparison.OrdinalIgnoreCase))
            return "skipped";

        if (string.Equals(reward.Outcome, "alternative", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(
                    reward.AlternativeId,
                    PaelsWing.sacrificeAlternativeKey,
                    StringComparison.OrdinalIgnoreCase))
                return "sacrificed to Pael";

            var alternative = string.IsNullOrWhiteSpace(reward.AlternativeId)
                ? "alternative"
                : reward.AlternativeId.Replace('_', ' ').ToLowerInvariant();
            return $"selected {StatsTooltip.EscapeBbcode(alternative)}";
        }

        if (string.Equals(reward.Outcome, "obtained", StringComparison.OrdinalIgnoreCase))
        {
            var cards = (reward.CardsObtained ?? new List<OrreryObtainedCardAggregate>())
                .Where(card => card != null)
                .Select(card =>
                {
                    var displayName = !string.IsNullOrWhiteSpace(card.DisplayName)
                        ? card.DisplayName
                        : !string.IsNullOrWhiteSpace(card.CardId)
                            ? RunTracker.FormatCardIdForDisplay(card.CardId)
                            : "Unknown card";
                    var upgradeSuffix = card.UpgradeLevel <= 0
                        ? ""
                        : new string('+', card.UpgradeLevel);
                    return StatsTooltip.EscapeBbcode(displayName + upgradeSuffix);
                })
                .ToList();
            return cards.Count == 0
                ? "obtained card"
                : $"obtained {string.Join(", ", cards)}";
        }

        if (string.Equals(
                reward.Outcome,
                "completed_without_card",
                StringComparison.OrdinalIgnoreCase))
            return "completed without obtaining a card";

        return "pending";
    }

    private static string BuildBloodSoakedRoseBodyBBCode(RelicAggregate agg, CardAggregate curseAgg)
    {
        var sb = new StringBuilder();
        AppendEnergyGeneratedStats(
            sb,
            agg,
            totalLabel: "Energy gained total",
            includeAveragePerCombat: true);

        AppendRelatedCurseCardStats(sb, "Enthralled", curseAgg);
        return sb.ToString();
    }

    private static string BuildStorybookBodyBBCode(CardAggregate brightestFlameAgg)
    {
        brightestFlameAgg ??= new CardAggregate();

        var sb = new StringBuilder();
        Row3(sb, "Brightest Flame played", brightestFlameAgg.Plays.ToString(), "");
        Row3(sb, DrawLabel("Brightest Flame drawn"), brightestFlameAgg.TimesDrawn.ToString(), "");
        Row3(sb, EnergyLabel("gained by Flame"), brightestFlameAgg.TotalEnergyGenerated.ToString(), "");
        Row3(sb, DrawLabel("Cards drawn by Flame"), brightestFlameAgg.TimesCardsDrawn.ToString(), "");
        Row3(sb, "Max HP lost to Flame", brightestFlameAgg.TotalMaxHpLost.ToString(), "");
        return sb.ToString();
    }

    private static string BuildRegaliteBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var blockPerTurn = agg.RegaliteTurns <= 0
            ? 0m
            : (decimal)agg.AdditionalBlockGained / agg.RegaliteTurns;
        var blockPerCombat = agg.RegaliteCombats <= 0
            ? 0m
            : (decimal)agg.AdditionalBlockGained / agg.RegaliteCombats;

        Row3(sb, "Cards created", agg.RegaliteCardsCreated.ToString(), "");
        Row3(sb, BlockLabel("block gained"), agg.AdditionalBlockGained.ToString(), "");
        Row3(sb, BlockLabel("avg block per turn"), FormatDecimal(blockPerTurn), "");
        Row3(sb, BlockLabel("avg block per combat"), FormatDecimal(blockPerCombat), "");
        return sb.ToString();
    }

    private static string BuildIntimidatingHelmetBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var blockPerTurn = agg.IntimidatingHelmetTurns <= 0
            ? 0m
            : (decimal)agg.AdditionalBlockGained / agg.IntimidatingHelmetTurns;
        var blockPerCombat = agg.IntimidatingHelmetCombats <= 0
            ? 0m
            : (decimal)agg.AdditionalBlockGained / agg.IntimidatingHelmetCombats;

        Row3(sb, "Cards played costing 2+", agg.Activations.ToString(), "");
        Row3(sb, BlockLabel("block gained"), agg.AdditionalBlockGained.ToString(), "");
        Row3(sb, BlockLabel("avg block per turn"), FormatDecimal(blockPerTurn), "");
        Row3(sb, BlockLabel("avg block per combat"), FormatDecimal(blockPerCombat), "");
        return sb.ToString();
    }

    private static string BuildDaughterOfTheWindBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var blockPerTurn = agg.DaughterOfTheWindTurns <= 0
            ? 0m
            : (decimal)agg.AdditionalBlockGained / agg.DaughterOfTheWindTurns;
        var blockPerCombat = agg.DaughterOfTheWindCombats <= 0
            ? 0m
            : (decimal)agg.AdditionalBlockGained / agg.DaughterOfTheWindCombats;

        Row3(sb, BlockLabel("Total block gained"), agg.AdditionalBlockGained.ToString(), "");
        Row3(sb, BlockLabel("Avg block gained per turn"), FormatDecimal(blockPerTurn), "");
        Row3(sb, BlockLabel("Avg block gained per combat"), FormatDecimal(blockPerCombat), "");
        return sb.ToString();
    }

    private static string BuildSturdyClampBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var blockRetainedPerTurn = agg.SturdyClampTurns <= 0
            ? 0m
            : (decimal)agg.SturdyClampBlockRetained / agg.SturdyClampTurns;
        var blockRetainedPerCombat = agg.SturdyClampCombats <= 0
            ? 0m
            : (decimal)agg.SturdyClampBlockRetained / agg.SturdyClampCombats;
        var excessBlockPerTurn = agg.SturdyClampTurns <= 0
            ? 0m
            : (decimal)agg.SturdyClampExcessBlockOverTen / agg.SturdyClampTurns;
        var excessBlockPerCombat = agg.SturdyClampCombats <= 0
            ? 0m
            : (decimal)agg.SturdyClampExcessBlockOverTen / agg.SturdyClampCombats;

        Row3(sb, BlockLabel("avg block retained per turn"), FormatDecimal(blockRetainedPerTurn), "");
        Row3(sb, BlockLabel("avg block retained per combat"), FormatDecimal(blockRetainedPerCombat), "");
        Row3(sb, BlockLabel("avg excess block over 10 per turn"), FormatDecimal(excessBlockPerTurn), "");
        Row3(sb, BlockLabel("avg excess block over 10 per combat"), FormatDecimal(excessBlockPerCombat), "");
        return sb.ToString();
    }

    private static string BuildCursedPearlBodyBBCode(RelicAggregate agg, CardAggregate curseAgg)
    {
        var sb = new StringBuilder();
        Row3(
            sb,
            "Floors ascended before first shop",
            (agg.FloorsAscendedBeforeFirstShop ?? 0).ToString(),
            "");
        AppendRelatedCurseCardStats(sb, "Greed", curseAgg, includePlayed: false);
        return sb.ToString();
    }

    private static string BuildNeowsBonesBodyBBCode(
        RelicAggregate agg,
        IReadOnlyDictionary<string, CardAggregate>? curseAggregates)
    {
        var sb = new StringBuilder();
        var relics = agg.RelicsGranted.Values
            .Where(relic => relic.Count > 0)
            .OrderByDescending(relic => relic.Count)
            .ThenBy(relic => relic.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var relicTotal = relics.Sum(relic => Math.Max(0, relic.Count));

        Row3(sb, "Neow relics obtained", relicTotal.ToString(), "");
        foreach (var relic in relics)
        {
            var displayName = StatsTooltip.EscapeBbcode(string.IsNullOrWhiteSpace(relic.DisplayName)
                ? RunTracker.FormatRelicIdForDisplay(relic.RelicId)
                : relic.DisplayName);
            var value = relic.Count == 1 ? displayName : $"{displayName} x{relic.Count}";
            Row3(sb, "Neow relic", value, "");
        }

        var curses = agg.CardsGranted.Values
            .Where(card => card.Count > 0)
            .OrderByDescending(card => card.Count)
            .ThenBy(card => card.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var curseTotal = curses.Sum(card => Math.Max(0, card.Count));

        Row3(sb, "Curses added", curseTotal.ToString(), "");
        if (curses.Count == 0)
        {
            AppendRelatedCurseCardStats(sb, "Curse", new CardAggregate());
            return sb.ToString();
        }

        foreach (var curse in curses)
        {
            var displayName = StatsTooltip.EscapeBbcode(string.IsNullOrWhiteSpace(curse.DisplayName)
                ? RunTracker.FormatCardIdForDisplay(curse.CardId)
                : curse.DisplayName);
            var value = curse.Count == 1 ? displayName : $"{displayName} x{curse.Count}";
            Row3(sb, "Curse added", value, "");

            CardAggregate? curseAgg = null;
            curseAggregates?.TryGetValue(curse.CardId, out curseAgg);
            AppendRelatedCurseCardStats(sb, displayName, curseAgg ?? new CardAggregate());
        }

        return sb.ToString();
    }

    private static void AppendRelatedCurseCardStats(
        StringBuilder sb,
        string displayName,
        CardAggregate curseAgg,
        bool includePlayed = true)
    {
        curseAgg ??= new CardAggregate();
        Row3(sb, $"{displayName} combats", curseAgg.CombatsInDeck.ToString(), "");
        Row3(sb, $"{displayName} drawn", curseAgg.TimesDrawn.ToString(), "");
        Row3(sb, $"{displayName} discarded", curseAgg.TimesDiscarded.ToString(), "");
        if (includePlayed)
            Row3(sb, $"{displayName} played", curseAgg.Plays.ToString(), "");
        Row3(sb, $"{displayName} exhausted", curseAgg.TimesExhausted.ToString(), "");
    }

    private static IReadOnlyDictionary<string, CardAggregate> BuildGrantedCurseAggregates(
        RelicAggregate agg,
        Func<string, CardAggregate> aggregateForDefinition)
    {
        var result = new Dictionary<string, CardAggregate>(StringComparer.Ordinal);
        foreach (var card in agg.CardsGranted.Values)
        {
            if (card.Count <= 0 || string.IsNullOrWhiteSpace(card.CardId)) continue;
            result[card.CardId] = aggregateForDefinition(card.CardId) ?? new CardAggregate();
        }

        return result;
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
        var activationsPerTurn = agg.ReptileTrinketTurns <= 0
            ? 0m
            : (decimal)agg.Activations / agg.ReptileTrinketTurns;
        var activationsPerCombat = agg.ReptileTrinketCombats <= 0
            ? 0m
            : (decimal)agg.Activations / agg.ReptileTrinketCombats;

        Row3(sb, "Activations", agg.Activations.ToString(), "");
        Row3(sb, "Strength added", FormatDecimal(agg.StrengthAdded), "");
        Row3(sb, "Avg activations per turn", FormatDecimal(activationsPerTurn), "");
        Row3(sb, "Avg activations per combat", FormatDecimal(activationsPerCombat), "");
        Row3(
            sb,
            "Turns with exactly 2 activations",
            agg.ReptileTrinketTurnsWithExactlyTwoActivations.ToString(),
            "");
        Row3(
            sb,
            "Turns with more than 2 activations",
            agg.ReptileTrinketTurnsWithMoreThanTwoActivations.ToString(),
            "");
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

    private static string BuildRazorToothBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var upgradedPerTurn = agg.RazorToothTurns <= 0
            ? 0m
            : (decimal)agg.CardsUpgraded / agg.RazorToothTurns;
        var upgradedPerCombat = agg.RazorToothCombats <= 0
            ? 0m
            : (decimal)agg.CardsUpgraded / agg.RazorToothCombats;
        var upgradedPlaysPerTurn = agg.RazorToothTurns <= 0
            ? 0m
            : (decimal)agg.RazorToothUpgradedCardPlays / agg.RazorToothTurns;
        var upgradedPlaysPerCombat = agg.RazorToothCombats <= 0
            ? 0m
            : (decimal)agg.RazorToothUpgradedCardPlays / agg.RazorToothCombats;
        var upgradedDrawsPerTurn = agg.RazorToothTurns <= 0
            ? 0m
            : (decimal)agg.RazorToothUpgradedCardDraws / agg.RazorToothTurns;
        var upgradedDrawsPerCombat = agg.RazorToothCombats <= 0
            ? 0m
            : (decimal)agg.RazorToothUpgradedCardDraws / agg.RazorToothCombats;

        Row3(sb, "Cards upgraded", agg.CardsUpgraded.ToString(), "");
        Row3(sb, "Avg cards upgraded/turn", FormatDecimal(upgradedPerTurn), "");
        Row3(sb, "Avg cards upgraded/combat", FormatDecimal(upgradedPerCombat), "");
        Row3(sb, "Upgraded-card plays", agg.RazorToothUpgradedCardPlays.ToString(), "");
        Row3(sb, "Avg upgraded plays/turn", FormatDecimal(upgradedPlaysPerTurn), "");
        Row3(sb, "Avg upgraded plays/combat", FormatDecimal(upgradedPlaysPerCombat), "");
        Row3(sb, "Upgraded-card draws", agg.RazorToothUpgradedCardDraws.ToString(), "");
        Row3(sb, "Avg upgraded draws/turn", FormatDecimal(upgradedDrawsPerTurn), "");
        Row3(sb, "Avg upgraded draws/combat", FormatDecimal(upgradedDrawsPerCombat), "");
        return sb.ToString();
    }

    private static string BuildWarHammerBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var cardsPerActivation = agg.Activations <= 0
            ? 0m
            : (decimal)agg.CardsUpgraded / agg.Activations;
        var upgradedPlaysPerTurn = agg.WarHammerTurns <= 0
            ? 0m
            : (decimal)agg.WarHammerUpgradedCardPlays / agg.WarHammerTurns;
        var upgradedPlaysPerCombat = agg.WarHammerCombats <= 0
            ? 0m
            : (decimal)agg.WarHammerUpgradedCardPlays / agg.WarHammerCombats;

        Row3(sb, "Activations", agg.Activations.ToString(), "");
        Row3(sb, "Cards upgraded", agg.CardsUpgraded.ToString(), "");
        Row3(sb, "Avg cards upgraded/activation", FormatDecimal(cardsPerActivation), "");
        foreach (var card in (agg.UpgradedCards ?? new List<string>())
                     .Where(card => !string.IsNullOrWhiteSpace(card)))
        {
            RowFlow(sb, "Upgraded card", StatsTooltip.EscapeBbcode(card), "");
        }
        Row3(sb, "Upgraded-card plays", agg.WarHammerUpgradedCardPlays.ToString(), "");
        Row3(sb, "Avg upgraded plays/turn", FormatDecimal(upgradedPlaysPerTurn), "");
        Row3(sb, "Avg upgraded plays/combat", FormatDecimal(upgradedPlaysPerCombat), "");
        return sb.ToString();
    }

    private static string BuildSandCastleBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        AppendUpgradedCardStats(sb, agg);
        return sb.ToString();
    }

    private static string BuildWhetstoneBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        AppendUpgradedCardStats(sb, agg);
        return sb.ToString();
    }

    private static string BuildGnarledHammerBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var cards = (agg.SharpEnchantedCards ?? new List<string>())
            .Where(card => !string.IsNullOrWhiteSpace(card))
            .ToList();

        Row3(sb, "Cards enchanted with Sharp", cards.Count.ToString(), "");
        foreach (var card in cards)
            RowFlow(sb, "Sharp-enchanted card", StatsTooltip.EscapeBbcode(card), "");
        return sb.ToString();
    }

    private static string BuildWarPaintBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        AppendUpgradedCardStats(sb, agg);
        return sb.ToString();
    }

    private static string BuildFragrantMushroomBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        AppendUpgradedCardStats(sb, agg);
        return sb.ToString();
    }

    private static void AppendUpgradedCardStats(StringBuilder sb, RelicAggregate agg)
    {
        var upgradedCards = (agg.UpgradedCards ?? new System.Collections.Generic.List<string>())
            .Where(card => !string.IsNullOrWhiteSpace(card))
            .ToList();

        Row3(sb, "Cards upgraded", agg.CardsUpgraded.ToString(), "");
        foreach (var card in upgradedCards)
            RowFlow(sb, "Upgraded card", StatsTooltip.EscapeBbcode(card), "");
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

    private static string BuildLizardTailBodyBBCode(RelicAggregate agg, int? floorAcquiredFallback = null)
    {
        var sb = new StringBuilder();
        Row3(sb, "Floor acquired", FormatFloor(agg.FloorAcquired ?? floorAcquiredFallback), "");
        Row3(
            sb,
            "Floor activated",
            agg.FloorActivated.HasValue ? FormatFloor(agg.FloorActivated) : "none yet",
            "");
        Row3(sb, "HP healed", FormatDecimal(agg.TotalHealingRestored), "");
        return sb.ToString();
    }

    private static string BuildPantographBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        AppendHealingStats(sb, agg, lostLabel: "healing wasted", reasonPrefix: "wasted to");
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
        AppendMaxHpChangeRows(sb, agg, "Max HP gained", MaxHpGained(agg));
        Row3(sb, "HP gained", FormatDecimal(agg.TotalHealingRestored), "");
        return sb.ToString();
    }

    private static string BuildStrawberryBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        AppendMaxHpChangeRows(sb, agg, "Max HP gained", agg.MaxHpGained);
        return sb.ToString();
    }

    private static string BuildPearBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        AppendMaxHpChangeRows(sb, agg, "Max HP gained", agg.MaxHpGained);
        return sb.ToString();
    }

    private static string BuildNutritiousOysterBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        AppendMaxHpChangeRows(sb, agg, "Max HP gained", agg.MaxHpGained);
        return sb.ToString();
    }

    private static string BuildMangoBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        AppendMaxHpChangeRows(sb, agg, "Max HP gained", agg.MaxHpGained);
        return sb.ToString();
    }

    private static string BuildStoneHumidifierBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var activations = agg.MaxHpActivations
            ?? new List<RelicMaxHpActivationAggregate>();

        Row3(sb, "Times triggered", agg.Activations.ToString(), "");
        Row3(sb, "Max HP gained", FormatDecimal(Math.Max(0m, agg.MaxHpGained)), "");

        for (var index = 0; index < activations.Count; index++)
        {
            var activation = activations[index];
            if (activation == null) continue;

            var activationNumber = index + 1;
            Row3(
                sb,
                $"Activation {activationNumber} starting HP",
                FormatDecimal(Math.Max(0m, activation.StartingHp)),
                "");
            Row3(
                sb,
                $"Activation {activationNumber} resulting HP",
                FormatDecimal(Math.Max(0m, activation.ResultingHp)),
                "");
        }

        return sb.ToString();
    }

    private static string BuildChosenCheeseBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Starting max HP", FormatDecimal(OriginalMaxHp(agg)), "");
        Row3(sb, "Max HP gained", FormatDecimal(Math.Max(0m, agg.MaxHpGained)), "");
        return sb.ToString();
    }

    private static string BuildDarkstonePeriaptBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Curses acquired", agg.CursesAcquired.ToString(), "");
        AppendMaxHpChangeRows(sb, agg, "Max HP gained", agg.TotalMaxHpGained);
        return sb.ToString();
    }

    private static string BuildLuckyFyshBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Gold gained", agg.GoldGained.ToString(), "");
        Row3(sb, "Cards added to deck", agg.CardsAddedToDeck.ToString(), "");
        return sb.ToString();
    }

    private static string BuildBookOfFiveRingsBodyBBCode(
        RelicAggregate agg,
        int? currentFloor,
        int? floorAcquiredFallback = null)
    {
        var sb = new StringBuilder();
        var floorAcquired = agg.FloorAcquired ?? floorAcquiredFallback;
        var floorsHeld = currentFloor.HasValue && floorAcquired.HasValue
            ? Math.Max(1, currentFloor.Value - floorAcquired.Value + 1)
            : Math.Max(1, currentFloor ?? 1);
        var cardsPerFloor = (decimal)agg.CardsAddedToDeck / floorsHeld;

        Row3(sb, "Total cards added to deck", agg.CardsAddedToDeck.ToString(), "");
        Row3(sb, "Avg cards added per floor", FormatDecimal(cardsPerFloor), "");
        Row3(sb, "Total times triggered", agg.Activations.ToString(), "");
        Row3(sb, "Total HP healed", FormatDecimal(agg.TotalHealingRestored), "");
        Row3(sb, "Total HP healing blocked", FormatDecimal(agg.TotalHealingLost), "");
        Row3(sb, "Card rewards skipped", agg.CardRewardsSkipped.ToString(), "");
        return sb.ToString();
    }

    private static string BuildSignetRingBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(
            sb,
            "Floors traveled until next shop reached",
            (agg.FloorsTraveledUntilNextShop ?? 0).ToString(),
            "");
        return sb.ToString();
    }

    private static string BuildLeafyPoulticeBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        AppendMaxHpChangeRows(sb, agg, "Max HP lost", MaxHpLost(agg));
        AppendCardTransformationRows(sb, agg, expectedCount: 2);
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

        AppendMaxHpChangeRows(sb, agg, "Max HP lost", MaxHpLost(agg));
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

    private static string BuildHeftyTabletBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var cardsGranted = agg.CardsGranted.Values.Sum(card => Math.Max(0, card.Count));
        Row3(sb, "Cards granted", cardsGranted.ToString(), "");
        Row3(sb, "Skipped", agg.CardChoicesSkipped.ToString(), "");

        foreach (var card in agg.CardsGranted.Values
                     .Where(card => card.Count > 0)
                     .OrderByDescending(card => card.Count)
                     .ThenBy(card => card.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var displayName = StatsTooltip.EscapeBbcode(string.IsNullOrWhiteSpace(card.DisplayName)
                ? RunTracker.FormatCardIdForDisplay(card.CardId)
                : card.DisplayName);
            var value = card.Count == 1 ? displayName : $"{displayName} x{card.Count}";
            Row3(sb, "Granted", value, "");
        }

        return sb.ToString();
    }

    private static string BuildArcaneScrollBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var cards = agg.CardsGranted.Values
            .Where(card => card.Count > 0)
            .OrderByDescending(card => card.Count)
            .ThenBy(card => card.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cards.Count == 0)
        {
            Row3(sb, "Rare received", "0", "");
            return sb.ToString();
        }

        foreach (var card in cards)
        {
            var displayName = StatsTooltip.EscapeBbcode(string.IsNullOrWhiteSpace(card.DisplayName)
                ? RunTracker.FormatCardIdForDisplay(card.CardId)
                : card.DisplayName);
            var value = card.Count == 1 ? displayName : $"{displayName} x{card.Count}";
            Row3(sb, "Rare received", value, "");
        }

        return sb.ToString();
    }

    private static string BuildLargeCapsuleBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var relics = agg.RelicsGranted.Values
            .Where(relic => relic.Count > 0)
            .OrderByDescending(relic => relic.Count)
            .ThenBy(relic => relic.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var total = relics.Sum(relic => Math.Max(0, relic.Count));

        Row3(sb, "Relics obtained", total.ToString(), "");

        foreach (var relic in relics)
        {
            var displayName = StatsTooltip.EscapeBbcode(string.IsNullOrWhiteSpace(relic.DisplayName)
                ? RunTracker.FormatRelicIdForDisplay(relic.RelicId)
                : relic.DisplayName);
            var value = relic.Count == 1 ? displayName : $"{displayName} x{relic.Count}";
            Row3(sb, "Obtained", value, "");
        }

        return sb.ToString();
    }

    private static string BuildPaelsWingBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "common cards consumed", agg.CommonCardsConsumed.ToString(), "");
        Row3(sb, "uncommon cards consumed", agg.UncommonCardsConsumed.ToString(), "");
        Row3(sb, "rare cards consumed", agg.RareCardsConsumed.ToString(), "");
        var artifacts = agg.RelicsGranted.Values
            .Where(artifact => artifact.Count > 0)
            .OrderByDescending(artifact => artifact.Count)
            .ThenBy(artifact => artifact.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var artifactsGained = artifacts.Sum(artifact => Math.Max(0, artifact.Count));
        Row3(sb, "Artifacts gained", artifactsGained.ToString(), "");

        foreach (var artifact in artifacts)
        {
            var displayName = StatsTooltip.EscapeBbcode(string.IsNullOrWhiteSpace(artifact.DisplayName)
                ? RunTracker.FormatRelicIdForDisplay(artifact.RelicId)
                : artifact.DisplayName);
            var value = artifact.Count == 1 ? displayName : $"{displayName} x{artifact.Count}";
            Row3(sb, "Artifact gained", value, "");
        }

        Row3(sb, "Sacrifices made", agg.SacrificesMade.ToString(), "");
        Row3(sb, "Sacrifices skipped", agg.SacrificesSkipped.ToString(), "");
        var sacrificesMade = Math.Max(0, agg.SacrificesMade);
        var sacrificeOpportunities = sacrificesMade + Math.Max(0, agg.SacrificesSkipped);
        if (sacrificeOpportunities > 0)
        {
            var ratePercent = 100m * sacrificesMade / sacrificeOpportunities;
            Row3(
                sb,
                "Sacrifice rate",
                $"{sacrificesMade}/{sacrificeOpportunities}",
                $"{FormatDecimal(ratePercent)}%");
        }

        return sb.ToString();
    }

    private static string BuildPaelsToothBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var cards = (agg.CardsReturned ?? new List<RelicCardReturnAggregate>())
            .Where(card => card != null)
            .ToList();
        Row3(sb, "Cards returned", cards.Count.ToString(), "");

        foreach (var card in cards)
        {
            var displayName = card.DisplayName;
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = string.IsNullOrWhiteSpace(card.CardId)
                    ? "Unknown card"
                    : RunTracker.FormatCardIdForDisplay(card.CardId);
                if (card.UpgradeLevel > 0)
                    displayName += new string('+', card.UpgradeLevel);
            }

            Row3(sb, "Returned card", StatsTooltip.EscapeBbcode(displayName), "");
        }

        return sb.ToString();
    }

    private static string BuildPaelsClawBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var goopyCardsPlayedPerTurn = agg.PaelsClawTurns <= 0
            ? 0m
            : (decimal)agg.PaelsClawGoopyCardsPlayed / agg.PaelsClawTurns;
        var goopyCardsPlayedPerCombat = agg.PaelsClawCombats <= 0
            ? 0m
            : (decimal)agg.PaelsClawGoopyCardsPlayed / agg.PaelsClawCombats;
        var enhancementsPerGoopyCard = agg.PaelsClawGoopyCards <= 0
            ? 0m
            : (decimal)agg.PaelsClawGoopyEnhancements / agg.PaelsClawGoopyCards;

        Row3(sb, "Goopy cards played", agg.PaelsClawGoopyCardsPlayed.ToString(), "");
        Row3(sb, "Avg Goopy cards played per turn", FormatDecimal(goopyCardsPlayedPerTurn), "");
        Row3(sb, "Avg Goopy cards played per combat", FormatDecimal(goopyCardsPlayedPerCombat), "");
        Row3(
            sb,
            "Avg number of Goopy enhancements per card with Goopy",
            FormatDecimal(enhancementsPerGoopyCard),
            "");
        return sb.ToString();
    }

    private static string BuildPaelsEyeBodyBBCode(RelicAggregate agg, bool activatedThisCombat = false)
    {
        var sb = new StringBuilder();
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        Row3(sb, "Activated this combat", activatedThisCombat ? "true" : "false", "");
        Row3(sb, "Combats without activation", agg.CombatsWithoutActivation.ToString(), "");
        Row3(sb, "Statuses exhausted", agg.StatusCardsExhausted.ToString(), "");
        Row3(sb, "Curses exhausted", agg.CurseCardsExhausted.ToString(), "");
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

    private static string BuildNutritiousSoupBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var averagePlays = agg.Activations <= 0
            ? 0m
            : (decimal)agg.NutritiousSoupEnchantedStrikesPlayed / agg.Activations;

        Row3(sb, "Combats held", agg.Activations.ToString(), "");
        Row3(sb, "Enchanted Strikes played", agg.NutritiousSoupEnchantedStrikesPlayed.ToString(), "");
        Row3(sb, "Avg Enchanted Strikes/combat", FormatDecimal(averagePlays), "");
        return sb.ToString();
    }

    private static string BuildMiniatureCannonBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var averagePlays = agg.Activations <= 0
            ? 0m
            : (decimal)agg.MiniatureCannonUpgradedAttackPlays / agg.Activations;
        var averageHits = agg.Activations <= 0
            ? 0m
            : (decimal)agg.MiniatureCannonUpgradedAttackHits / agg.Activations;

        Row3(sb, "Combats held", agg.Activations.ToString(), "");
        Row3(sb, "Upgraded attacks in deck", agg.MiniatureCannonUpgradedAttacksInDeck.ToString(), "");
        Row3(sb, "Upgraded attack plays", agg.MiniatureCannonUpgradedAttackPlays.ToString(), "");
        Row3(sb, "Upgraded attack hits", agg.MiniatureCannonUpgradedAttackHits.ToString(), "");
        Row3(sb, "Avg plays per combat", FormatDecimal(averagePlays), "");
        Row3(sb, "Avg hits per combat", FormatDecimal(averageHits), "");
        return sb.ToString();
    }

    private static string BuildVajraBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Attacks played", agg.VajraAttacksPlayed.ToString(), "");
        Row3(sb, "Attack hits", agg.VajraAttackHits.ToString(), "");
        return sb.ToString();
    }

    private static string BuildKunaiBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var averageEndCharge = agg.KunaiTurnEndChargeCount <= 0
            ? 0m
            : (decimal)agg.KunaiTurnEndChargeTotal / agg.KunaiTurnEndChargeCount;

        Row3(sb, "Attacks played", agg.KunaiAttacksPlayed.ToString(), "");
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        Row3(sb, "Dexterity gained", agg.KunaiDexterityGained.ToString(), "");
        Row3(sb, "Turns ended at 1 charge", agg.KunaiTurnsEndedAt1Charge.ToString(), "");
        Row3(sb, "Turns ended at 2 charges", agg.KunaiTurnsEndedAt2Charges.ToString(), "");
        Row3(sb, "Avg charge at turn end", FormatDecimal(averageEndCharge), "");
        return sb.ToString();
    }

    private static string BuildKusarigamaBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Attacks played", agg.KusarigamaAttacksPlayed.ToString(), "");
        AppendRelicDamageStats(
            sb,
            agg,
            triggerLabel: "Activations",
            averageLabel: "Damage per activation",
            averageDenominator: agg.Activations);
        AppendTurnResetChargeRows(
            sb,
            agg.KusarigamaTurnsEndedAt1Charge,
            agg.KusarigamaTurnsEndedAt2Charges,
            agg.KusarigamaTurnEndChargeTotal,
            agg.KusarigamaTurnEndChargeCount);
        return sb.ToString();
    }

    private static string BuildOrnamentalFanBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var blockPerActivation = agg.Activations <= 0
            ? 0m
            : (decimal)agg.AdditionalBlockGained / agg.Activations;

        Row3(sb, "Attacks played", agg.OrnamentalFanAttacksPlayed.ToString(), "");
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        Row3(sb, BlockLabel("block gained"), agg.AdditionalBlockGained.ToString(), "");
        Row3(sb, BlockLabel("block gained per activation"), FormatDecimal(blockPerActivation), "");
        Row3(sb, "Turns ended at 0 charges", agg.OrnamentalFanTurnsEndedAt0Charges.ToString(), "");
        AppendTurnResetChargeRows(
            sb,
            agg.OrnamentalFanTurnsEndedAt1Charge,
            agg.OrnamentalFanTurnsEndedAt2Charges,
            agg.OrnamentalFanTurnEndChargeTotal,
            agg.OrnamentalFanTurnEndChargeCount);
        return sb.ToString();
    }

    private static string BuildShurikenBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var strengthPerActivation = agg.Activations <= 0
            ? 0m
            : agg.StrengthAdded / agg.Activations;

        Row3(sb, "Attacks played", agg.ShurikenAttacksPlayed.ToString(), "");
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        Row3(sb, "Strength gained", FormatDecimal(agg.StrengthAdded), "");
        Row3(sb, "Strength gained per activation", FormatDecimal(strengthPerActivation), "");
        AppendTurnResetChargeRows(
            sb,
            agg.ShurikenTurnsEndedAt1Charge,
            agg.ShurikenTurnsEndedAt2Charges,
            agg.ShurikenTurnEndChargeTotal,
            agg.ShurikenTurnEndChargeCount);
        return sb.ToString();
    }

    private static string BuildRuinedHelmetBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var strengthPerActivation = agg.Activations <= 0
            ? 0m
            : agg.StrengthAdded / agg.Activations;
        var strengthPerCombat = agg.RuinedHelmetCombats <= 0
            ? 0m
            : agg.StrengthAdded / agg.RuinedHelmetCombats;

        Row3(sb, "Times activated", agg.Activations.ToString(), "");
        Row3(sb, "Total strength gained", FormatDecimal(agg.StrengthAdded), "");
        Row3(
            sb,
            "Strength gained this combat",
            FormatDecimal(agg.RuinedHelmetStrengthAddedThisCombat),
            "");
        Row3(
            sb,
            "Avg strength gained per activation",
            FormatDecimal(strengthPerActivation),
            "");
        Row3(sb, "Avg strength gained per combat", FormatDecimal(strengthPerCombat), "");
        return sb.ToString();
    }

    private static void AppendTurnResetChargeRows(
        StringBuilder sb,
        int turnsEndedAt1Charge,
        int turnsEndedAt2Charges,
        int turnEndChargeTotal,
        int turnEndChargeCount)
    {
        var averageEndCharge = turnEndChargeCount <= 0
            ? 0m
            : (decimal)turnEndChargeTotal / turnEndChargeCount;

        Row3(sb, "Turns ended at 1 charge", turnsEndedAt1Charge.ToString(), "");
        Row3(sb, "Turns ended at 2 charges", turnsEndedAt2Charges.ToString(), "");
        Row3(sb, "Avg charge at turn end", FormatDecimal(averageEndCharge), "");
    }

    private static string BuildPaperPhrogBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var averageDamagePerCombat = agg.PaperPhrogCombats <= 0
            ? 0m
            : agg.PaperPhrogDamageAdded / agg.PaperPhrogCombats;
        var averageDamagePerTurn = agg.PaperPhrogTurns <= 0
            ? 0m
            : agg.PaperPhrogDamageAdded / agg.PaperPhrogTurns;
        var averageAttacksPerCombat = agg.PaperPhrogCombats <= 0
            ? 0m
            : (decimal)agg.PaperPhrogEnhancedAttacks / agg.PaperPhrogCombats;
        var averageAttacksPerTurn = agg.PaperPhrogTurns <= 0
            ? 0m
            : (decimal)agg.PaperPhrogEnhancedAttacks / agg.PaperPhrogTurns;

        Row3(sb, "Damage added", FormatDecimal(agg.PaperPhrogDamageAdded), "");
        Row3(sb, "Avg damage added per combat", FormatDecimal(averageDamagePerCombat), "");
        Row3(sb, "Avg damage added per turn", FormatDecimal(averageDamagePerTurn), "");
        Row3(sb, "Vulnerable-enhanced attacks", agg.PaperPhrogEnhancedAttacks.ToString(), "");
        Row3(sb, "Avg enhanced attacks per combat", FormatDecimal(averageAttacksPerCombat), "");
        Row3(sb, "Avg enhanced attacks per turn", FormatDecimal(averageAttacksPerTurn), "");
        return sb.ToString();
    }

    private static string BuildBookmarkBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var averageActivations = agg.BookmarkCombats <= 0
            ? 0m
            : (decimal)agg.Activations / agg.BookmarkCombats;

        Row3(sb, "Activations", agg.Activations.ToString(), "");
        Row3(sb, "common activations", agg.BookmarkCommonActivations.ToString(), "");
        Row3(sb, "uncommon activations", agg.BookmarkUncommonActivations.ToString(), "");
        Row3(sb, "rare activations", agg.BookmarkRareActivations.ToString(), "");
        Row3(sb, "Combats held", agg.BookmarkCombats.ToString(), "");
        Row3(sb, "Avg activations per combat", FormatDecimal(averageActivations), "");
        return sb.ToString();
    }

    private static string BuildBrilliantScarfBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var energySavedPerCombat = agg.DiscountCombats <= 0
            ? 0m
            : (decimal)agg.EnergySavedByDiscount / agg.DiscountCombats;
        var energySavedPerUse = agg.DiscountsTaken <= 0
            ? 0m
            : (decimal)agg.EnergySavedByDiscount / agg.DiscountsTaken;
        Row3(sb, "Combats held", agg.DiscountCombats.ToString(), "");
        Row3(sb, "Discounts offered", agg.DiscountsOffered.ToString(), "");
        Row3(sb, "Discounts taken", agg.DiscountsTaken.ToString(), "");
        Row3(sb, EnergyLabel("Energy saved"), agg.EnergySavedByDiscount.ToString(), "");
        Row3(sb, EnergyLabel("saved / combat"), FormatDecimal(energySavedPerCombat), "");
        Row3(sb, EnergyLabel("saved / use"), FormatDecimal(energySavedPerUse), "");

        for (int energyCost = 0; energyCost <= 3; energyCost++)
        {
            Row3(
                sb,
                BrilliantScarfCostLabel(energyCost, starCost: 0),
                BrilliantScarfCostCount(agg, energyCost, starCost: 0).ToString(),
                "");
        }

        foreach (var bucket in DynamicBrilliantScarfCostBuckets(agg))
        {
            Row3(
                sb,
                BrilliantScarfCostLabel(bucket.EnergyCost, bucket.StarCost),
                bucket.Count.ToString(),
                "");
        }

        return sb.ToString();
    }

    private static string BuildMummifiedHandBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var averageTriggeringPowerCost = agg.Activations <= 0
            ? 0m
            : agg.MummifiedHandTriggeringPowerCostTotal / agg.Activations;
        var averageDiscountGiven = agg.Activations <= 0
            ? 0m
            : agg.MummifiedHandDiscountGivenTotal / agg.Activations;
        var averageEnergySpentToDiscountedCostRatio =
            agg.MummifiedHandEnergySpentToDiscountedCostRatioCount <= 0
                ? 0m
                : agg.MummifiedHandEnergySpentToDiscountedCostRatioTotal
                  / agg.MummifiedHandEnergySpentToDiscountedCostRatioCount;
        var averageActivationsPerCombat = agg.MummifiedHandCombats <= 0
            ? 0m
            : (decimal)agg.Activations / agg.MummifiedHandCombats;
        var averageActivationsPerTurn = agg.MummifiedHandTurns <= 0
            ? 0m
            : (decimal)agg.Activations / agg.MummifiedHandTurns;

        Row3(sb, "Times triggered", agg.Activations.ToString(), "");
        Row3(sb, EnergyLabel("Avg cost of triggering Power"), FormatDecimal(averageTriggeringPowerCost), "");
        Row3(sb, EnergyLabel("Avg discount given"), FormatDecimal(averageDiscountGiven), "");
        Row3(
            sb,
            "Avg ratio: Power energy spent / discounted card cost",
            FormatDecimal(averageEnergySpentToDiscountedCostRatio),
            "");
        Row3(sb, "Avg activations per combat", FormatDecimal(averageActivationsPerCombat), "");
        Row3(sb, "Avg activations per turn", FormatDecimal(averageActivationsPerTurn), "");
        Row3(sb, "Discounted Powers", agg.MummifiedHandDiscountedPowers.ToString(), "");
        Row3(sb, "Discounted Attacks", agg.MummifiedHandDiscountedAttacks.ToString(), "");
        Row3(sb, "Discounted Skills", agg.MummifiedHandDiscountedSkills.ToString(), "");
        Row3(sb, "Discounted Commons", agg.MummifiedHandDiscountedCommons.ToString(), "");
        Row3(sb, "Discounted Uncommons", agg.MummifiedHandDiscountedUncommons.ToString(), "");
        Row3(sb, "Discounted Rares", agg.MummifiedHandDiscountedRares.ToString(), "");
        return sb.ToString();
    }

    private static string BuildJuzuBraceletBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "? sites entered", agg.QuestionMarkSitesEntered.ToString(), "");
        return sb.ToString();
    }

    private static string BuildDowsingRodBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var roomsRemaining = Math.Clamp(
            agg.DowsingQuestionRoomsRemaining ?? Dowsing.maxRooms,
            0,
            Dowsing.maxRooms);
        Row3(sb, "? rooms remaining", roomsRemaining.ToString(), "");
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

    private static string BuildCentennialPuzzleBodyBBCode(
        RelicAggregate agg,
        bool triggeredThisCombat = false)
    {
        var sb = new StringBuilder();
        var averageDrawn = agg.Activations <= 0
            ? 0m
            : (decimal)agg.AdditionalCardsDrawn / agg.Activations;
        Row3(sb, "Activations", agg.Activations.ToString(), "");
        Row3(sb, "Triggered this combat", triggeredThisCombat ? "true" : "false", "");
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

    private static string BuildShovelBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "Relics acquired", agg.RelicsAcquired.ToString(), "");
        Row3(sb, "common relics", agg.CommonRelicsAcquired.ToString(), "");
        Row3(sb, "uncommon relics", agg.UncommonRelicsAcquired.ToString(), "");
        Row3(sb, "rare relics", agg.RareRelicsAcquired.ToString(), "");
        Row3(sb, "Campfires not dug", agg.CampfiresNotDug.ToString(), "");
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

    private static IEnumerable<AppliedEffectAggregate> OtherUnsettlingLampDebuffs(RelicAggregate agg)
    {
        if (agg.AppliedEffects == null) return Enumerable.Empty<AppliedEffectAggregate>();

        return agg.AppliedEffects.Values
            .Where(effect => effect != null
                && effect.TotalAmountApplied > 0m
                && !RunTracker.IsVulnerableEffect(effect.EffectId, effect.DisplayName)
                && !RunTracker.IsWeakEffect(effect.EffectId, effect.DisplayName))
            .OrderBy(effect => string.IsNullOrWhiteSpace(effect.DisplayName) ? effect.EffectId : effect.DisplayName);
    }

    private static string RelicEffectLabel(AppliedEffectAggregate effect, string suffix)
    {
        var displayName = string.IsNullOrWhiteSpace(effect.DisplayName)
            ? effect.EffectId
            : effect.DisplayName;
        var label = $"{displayName} {suffix}";
        return string.IsNullOrWhiteSpace(effect.IconPath)
            ? label
            : $"{InlineIcon(effect.IconPath)} {label}";
    }

    private static string BlockLabel(string suffix)
    {
        var path = NormalizeResourcePath(BlockIconPath);
        return $"[img={InlineIconSize}x{InlineIconSize}]{path}[/img] {suffix}";
    }

    private static void AppendHealingStats(
        StringBuilder sb,
        RelicAggregate agg,
        string lostLabel = "healing lost",
        string reasonPrefix = "lost to")
    {
        Row3(sb, "HP healed", FormatDecimal(agg.TotalHealingRestored), "");
        Row3(sb, lostLabel, FormatDecimal(agg.TotalHealingLost), "");

        if (agg.TotalHealingLost <= 0m) return;

        foreach (var reason in agg.HealingLostReasons.Values
                     .OrderByDescending(r => r.Amount)
                     .ThenBy(r => r.DisplayName))
        {
            if (reason.Amount <= 0m) continue;
            var label = string.IsNullOrWhiteSpace(reason.DisplayName)
                ? $"{reasonPrefix} other/prevented"
                : $"{reasonPrefix} {StatsTooltip.EscapeBbcode(reason.DisplayName)}";
            Row3(sb, label, FormatDecimal(reason.Amount), "");
        }
    }

    private static void AppendEnergyGeneratedStats(
        StringBuilder sb,
        RelicAggregate agg,
        string totalLabel = "Energy generated",
        bool includeAveragePerCombat = false,
        string averageLabel = "Avg energy gained per combat",
        int? combatCount = null,
        bool includeCombatsHeld = false)
    {
        Row3(sb, EnergyLabel(totalLabel), agg.EnergyGenerated.ToString(), "");
        var combats = combatCount ?? agg.Activations;
        if (includeCombatsHeld)
            Row3(sb, "Combats held", combats.ToString(), "");

        if (!includeAveragePerCombat) return;

        var average = combats <= 0
            ? 0m
            : (decimal)agg.EnergyGenerated / combats;
        Row3(sb, EnergyLabel(averageLabel), FormatDecimal(average), "");
    }

    private static string FormatDecimal(decimal value)
    {
        return decimal.Truncate(value) == value
            ? value.ToString("0")
            : value.ToString("0.##");
    }

    private static string FormatFloor(int? floor)
    {
        return floor.HasValue && floor.Value > 0
            ? floor.Value.ToString()
            : "0";
    }

    private static void AppendMaxHpChangeRows(
        StringBuilder sb,
        RelicAggregate agg,
        string deltaLabel,
        decimal delta)
    {
        Row3(sb, "Original max HP", FormatDecimal(OriginalMaxHp(agg)), "");
        Row3(sb, "New max HP", FormatDecimal(NewMaxHp(agg)), "");
        Row3(sb, deltaLabel, FormatDecimal(Math.Max(0m, delta)), "");
    }

    private static void AppendCardTransformationRows(StringBuilder sb, RelicAggregate agg, int expectedCount)
    {
        var transformations = agg.CardTransformations ?? new List<RelicCardTransformationAggregate>();
        for (var i = 0; i < expectedCount; i++)
        {
            var transformation = i < transformations.Count ? transformations[i] : null;
            Row3(sb, $"Transform {i + 1} source", CardTransformationDisplay(
                transformation?.SourceDisplayName,
                transformation?.SourceCardId), "");
            Row3(sb, $"Transform {i + 1} result", CardTransformationDisplay(
                transformation?.ResultDisplayName,
                transformation?.ResultCardId), "");
        }
    }

    private static string CardTransformationDisplay(string? displayName, string? cardId)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
            return StatsTooltip.EscapeBbcode(displayName);

        if (!string.IsNullOrWhiteSpace(cardId))
            return StatsTooltip.EscapeBbcode(RunTracker.FormatCardIdForDisplay(cardId));

        return "0";
    }

    private static decimal OriginalMaxHp(RelicAggregate agg)
        => agg.OriginalMaxHp ?? agg.StartingMaxHp ?? 0m;

    private static decimal NewMaxHp(RelicAggregate agg)
        => agg.NewMaxHp ?? agg.ResultingMaxHp ?? 0m;

    private static decimal MaxHpGained(RelicAggregate agg)
        => Math.Max(0m, NewMaxHp(agg) - OriginalMaxHp(agg));

    private static decimal MaxHpLost(RelicAggregate agg)
        => Math.Max(0m, OriginalMaxHp(agg) - NewMaxHp(agg));

    private static string EnergyLabel(string suffix)
    {
        var path = NormalizeResourcePath(EnergyIconPath);
        return $"[img={InlineIconSize}x{InlineIconSize}]{path}[/img] {suffix}";
    }

    private static string DrawLabel(string suffix)
    {
        var path = NormalizeResourcePath(DrawIconPath);
        return $"[img={InlineIconSize}x{InlineIconSize}]{path}[/img] {suffix}";
    }

    private static string BrilliantScarfCostLabel(int energyCost, int starCost)
    {
        var energyIcon = InlineIcon(EnergyIconPath);
        if (starCost > 0)
        {
            var starIcon = InlineIcon(StarIconPath);
            return $"{Math.Max(0, energyCost)} {energyIcon} {Math.Max(0, starCost)} {starIcon} cost reduced";
        }

        return $"{Math.Max(0, energyCost)} {energyIcon} cost reduced";
    }

    private static int BrilliantScarfCostCount(RelicAggregate agg, int energyCost, int starCost)
    {
        if (agg.DiscountedCardCosts == null) return 0;
        return agg.DiscountedCardCosts.Values
            .Where(b => b.EnergyCost == energyCost && b.StarCost == starCost)
            .Sum(b => Math.Max(0, b.Count));
    }

    private static IEnumerable<DiscountedCardCostAggregate> DynamicBrilliantScarfCostBuckets(RelicAggregate agg)
    {
        if (agg.DiscountedCardCosts == null) return Enumerable.Empty<DiscountedCardCostAggregate>();

        return agg.DiscountedCardCosts.Values
            .Where(b => b.Count > 0)
            .Select(b => new DiscountedCardCostAggregate
            {
                EnergyCost = Math.Max(0, b.EnergyCost),
                StarCost = Math.Max(0, b.StarCost),
                Count = Math.Max(0, b.Count),
            })
            .Where(b => b.StarCost > 0 || b.EnergyCost > 3)
            .GroupBy(b => new { b.EnergyCost, b.StarCost })
            .Select(g => new DiscountedCardCostAggregate
            {
                EnergyCost = g.Key.EnergyCost,
                StarCost = g.Key.StarCost,
                Count = g.Sum(b => b.Count),
            })
            .OrderBy(b => b.EnergyCost)
            .ThenBy(b => b.StarCost);
    }

    private static string InlineIcon(string path)
    {
        var normalized = NormalizeResourcePath(path);
        return $"[img={InlineIconSize}x{InlineIconSize}]{normalized}[/img]";
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

    private static bool IsVambraceUsedThisCombat(Vambrace relic)
    {
        try
        {
            return VambraceBlockGainedThisCombatField?.GetValue(relic) is bool used && used;
        }
        catch
        {
            return false;
        }
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

    private static bool IsMiniatureCannonStatsRelicModel(object model)
    {
        return IsRelicModel(model, "MegaCrit.Sts2.Core.Models.Relics.MiniatureCannon");
    }

    private static bool IsMrStrugglesStatsRelicModel(object model)
    {
        return IsRelicModel(model, "MegaCrit.Sts2.Core.Models.Relics.MrStruggles");
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
        if (VisibleTextLength(label) > MaxTableLabelVisibleChars)
        {
            RowFlow(sb, label, value, pct);
            return;
        }

        sb.Append("[table=3]");
        sb.Append($"[cell expand=4 padding=0,0,12,0][color=#e0e0e0]{label}[/color][/cell]");
        sb.Append($"[cell expand=1 padding=0,0,12,0][right][b]{value}[/b][/right][/cell]");
        sb.Append($"[cell expand=1 padding=0,0,4,0][right][color=#b5b5b5]{pct}[/color][/right][/cell]");
        sb.Append("[/table]\n");
    }

    private static void RowFlow(StringBuilder sb, string label, string value, string pct)
    {
        sb.Append($"[color=#e0e0e0]{label}[/color]");
        if (!string.IsNullOrEmpty(value))
        {
            sb.Append($"  [b]{value}[/b]");
        }
        if (!string.IsNullOrEmpty(pct))
        {
            sb.Append($"  [color=#b5b5b5]{pct}[/color]");
        }
        sb.Append('\n');
    }

    private static int VisibleTextLength(string bbcode)
    {
        var count = 0;
        for (var i = 0; i < bbcode.Length;)
        {
            if (bbcode[i] == '[')
            {
                var close = bbcode.IndexOf(']', i);
                if (close < 0)
                {
                    count += 1;
                    i += 1;
                    continue;
                }

                var tag = bbcode.Substring(i + 1, close - i - 1).Trim();
                if (tag.StartsWith("img", StringComparison.OrdinalIgnoreCase))
                {
                    var imageClose = bbcode.IndexOf("[/img]", close + 1, StringComparison.OrdinalIgnoreCase);
                    i = imageClose >= 0
                        ? imageClose + "[/img]".Length
                        : close + 1;
                    continue;
                }

                i = close + 1;
                continue;
            }

            count += 1;
            i += 1;
        }

        return count;
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
