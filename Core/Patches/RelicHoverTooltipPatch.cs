using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;


namespace SpireLens.Core.Patches;

internal readonly record struct LetterOpenerRates(
    decimal DamagePerCombat,
    decimal DamagePerTurn,
    decimal TargetsPerActivation,
    decimal DamagePerSkillPlayed);

/// <summary>
/// Builds the native SpireLens hover-tip entry for an owned relic.
/// NHoverTipSet owns the rendered node and its complete lifecycle.
/// </summary>
public static class RelicHoverShowPatch
{
    private const string ScalarStatsTableOpen = "[table=4]";
    private const string StatsTableClose = "[/table]\n";
    private const string EnthralledDefinitionId = "CARD.ENTHRALLED";
    private const string CursedPearlCurseDefinitionId = "CARD.GREED";
    private const string BrightestFlameDefinitionId = "CARD.BRIGHTEST_FLAME";
    private const string VulnerableIconPath = "res://images/atlases/power_atlas.sprites/vulnerable_power.tres";
    private const string WeakIconPath = "res://images/atlases/power_atlas.sprites/weak_power.tres";
    private const string BlockIconPath = "res://images/ui/combat/block.png";
    private const string DrawIconPath = "res://images/atlases/power_atlas.sprites/draw_cards_next_turn_power.tres";
    private const string PaelsWingIconPath = "res://images/atlases/relic_atlas.sprites/paels_wing.tres";
    private const string GenericRelicIconPath = "res://images/ui/reward_screen/reward_icon_shared_relic.png";
    private const string StarIconPath = "res://images/packed/sprite_fonts/star_icon.png";
    private const string VigorIconPath = "res://images/atlases/power_atlas.sprites/vigor_power.tres";
    private const int SealOfGoldLossPerTrigger = 5;
    private const float SturdyClampTooltipWidth = 420f;
    private const float EmberTeaTooltipWidth = 500f;
    private const float PaelsWingTooltipWidth = 500f;
    private const float FresnelLensTooltipWidth = 500f;
    private static readonly System.Reflection.FieldInfo? VambraceBlockGainedThisCombatField =
        AccessTools.Field(typeof(Vambrace), "_blockGainedThisCombat");
    private static readonly System.Reflection.FieldInfo? PermafrostActivatedThisCombatField =
        AccessTools.Field(typeof(Permafrost), "_activatedThisCombat");
    private static readonly System.Reflection.FieldInfo? RainbowRingAttacksPlayedThisTurnField =
        AccessTools.Field(typeof(RainbowRing), "_attacksPlayedThisTurn");
    private static readonly System.Reflection.FieldInfo? RainbowRingPowersPlayedThisTurnField =
        AccessTools.Field(typeof(RainbowRing), "_powersPlayedThisTurn");
    private static readonly System.Reflection.FieldInfo? RainbowRingSkillsPlayedThisTurnField =
        AccessTools.Field(typeof(RainbowRing), "_skillsPlayedThisTurn");

    internal static bool TryBuildNativeHoverTip(
        NRelicInventoryHolder holder,
        out HoverTip statsTip)
    {
        statsTip = default;
        if (!ViewStatsInjectorPatch.StatsVisibilityEnabled) return false;

        var relicModel = holder?.Relic?.Model;
        if (relicModel == null) return false;
        if (!TryBuildInventoryBodyBBCode(holder, relicModel, out var title, out var body))
            return false;

        statsTip = StatsTooltip.CreateNativeTip(
            title,
            body,
            stretchHorizontally: ShouldStretchStatsTooltip(relicModel, body));
        return true;
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
            if (current is NGameOverScreen)
                return true;
        }

        // The in-run relic inventory is persistent global UI. On the game-over
        // screen its holders remain under the global top bar rather than being
        // reparented beneath NGameOverScreen, so ancestor-only detection can
        // never identify the death-screen context. Resolve the actual active
        // screen in the shared scene tree instead.
        var tree = node?.GetTree() ?? Engine.GetMainLoop() as SceneTree;
        return tree?.Root != null && ContainsVisibleGameOverScreen(tree.Root);
    }

    private static bool ContainsVisibleGameOverScreen(Node node)
    {
        if (node is NGameOverScreen screen && screen.IsVisibleInTree())
            return true;

        for (var i = 0; i < node.GetChildCount(); i++)
        {
            if (ContainsVisibleGameOverScreen(node.GetChild(i)))
                return true;
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
        => relicModel switch
        {
            SturdyClamp => SturdyClampTooltipWidth,
            EmberTea => EmberTeaTooltipWidth,
            RedSkull => EmberTeaTooltipWidth,
            WhisperingEarring => EmberTeaTooltipWidth,
            TungstenRod => EmberTeaTooltipWidth,
            PaelsWing => PaelsWingTooltipWidth,
            FresnelLens => FresnelLensTooltipWidth,
            _ => null,
        };

    internal static bool ShouldStretchStatsTooltip(
        RelicModel? relicModel,
        string? bodyBBCode)
    {
        if (relicModel == null) return false;

        // A shared scalar table deliberately sizes its label column from the
        // longest row. The game's default 360px wrapped hover tip does not
        // shrink table columns, so allow the native control to grow to that
        // calculated width instead of clipping the aligned value columns.
        return GetPreferredStatsTooltipWidth(relicModel).HasValue
               || (!string.IsNullOrEmpty(bodyBBCode)
                   && bodyBBCode.Contains(
                       ScalarStatsTableOpen,
                       StringComparison.Ordinal));
    }

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

        if (relicModel is BagOfPreparation)
        {
            title = "Bag of Preparation";
            body = BuildBagOfPreparationBodyBBCode(agg);
            return true;
        }

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

        if (relicModel is PollinousCore)
        {
            title = "Pollinous Core";
            body = BuildPollinousCoreBodyBBCode(agg);
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
            title = IsFakeAnchorRelicModel(relicModel) ? "Anchor???" : "Anchor";
            body = BuildAnchorBodyBBCode(agg);
            return true;
        }

        if (relicModel is FakeVenerableTeaSet)
        {
            title = "Venerable Tea Set???";
            body = BuildVenerableTeaSetBodyBBCode(agg);
            return true;
        }

        if (relicModel is VenerableTeaSet)
        {
            title = "Venerable Tea Set";
            body = BuildVenerableTeaSetBodyBBCode(agg);
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

        if (relicModel is SymbioticVirus)
        {
            title = "Symbiotic Virus";
            body = BuildSymbioticVirusBodyBBCode(agg);
            return true;
        }

        if (relicModel is BingBong)
        {
            title = "Bing Bong";
            body = BuildBingBongBodyBBCode(agg);
            return true;
        }

        if (relicModel is GoldPlatedCables)
        {
            title = "Gold-Plated Cables";
            body = BuildGoldPlatedCablesBodyBBCode(agg);
            return true;
        }

        if (relicModel is HappyFlower or FakeHappyFlower)
        {
            title = relicModel is FakeHappyFlower
                ? "Happy Flower???"
                : "Happy Flower";
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

        if (relicModel is EmberTea)
        {
            title = "Ember Tea";
            body = BuildEmberTeaBodyBBCode(agg);
            return true;
        }

        if (relicModel is RedSkull)
        {
            title = "Red Skull";
            body = BuildRedSkullBodyBBCode(agg);
            return true;
        }

        if (relicModel is ToastyMittens)
        {
            title = "Toasty Mittens";
            body = BuildToastyMittensBodyBBCode(agg);
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

        if (relicModel is LostWisp)
        {
            title = "Lost Wisp";
            body = BuildLostWispBodyBBCode(agg);
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

        if (relicModel is CaptainsWheel)
        {
            title = "Captain's Wheel";
            body = BuildCaptainsWheelBodyBBCode(agg);
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
            body = BuildEggBodyBBCode(agg, "attack");
            return true;
        }

        if (relicModel is ToxicEgg)
        {
            title = "Toxic Egg";
            body = BuildEggBodyBBCode(agg, "skill");
            return true;
        }

        if (relicModel is FrozenEgg)
        {
            title = "Frozen Egg";
            body = BuildEggBodyBBCode(agg, "power");
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

        if (relicModel is RainbowRing rainbowRing)
        {
            title = "Rainbow Ring";
            var turnState = GetRainbowRingTurnState(rainbowRing);
            body = BuildRainbowRingBodyBBCode(
                agg,
                turnState.AttackPlayed,
                turnState.PowerPlayed,
                turnState.SkillPlayed);
            return true;
        }

        if (relicModel is SparklingRouge)
        {
            title = "Sparkling Rouge";
            body = BuildSparklingRougeBodyBBCode(agg);
            return true;
        }

        if (relicModel is BeatingRemnant)
        {
            title = "Beating Remnant";
            body = BuildBeatingRemnantBodyBBCode(agg);
            return true;
        }

        if (relicModel is WhisperingEarring)
        {
            title = "Whispering Earring";
            body = BuildWhisperingEarringBodyBBCode(agg);
            return true;
        }

        if (relicModel is TungstenRod)
        {
            title = "Tungsten Rod";
            body = BuildTungstenRodBodyBBCode(agg);
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

        if (relicModel is TriBoomerang)
        {
            title = "Tri-Boomerang";
            body = BuildTriBoomerangBodyBBCode(agg);
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

        if (relicModel is MeatOnTheBone)
        {
            title = "Meat on the Bone";
            body = BuildMeatOnTheBoneBodyBBCode(agg);
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

        if (relicModel is FakeLeesWaffle)
        {
            title = "Lee's Waffle???";
            body = BuildFakeLeesWaffleBodyBBCode(agg);
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

        if (relicModel is FakeMango)
        {
            title = "Mango???";
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

        if (relicModel is AmethystAubergine)
        {
            title = "Amethyst Aubergine";
            body = BuildAmethystAubergineBodyBBCode(agg);
            return true;
        }

        if (relicModel is WongosMysteryTicket)
        {
            title = "Wongo's Mystery Ticket";
            body = BuildWongosMysteryTicketBodyBBCode(agg);
            return true;
        }

        if (relicModel is MawBank)
        {
            title = "Maw Bank";
            body = BuildMawBankBodyBBCode(agg);
            return true;
        }

        if (relicModel is OldCoin)
        {
            title = "Old Coin";
            body = BuildOldCoinBodyBBCode(agg);
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

        if (relicModel is WingedBoots wingedBoots)
        {
            title = "Winged Boots";
            body = BuildWingedBootsBodyBBCode(agg, wingedBoots.TimesUsed);
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

        if (relicModel is FakeBloodVial)
        {
            title = "Blood Vial???";
            body = BuildBloodVialBodyBBCode(agg);
            return true;
        }

        if (IsRelicModel(relicModel, "MegaCrit.Sts2.Core.Models.Relics.Toolbox"))
        {
            title = "Toolbox";
            body = BuildToolboxBodyBBCode(agg);
            return true;
        }

        if (relicModel is WhiteStar)
        {
            title = "White Star";
            body = BuildWhiteStarBodyBBCode(agg);
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

        if (relicModel is BurningSticks)
        {
            title = "Burning Sticks";
            body = BuildBurningSticksBodyBBCode(agg);
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

    private static string BuildBagOfPreparationBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        RelicActivationRow(
            sb,
            agg.Activations.ToString(),
            "Activations — first-turn hand draws whose requested card count Bag of Preparation increased.");
        Row3(
            sb,
            "Cards drawn",
            agg.AdditionalCardsDrawn.ToString(),
            "",
            "Cards drawn — first-turn cards that were actually drawn because of Bag of Preparation's added hand-draw count.");
        return sb.ToString();
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
        var averageCountAtTurnEnd = agg.PocketwatchTurns <= 0
            ? 0m
            : (decimal)agg.PocketwatchTurnEndCountTotal / agg.PocketwatchTurns;
        var averageValueWhenActivated = agg.PocketwatchActivationValueSamples <= 0
            ? 0m
            : (decimal)agg.PocketwatchActivatedTurnEndCountTotal
                / agg.PocketwatchActivationValueSamples;
        var averageValueWhenMissed = agg.PocketwatchTurnsActivationMissed <= 0
            ? 0m
            : (decimal)agg.PocketwatchMissedTurnEndCountTotal
                / agg.PocketwatchTurnsActivationMissed;
        var averageActivationsPerTurn = agg.PocketwatchTurns <= 0
            ? 0m
            : (decimal)agg.Activations / agg.PocketwatchTurns;
        var averageActivationsPerCombat = agg.PocketwatchCombats <= 0
            ? 0m
            : (decimal)agg.Activations / agg.PocketwatchCombats;

        Row3(
            sb,
            "Additional cards drawn",
            agg.AdditionalCardsDrawn.ToString(),
            "",
            "Additional cards drawn — cards actually added to hand draws by Pocketwatch.");
        Row3(
            sb,
            "Avg count at turn end",
            FormatDecimal(averageCountAtTurnEnd),
            "",
            "Average count at turn end — cards played by the relic's owner at the end of each turn while Pocketwatch was held.");
        Row3(
            sb,
            "Turns activation missed",
            agg.PocketwatchTurnsActivationMissed.ToString(),
            "",
            "Turns activation missed — turns that ended above Pocketwatch's card threshold and therefore could not activate it.");
        Row3(
            sb,
            "Avg value when activated",
            FormatDecimal(averageValueWhenActivated),
            "",
            "Average value when activated — the prior turn's card count when Pocketwatch actually added cards to the next hand draw.");
        Row3(
            sb,
            "Avg value when missed",
            FormatDecimal(averageValueWhenMissed),
            "",
            "Average value when missed — the card count on turns that ended above Pocketwatch's activation threshold.");
        Row3(
            sb,
            "Avg activations per turn",
            FormatDecimal(averageActivationsPerTurn),
            "",
            "Average activations per turn — actual Pocketwatch activations divided by turns completed while it was held.");
        Row3(
            sb,
            "Avg activations per combat",
            FormatDecimal(averageActivationsPerCombat),
            "",
            "Average activations per combat — actual Pocketwatch activations divided by combats in which it was held.");
        return sb.ToString();
    }

    private static string BuildPollinousCoreBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var averageActivationsPerCombat = agg.PollinousCoreCombats <= 0
            ? 0m
            : (decimal)agg.Activations / agg.PollinousCoreCombats;
        var averageTurnsPerCombat = agg.PollinousCoreCombats <= 0
            ? 0m
            : (decimal)agg.PollinousCoreTurns / agg.PollinousCoreCombats;
        var averageCardsDrawnPerCombat = agg.PollinousCoreCombats <= 0
            ? 0m
            : (decimal)agg.AdditionalCardsDrawn / agg.PollinousCoreCombats;

        Row3(
            sb,
            "Activations",
            agg.Activations.ToString(),
            "",
            "Times Pollinous Core reached four counters and added cards to the upcoming hand draw.");
        Row3(
            sb,
            "Turns ended on 0 counters",
            agg.PollinousCoreTurnsEndedOn0Counters.ToString(),
            "",
            "Player turns that ended after Pollinous Core activated and reset its counter to zero.");
        Row3(
            sb,
            "Turns ended on 1 counter",
            agg.PollinousCoreTurnsEndedOn1Counter.ToString(),
            "",
            "Player turns that ended with Pollinous Core showing one counter.");
        Row3(
            sb,
            "Turns ended on 2 counters",
            agg.PollinousCoreTurnsEndedOn2Counters.ToString(),
            "",
            "Player turns that ended with Pollinous Core showing two counters.");
        Row3(
            sb,
            "Turns ended on 3 counters",
            agg.PollinousCoreTurnsEndedOn3Counters.ToString(),
            "",
            "Player turns that ended with Pollinous Core showing three counters.");
        Row3(
            sb,
            "Avg activations/combat",
            FormatDecimal(averageActivationsPerCombat),
            "",
            "Average activations per combat — activations divided by combats where Pollinous Core was held.");
        Row3(
            sb,
            "Avg turns/combat",
            FormatDecimal(averageTurnsPerCombat),
            "",
            "Average turns per combat — completed player turns divided by combats where Pollinous Core was held.");
        Row3(
            sb,
            "Cards drawn",
            agg.AdditionalCardsDrawn.ToString(),
            "",
            "Cards drawn — Pollinous Core cards that actually reached the hand.");
        Row3(
            sb,
            "Card draws blocked",
            agg.AdditionalCardDrawsBlocked.ToString(),
            "",
            "Card draws blocked — Pollinous Core cards requested but prevented by draw limits or draw-prevention effects.");
        Row3(
            sb,
            "Avg cards drawn/combat",
            FormatDecimal(averageCardsDrawnPerCombat),
            "",
            "Average cards drawn per combat — observed Pollinous Core cards drawn divided by combats where it was held.");
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
        RelicActivationRow(sb, agg.Activations.ToString());
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
        RelicActivationRow(sb, agg.Activations.ToString());
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

        RelicActivationRow(sb, agg.Activations.ToString());
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
        RelicActivationRow(sb, agg.Activations.ToString());
        Row3(sb, BlockLabel("block gained"), agg.AdditionalBlockGained.ToString(), "");
        return sb.ToString();
    }

    private static string BuildVenerableTeaSetBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        RelicActivationRow(sb, agg.Activations.ToString());
        Row3(sb, EnergyLabel("Energy gained"), agg.EnergyGenerated.ToString(), "");
        return sb.ToString();
    }

    private static string BuildLetterOpenerBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var rates = CalculateLetterOpenerRates(agg);

        RelicActivationRow(sb, agg.Activations.ToString());
        Row3(sb, "Damage attempted", agg.TotalDamageAttempted.ToString(), "");
        Row3(sb, "Targets hit", agg.TotalTargets.ToString(), "");
        Row3(sb, "Targets hit per activation", FormatDecimal(rates.TargetsPerActivation), "");
        Row3(sb, "Avg damage per combat", FormatDecimal(rates.DamagePerCombat), "");
        Row3(sb, "Avg damage per turn", FormatDecimal(rates.DamagePerTurn), "");
        Row3(sb, "Turns ended at 1 charge", agg.LetterOpenerTurnsEndedAt1Charge.ToString(), "");
        Row3(sb, "Turns ended at 2 charges", agg.LetterOpenerTurnsEndedAt2Charges.ToString(), "");
        Row3(sb, "Avg damage per skill played", FormatDecimal(rates.DamagePerSkillPlayed), "");
        return sb.ToString();
    }

    internal static LetterOpenerRates CalculateLetterOpenerRates(RelicAggregate agg)
    {
        agg ??= new RelicAggregate();
        return new LetterOpenerRates(
            DamagePerCombat: agg.LetterOpenerCombats <= 0
                ? 0m
                : (decimal)agg.TotalDamageAttempted / agg.LetterOpenerCombats,
            DamagePerTurn: agg.LetterOpenerTurns <= 0
                ? 0m
                : (decimal)agg.TotalDamageAttempted / agg.LetterOpenerTurns,
            TargetsPerActivation: agg.Activations <= 0
                ? 0m
                : (decimal)agg.TotalTargets / agg.Activations,
            DamagePerSkillPlayed: agg.LetterOpenerSkillsPlayed <= 0
                ? 0m
                : (decimal)agg.TotalDamageAttempted / agg.LetterOpenerSkillsPlayed);
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
            triggerDescription: "Times triggered — the number of times this relic has activated.",
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
        RelicActivationRow(sb, agg.Activations.ToString());
        AppendHealingStats(sb, agg);
        return sb.ToString();
    }

    private static string BuildBoneFluteBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        RelicActivationRow(
            sb,
            agg.BoneFluteTriggers.ToString(),
            "Times triggered — the number of times this relic has activated.");
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

    private static string BuildSymbioticVirusBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(
            sb,
            "Times orb was evoked",
            agg.SymbioticVirusOrbEvokes.ToString(),
            "");
        Row3(
            sb,
            "Times orb passive triggered",
            agg.SymbioticVirusOrbPassiveTriggers.ToString(),
            "");
        Row3(
            sb,
            "Times orb fizzled",
            agg.SymbioticVirusOrbFizzles.ToString(),
            "");
        return sb.ToString();
    }

    private static string BuildGoldPlatedCablesBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(
            sb,
            "Activations with orb",
            agg.Activations.ToString(),
            "",
            "Activations with orb — times Gold-Plated Cables increased the passive trigger count of the owner's first orb.");

        var activationsByOrb = agg.GoldPlatedCablesActivationsByOrbType
            ?? new Dictionary<string, RelicOrbActivationAggregate>();
        var standardOrbs = new[]
        {
            ("ORB.LIGHTNING", "Lightning"),
            ("ORB.FROST", "Frost"),
            ("ORB.DARK", "Dark"),
            ("ORB.PLASMA", "Plasma"),
            ("ORB.GLASS", "Glass"),
        };

        foreach (var (orbId, fallbackName) in standardOrbs)
        {
            activationsByOrb.TryGetValue(orbId, out var bucket);
            var displayName = string.IsNullOrWhiteSpace(bucket?.DisplayName)
                ? fallbackName
                : bucket.DisplayName;
            Row3(
                sb,
                $"{displayName} activations",
                Math.Max(0, bucket?.Activations ?? 0).ToString(),
                "",
                $"{displayName} activations — confirmed Gold-Plated Cables activations that targeted a {displayName} orb.");
        }

        foreach (var bucket in activationsByOrb
                     .Where(kvp => standardOrbs.All(standard =>
                         !string.Equals(
                             standard.Item1,
                             kvp.Key,
                             StringComparison.Ordinal)))
                     .Select(kvp => kvp.Value)
                     .Where(bucket => bucket != null && bucket.Activations > 0)
                     .OrderBy(bucket => bucket.DisplayName, StringComparer.Ordinal))
        {
            var displayName = string.IsNullOrWhiteSpace(bucket.DisplayName)
                ? RunTracker.FormatOrbIdForDisplay(bucket.OrbId)
                : bucket.DisplayName;
            Row3(
                sb,
                $"{displayName} activations",
                bucket.Activations.ToString(),
                "",
                $"{displayName} activations — confirmed Gold-Plated Cables activations that targeted a {displayName} orb.");
        }

        Row3(
            sb,
            "Turns with no orb to target",
            agg.GoldPlatedCablesNoOrbTargets.ToString(),
            "",
            "Turns with no orb to target — player turns that ended while Gold-Plated Cables had no first orb available.");
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
        RelicActivationRow(sb, agg.Activations.ToString());
        AppendEnergyGeneratedStats(sb, agg);
        Row3(sb, excessEnergyLabel, turnsEndedWithExcessEnergy.ToString(), "");
        if (includeCombatsWithEnergyNotGained)
            Row3(sb, "Combats without energy", agg.CombatsWithoutActivation.ToString(), "");
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
        RelicActivationRow(sb, agg.Activations.ToString());
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
        var chargeSamples = agg.PendulumCombatEndChargeCount;
        var chargeTotal = agg.PendulumCombatEndChargeTotal;
        if (chargeSamples <= 0)
        {
            chargeSamples =
                agg.PendulumCombatsEndedOn0Charges
                + agg.PendulumCombatsEndedOn1Charge
                + agg.PendulumCombatsEndedOn2Charges;
            chargeTotal =
                agg.PendulumCombatsEndedOn1Charge
                + (agg.PendulumCombatsEndedOn2Charges * 2);
        }
        var averageEndCharge = chargeSamples <= 0
            ? 0m
            : (decimal)chargeTotal / chargeSamples;

        RelicActivationRow(sb, agg.Activations.ToString());
        Row3(sb, "Cards drawn", agg.AdditionalCardsDrawn.ToString(), "");
        Row3(sb, "Avg cards drawn per combat", FormatDecimal(cardsDrawnPerCombat), "");
        Row3(sb, "Combats ended on 0 charges", agg.PendulumCombatsEndedOn0Charges.ToString(), "");
        Row3(sb, "Combats ended on 1 charge", agg.PendulumCombatsEndedOn1Charge.ToString(), "");
        Row3(sb, "Combats ended on 2 charges", agg.PendulumCombatsEndedOn2Charges.ToString(), "");
        Row3(sb, "Avg charge at combat end", FormatDecimal(averageEndCharge), "");
        return sb.ToString();
    }

    private static string BuildMercuryHourglassBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        AppendRelicDamageStats(
            sb,
            agg,
            triggerDescription: "Combats triggered — the number of combats in which this relic activated.",
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
            triggerDescription: "Activations — the number of times this relic has activated.",
            averageLabel: "Damage per activation",
            averageDenominator: agg.Activations);
        return sb.ToString();
    }

    private static string BuildLostWispBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var damagePerPower = agg.Activations <= 0
            ? 0m
            : (decimal)agg.TotalDamageDealt / agg.Activations;

        Row3(
            sb,
            "Powers played",
            agg.Activations.ToString(),
            "",
            "Power cards played by this relic's owner while Lost Wisp could activate.");
        Row3(sb, "Damage attempted", agg.TotalDamageAttempted.ToString(), "");
        Row3(sb, "Damage dealt", agg.TotalDamageDealt.ToString(), "");
        Row3(sb, "Damage blocked", agg.TotalDamageBlocked.ToString(), "");
        Row3(sb, "Overkill", agg.TotalDamageOverkill.ToString(), "");
        Row3(sb, "Kills", agg.Kills.ToString(), "");
        Row3(sb, "Targets hit", agg.TotalTargets.ToString(), "");
        Row3(sb, "Avg damage per Power", FormatDecimal(damagePerPower), "");
        return sb.ToString();
    }

    private static string BuildParryingShieldBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        AppendRelicDamageStats(
            sb,
            agg,
            triggerDescription: "Activations — the number of times this relic has activated.",
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
            triggerDescription: "Combats triggered — the number of combats in which this relic activated.",
            averageLabel: "Damage per combat",
            averageDenominator: agg.Activations);
        return sb.ToString();
    }

    private static void AppendRelicDamageStats(
        StringBuilder sb,
        RelicAggregate agg,
        string triggerDescription,
        string? averageLabel = null,
        int averageDenominator = 0)
    {
        RelicActivationRow(sb, agg.Activations.ToString(), triggerDescription);
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
        RelicActivationRow(sb, agg.Activations.ToString());
        Row3(sb, BlockLabel("block gained"), agg.AdditionalBlockGained.ToString(), "");
        return sb.ToString();
    }

    private static string BuildCaptainsWheelBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        RelicActivationRow(sb, agg.Activations.ToString());
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
        RelicActivationRow(
            sb,
            agg.Activations.ToString(),
            "Times triggered — the number of times this relic has activated.");
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
        NimbleRow(
            sb,
            "cards taken",
            agg.NimbleCardsTaken,
            "Nimble cards taken from rewards affected by Fresnel Lens.");
        NimbleRow(
            sb,
            "reward screens",
            agg.RewardScreensWithNimbleCards,
            "Reward screens that offered at least one Nimble card.");
        NimbleRow(
            sb,
            "reward screens with 2",
            agg.RewardScreensWithTwoNimbleCards,
            "Reward screens that offered exactly two Nimble cards.");
        NimbleRow(
            sb,
            "reward screens with 3+",
            agg.RewardScreensWithThreeOrMoreNimbleCards,
            "Reward screens that offered three or more Nimble cards.");
        NimbleRow(
            sb,
            "reward screens with none",
            agg.RewardScreensWithoutNimbleCards,
            "Reward screens that offered no Nimble cards.");
        NimbleRow(
            sb,
            "offered, none taken",
            agg.RewardScreensWithNimbleCardsButNoneTaken,
            "Reward screens that offered Nimble cards but from which none were taken.");
        return sb.ToString();
    }

    private static void NimbleRow(
        StringBuilder sb,
        string label,
        int value,
        string fullDescription)
    {
        DescribedIconRow(
            sb,
            ["nimble"],
            [],
            label,
            value.ToString(),
            fullDescription);
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
        AppendEggCardRow(
            sb,
            cardType,
            rarity: null,
            count: agg.UpgradedCardsOffered,
            action: "offered");
        AppendEggCardRow(
            sb,
            cardType,
            "common",
            agg.UpgradedCommonCardsOffered,
            "offered");
        AppendEggCardRow(
            sb,
            cardType,
            "uncommon",
            agg.UpgradedUncommonCardsOffered,
            "offered");
        AppendEggCardRow(
            sb,
            cardType,
            "rare",
            agg.UpgradedRareCardsOffered,
            "offered");
        AppendEggCardRow(
            sb,
            cardType,
            rarity: null,
            count: agg.UpgradedCardsTaken,
            action: "taken");
        AppendEggCardRow(
            sb,
            cardType,
            "common",
            agg.UpgradedCommonCardsTaken,
            "taken");
        AppendEggCardRow(
            sb,
            cardType,
            "uncommon",
            agg.UpgradedUncommonCardsTaken,
            "taken");
        AppendEggCardRow(
            sb,
            cardType,
            "rare",
            agg.UpgradedRareCardsTaken,
            "taken");
        return sb.ToString();
    }

    private static void AppendEggCardRow(
        StringBuilder sb,
        string cardType,
        string? rarity,
        int count,
        string action)
    {
        var pluralCardType = $"{cardType}s";
        var rarityText = string.IsNullOrEmpty(rarity)
            ? string.Empty
            : $"{rarity} ";
        var typeConceptId = string.IsNullOrEmpty(rarity)
            ? cardType
            : $"{cardType}_{rarity}";
        DescribedIconRow(
            sb,
            ["upgraded", typeConceptId],
            [],
            action,
            count.ToString(),
            $"Upgraded {rarityText}{pluralCardType} {action}.");
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
                TextValueRow(sb, $"Card reward {screenNumber}", "not seen yet", "");
                continue;
            }

            var cards = screen.Cards ?? new List<RelicCardRewardOptionAggregate>();
            if (cards.Count == 0)
            {
                TextValueRow(sb, $"Card reward {screenNumber}", "no cards offered", "");
                continue;
            }

            Row3(
                sb,
                $"Card reward {screenNumber}",
                string.Empty,
                "",
                $"The cards offered in Silver Crucible reward {screenNumber}.");
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
        DescribedIconFlowRow(
            sb,
            ["card"],
            [],
            $"[b]{displayName}[/b]",
            outcome,
            $"This card was offered by Silver Crucible and was {outcome}.");
    }

    private static string BuildOrreryBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var rewards = agg.OrreryRewards ?? new List<OrreryRewardAggregate>();

        for (var rewardNumber = 1; rewardNumber <= 5; rewardNumber++)
        {
            var reward = rewards.LastOrDefault(candidate =>
                candidate != null && candidate.RewardNumber == rewardNumber);
            TextValueRow(
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
            {
                return StatConceptGlossary.RenderInlineImage(
                    PaelsWingIconPath);
            }

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
        Row3(
            sb,
            EnergyLabel("Energy gained by Flame"),
            brightestFlameAgg.TotalEnergyGenerated.ToString(),
            "");
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

        DescribedIconRow(
            sb,
            ["average", "block", "turn"],
            ["turn"],
            "retained",
            FormatDecimal(blockRetainedPerTurn),
            "Average block retained by Sturdy Clamp per turn.");
        DescribedIconRow(
            sb,
            ["average", "block", "combat"],
            ["combat"],
            "retained",
            FormatDecimal(blockRetainedPerCombat),
            "Average block retained by Sturdy Clamp per combat.");
        DescribedIconRow(
            sb,
            ["average", "block_wasted", "turn"],
            ["turn"],
            "excess over 10",
            FormatDecimal(excessBlockPerTurn),
            "Average block discarded above Sturdy Clamp's 10-block retention cap per turn.");
        DescribedIconRow(
            sb,
            ["average", "block_wasted", "combat"],
            ["combat"],
            "excess over 10",
            FormatDecimal(excessBlockPerCombat),
            "Average block discarded above Sturdy Clamp's 10-block retention cap per combat.");
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
            TextValueRow(sb, "Neow relic", value, "");
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
            TextValueRow(sb, "Curse added", value, "");

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
        var blockPerTurn = agg.CloakClaspTurns <= 0
            ? 0m
            : (decimal)agg.AdditionalBlockGained / agg.CloakClaspTurns;
        var blockPerCombat = agg.CloakClaspCombats <= 0
            ? 0m
            : (decimal)agg.AdditionalBlockGained / agg.CloakClaspCombats;

        Row3(sb, BlockLabel("Block gained"), agg.AdditionalBlockGained.ToString(), "");
        Row3(sb, BlockLabel("avg block gained per turn"), FormatDecimal(blockPerTurn), "");
        Row3(sb, BlockLabel("avg block gained per combat"), FormatDecimal(blockPerCombat), "");
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

        RelicActivationRow(sb, agg.Activations.ToString());
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

    private static string BuildRainbowRingBodyBBCode(
        RelicAggregate agg,
        bool attackPlayedThisTurn,
        bool powerPlayedThisTurn,
        bool skillPlayedThisTurn)
    {
        var sb = new StringBuilder();
        var activationsPerTurn = agg.RainbowRingTurns <= 0
            ? 0m
            : (decimal)agg.Activations / agg.RainbowRingTurns;
        var activationsPerCombat = agg.RainbowRingCombats <= 0
            ? 0m
            : (decimal)agg.Activations / agg.RainbowRingCombats;

        RelicActivationRow(sb, agg.Activations.ToString());
        Row3(sb, "Avg activations per turn", FormatDecimal(activationsPerTurn), "");
        Row3(sb, "Avg activations per combat", FormatDecimal(activationsPerCombat), "");
        Row3(sb, "Attack played this turn", FormatBoolean(attackPlayedThisTurn), "");
        Row3(sb, "Power played this turn", FormatBoolean(powerPlayedThisTurn), "");
        Row3(sb, "Skill played this turn", FormatBoolean(skillPlayedThisTurn), "");
        return sb.ToString();
    }

    private static string BuildSparklingRougeBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(
            sb,
            "Combats ended on turn 1",
            agg.SparklingRougeCombatsEndedOnTurn1.ToString(),
            "");
        Row3(
            sb,
            "Combats ended on turn 2",
            agg.SparklingRougeCombatsEndedOnTurn2.ToString(),
            "");
        Row3(
            sb,
            "Combats ended on turn 3+",
            agg.SparklingRougeCombatsEndedOnTurn3Plus.ToString(),
            "");
        return sb.ToString();
    }

    private static (
        bool AttackPlayed,
        bool PowerPlayed,
        bool SkillPlayed) GetRainbowRingTurnState(RainbowRing relic)
    {
        try
        {
            return (
                ReadPositiveInt(RainbowRingAttacksPlayedThisTurnField, relic),
                ReadPositiveInt(RainbowRingPowersPlayedThisTurnField, relic),
                ReadPositiveInt(RainbowRingSkillsPlayedThisTurnField, relic));
        }
        catch
        {
            return (false, false, false);
        }
    }

    private static bool ReadPositiveInt(
        System.Reflection.FieldInfo? field,
        object instance)
        => field?.GetValue(instance) is int value && value > 0;

    private static string FormatBoolean(bool value)
        => value ? "true" : "false";

    private static string BuildGorgetBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        RelicActivationRow(sb, agg.Activations.ToString());
        Row3(sb, "Plating added", FormatDecimal(agg.PlatingAdded), "");
        return sb.ToString();
    }

    private static string BuildBeatingRemnantBodyBBCode(RelicAggregate agg)
    {
        var preventedPerTurn = agg.BeatingRemnantTurns <= 0
            ? 0m
            : agg.BeatingRemnantHpLossPrevented / agg.BeatingRemnantTurns;
        var preventedPerCombat = agg.BeatingRemnantCombats <= 0
            ? 0m
            : agg.BeatingRemnantHpLossPrevented / agg.BeatingRemnantCombats;

        var sb = new StringBuilder();
        Row3(
            sb,
            "HP loss prevented",
            FormatDecimal(agg.BeatingRemnantHpLossPrevented),
            "");
        Row3(
            sb,
            "Avg HP loss prevented per turn",
            FormatDecimal(preventedPerTurn),
            "");
        Row3(
            sb,
            "Avg HP loss prevented per combat",
            FormatDecimal(preventedPerCombat),
            "");
        return sb.ToString();
    }

    private static string BuildWhisperingEarringBodyBBCode(RelicAggregate agg)
    {
        var lifeLostPerCombat = agg.WhisperingEarringCombats <= 0
            ? 0m
            : agg.WhisperingEarringFirstRoundHpLost / agg.WhisperingEarringCombats;

        var sb = new StringBuilder();
        Row3(
            sb,
            "Total life lost, player's first turn through opponent's first turn",
            FormatDecimal(agg.WhisperingEarringFirstRoundHpLost),
            "");
        Row3(
            sb,
            "Avg life lost, player's first turn through opponent's first turn per combat",
            FormatDecimal(lifeLostPerCombat),
            "");
        return sb.ToString();
    }

    private static string BuildTungstenRodBodyBBCode(RelicAggregate agg)
    {
        var turns = agg.TungstenRodTurns;
        var combats = agg.TungstenRodCombats;
        var sb = new StringBuilder();

        AddPreventionRows(
            "Damage prevented",
            "Lost life prevented",
            agg.TungstenRodDamagePrevented,
            turns,
            combats,
            "HP loss prevented by Tungsten Rod.",
            "Average HP loss prevented by Tungsten Rod per player turn while it was held.",
            "Average HP loss prevented by Tungsten Rod per combat while it was held.");
        AddPreventionRows(
            "Self-inflicted lost life prevented",
            "Self-inflicted lost life prevented",
            agg.TungstenRodSelfDamagePrevented,
            turns,
            combats,
            "HP loss prevented from the player's own non-Curse, non-Status cards and Buff powers.",
            "Average self-inflicted HP loss prevented per player turn while Tungsten Rod was held.",
            "Average self-inflicted HP loss prevented per combat while Tungsten Rod was held.");
        AddPreventionRows(
            "Curse-inflicted lost life prevented",
            "Curse-inflicted lost life prevented",
            agg.TungstenRodCurseDamagePrevented,
            turns,
            combats,
            "HP loss prevented from direct Curse-card damage.",
            "Average Curse-inflicted HP loss prevented per player turn while Tungsten Rod was held.",
            "Average Curse-inflicted HP loss prevented per combat while Tungsten Rod was held.");
        AddPreventionRows(
            "Status-inflicted lost life prevented",
            "Status-inflicted lost life prevented",
            agg.TungstenRodStatusDamagePrevented,
            turns,
            combats,
            "HP loss prevented from direct Status-card damage.",
            "Average Status-inflicted HP loss prevented per player turn while Tungsten Rod was held.",
            "Average Status-inflicted HP loss prevented per combat while Tungsten Rod was held.");
        AddPreventionRows(
            "Enemy-source lost life prevented",
            "Enemy-source lost life prevented",
            agg.TungstenRodEnemyDamagePrevented,
            turns,
            combats,
            "HP loss prevented from enemy creatures and Debuff powers.",
            "Average enemy-source HP loss prevented per player turn while Tungsten Rod was held.",
            "Average enemy-source HP loss prevented per combat while Tungsten Rod was held.");

        return sb.ToString();

        void AddPreventionRows(
            string totalLabel,
            string averageLabel,
            decimal total,
            int turnCount,
            int combatCount,
            string totalDescription,
            string turnDescription,
            string combatDescription)
        {
            var perTurn = turnCount <= 0 ? 0m : total / turnCount;
            var perCombat = combatCount <= 0 ? 0m : total / combatCount;

            Row3(sb, totalLabel, FormatDecimal(total), "", totalDescription);
            Row3(
                sb,
                $"Avg {LowercaseFirst(averageLabel)} per turn",
                FormatDecimal(perTurn),
                "",
                turnDescription);
            Row3(
                sb,
                $"Avg {LowercaseFirst(averageLabel)} per combat",
                FormatDecimal(perCombat),
                "",
                combatDescription);
        }
    }

    private static string LowercaseFirst(string value)
        => string.IsNullOrEmpty(value)
            ? value
            : char.ToLowerInvariant(value[0]) + value[1..];

    private static string BuildStoneCrackerBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var upgradedPlaysPerTurn = agg.StoneCrackerTurns <= 0
            ? 0m
            : (decimal)agg.StoneCrackerUpgradedCardPlays / agg.StoneCrackerTurns;
        var upgradedPlaysPerCombat = agg.StoneCrackerCombats <= 0
            ? 0m
            : (decimal)agg.StoneCrackerUpgradedCardPlays / agg.StoneCrackerCombats;

        RelicActivationRow(sb, agg.Activations.ToString());
        Row3(sb, "Cards upgraded", agg.CardsUpgraded.ToString(), "");
        Row3(sb, "Upgraded commons", agg.StoneCrackerUpgradedCommons.ToString(), "");
        Row3(sb, "Upgraded uncommons", agg.StoneCrackerUpgradedUncommons.ToString(), "");
        Row3(sb, "Upgraded rares", agg.StoneCrackerUpgradedRares.ToString(), "");
        Row3(
            sb,
            "Cards played upgraded",
            agg.StoneCrackerUpgradedCardPlays.ToString(),
            "",
            "Cards upgraded by Stone Cracker that were played.");
        Row3(
            sb,
            "Avg cards played upgraded per turn",
            FormatDecimal(upgradedPlaysPerTurn),
            "",
            "Average number of cards upgraded by Stone Cracker that were played per turn.");
        Row3(
            sb,
            "Avg cards played upgraded per combat",
            FormatDecimal(upgradedPlaysPerCombat),
            "",
            "Average number of cards upgraded by Stone Cracker that were played per combat.");
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

        RelicActivationRow(sb, agg.Activations.ToString());
        Row3(sb, "Cards upgraded", agg.CardsUpgraded.ToString(), "");
        Row3(sb, "Avg cards upgraded/activation", FormatDecimal(cardsPerActivation), "");
        foreach (var card in (agg.UpgradedCards ?? new List<string>())
                     .Where(card => !string.IsNullOrWhiteSpace(card)))
        {
            TextValueRow(sb, "Upgraded card", StatsTooltip.EscapeBbcode(card), "");
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
        AppendUpgradedCardStats(
            sb,
            agg,
            totalLabel: "Attacks upgraded",
            itemLabel: "Upgraded attack");
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
            TextValueRow(sb, "Sharp-enchanted card", StatsTooltip.EscapeBbcode(card), "");
        return sb.ToString();
    }

    private static string BuildTriBoomerangBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var cards = (agg.TriBoomerangInstinctCards
                ?? new List<RelicEnchantedCardAggregate>())
            .Where(card =>
                card != null
                && !string.IsNullOrWhiteSpace(card.CardInstanceId))
            .ToList();
        decimal playsPerCombat = agg.TriBoomerangCombats > 0
            ? (decimal)agg.TriBoomerangInstinctCardPlays
                / agg.TriBoomerangCombats
            : 0m;

        Row3(sb, "Cards enchanted with Instinct", cards.Count.ToString(), "");
        foreach (var card in cards)
        {
            var displayName = string.IsNullOrWhiteSpace(card.DisplayName)
                ? card.CardInstanceId
                : card.DisplayName;
            TextValueRow(
                sb,
                "Instinct-enchanted card",
                StatsTooltip.EscapeBbcode(displayName),
                "");
        }
        Row3(
            sb,
            "Times Instinct cards were played",
            agg.TriBoomerangInstinctCardPlays.ToString(),
            "");
        Row3(
            sb,
            "Avg Instinct-card plays per combat",
            FormatDecimal(playsPerCombat),
            "");
        return sb.ToString();
    }

    private static string BuildWarPaintBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        AppendUpgradedCardStats(
            sb,
            agg,
            totalLabel: "Skills upgraded",
            itemLabel: "Upgraded skill");
        return sb.ToString();
    }

    private static string BuildFragrantMushroomBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        AppendUpgradedCardStats(sb, agg);
        return sb.ToString();
    }

    private static void AppendUpgradedCardStats(
        StringBuilder sb,
        RelicAggregate agg,
        string totalLabel = "Cards upgraded",
        string itemLabel = "Upgraded card")
    {
        var upgradedCards = (agg.UpgradedCards ?? new System.Collections.Generic.List<string>())
            .Where(card => !string.IsNullOrWhiteSpace(card))
            .ToList();

        Row3(sb, totalLabel, agg.CardsUpgraded.ToString(), "");
        foreach (var card in upgradedCards)
            TextValueRow(sb, itemLabel, StatsTooltip.EscapeBbcode(card), "");
    }

    private static string BuildMealTicketBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        RelicActivationRow(sb, agg.Activations.ToString());
        AppendHealingStats(sb, agg);
        return sb.ToString();
    }

    private static string BuildMeatOnTheBoneBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        RelicActivationRow(sb, agg.Activations.ToString());
        AppendHealingStats(sb, agg);
        return sb.ToString();
    }

    private static string BuildPlanisphereBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        RelicActivationRow(sb, agg.Activations.ToString());
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
        RelicActivationRow(sb, agg.Activations.ToString());
        AppendHealingStats(sb, agg, lostLabel: "healing wasted", reasonPrefix: "wasted to");
        return sb.ToString();
    }

    private static string BuildBurningBloodBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        ConceptRow(
            sb,
            "activation",
            agg.Activations.ToString(),
            "Times Burning Blood has activated.");
        ConceptRow(
            sb,
            "healing_gained",
            FormatDecimal(agg.TotalHealingRestored),
            "Total HP restored by Burning Blood.");
        ConceptRow(
            sb,
            "healing_blocked",
            FormatDecimal(agg.TotalHealingLost),
            "Total Burning Blood healing that did not restore HP.");

        if (agg.TotalHealingLost <= 0m) return sb.ToString();

        foreach (var reason in NonRedundantHealingLostReasons(agg))
        {
            var reasonName = string.IsNullOrWhiteSpace(reason.DisplayName)
                ? "other/prevented causes"
                : StatsTooltip.EscapeBbcode(reason.DisplayName);
            DescribedIconRow(
                sb,
                ["healing_blocked"],
                [],
                $"blocked by {reasonName}",
                FormatDecimal(reason.Amount),
                $"Burning Blood healing that did not restore HP because of {reasonName}.");
        }

        return sb.ToString();
    }

    private static string BuildLeesWaffleBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        AppendMaxHpChangeRows(sb, agg, "Max HP gained", MaxHpGained(agg));
        Row3(sb, "HP gained", FormatDecimal(agg.TotalHealingRestored), "");
        return sb.ToString();
    }

    private static string BuildFakeLeesWaffleBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        RelicActivationRow(sb, agg.Activations.ToString());
        AppendHealingStats(sb, agg);
        return sb.ToString();
    }

    private static string BuildStrawberryBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        RelicActivationRow(sb, agg.Activations.ToString());
        AppendMaxHpChangeRows(sb, agg, "Max HP gained", agg.MaxHpGained);
        return sb.ToString();
    }

    private static string BuildPearBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        RelicActivationRow(sb, agg.Activations.ToString());
        AppendMaxHpChangeRows(sb, agg, "Max HP gained", agg.MaxHpGained);
        return sb.ToString();
    }

    private static string BuildNutritiousOysterBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        RelicActivationRow(sb, agg.Activations.ToString());
        AppendMaxHpChangeRows(sb, agg, "Max HP gained", agg.MaxHpGained);
        return sb.ToString();
    }

    private static string BuildMangoBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        RelicActivationRow(sb, agg.Activations.ToString());
        AppendMaxHpChangeRows(sb, agg, "Max HP gained", agg.MaxHpGained);
        return sb.ToString();
    }

    private static string BuildStoneHumidifierBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var activations = agg.MaxHpActivations
            ?? new List<RelicMaxHpActivationAggregate>();

        RelicActivationRow(
            sb,
            agg.Activations.ToString(),
            "Times triggered — the number of times this relic has activated.");
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

    private static string BuildAmethystAubergineBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        RelicActivationRow(
            sb,
            agg.Activations.ToString(),
            "Times triggered — successful Amethyst Aubergine reward additions.");
        Row3(
            sb,
            "Extra gold received",
            agg.GoldGained.ToString(),
            "",
            "Extra gold received — the total amount on Gold rewards added by Amethyst Aubergine.");
        return sb.ToString();
    }

    private static string BuildWongosMysteryTicketBodyBBCode(
        RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var floorsBeforeActivation =
            agg.FloorAcquired.HasValue && agg.FloorActivated.HasValue
                ? Math.Max(
                    0,
                    agg.FloorActivated.Value - agg.FloorAcquired.Value)
                    .ToString()
                : "not yet";

        Row3(
            sb,
            "Floors ascended before activating",
            floorsBeforeActivation,
            "",
            "Floors ascended before activating — the distance from receiving Wongo's Mystery Ticket to the combat reward where it activated.");

        var relics = agg.RelicsGranted.Values
            .Where(relic => relic.Count > 0)
            .OrderByDescending(relic => relic.Count)
            .ThenBy(
                relic => relic.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
        var total = relics.Sum(relic => Math.Max(0, relic.Count));
        Row3(
            sb,
            "Relics received",
            total.ToString(),
            "",
            "Relics received — relics successfully claimed from Wongo's Mystery Ticket rewards.");

        foreach (var relic in relics)
        {
            var displayName = StatsTooltip.EscapeBbcode(
                string.IsNullOrWhiteSpace(relic.DisplayName)
                    ? RunTracker.FormatRelicIdForDisplay(relic.RelicId)
                    : relic.DisplayName);
            var value = relic.Count == 1
                ? displayName
                : $"{displayName} x{relic.Count}";
            TextValueRow(sb, "Relic received", value, "");
        }

        return sb.ToString();
    }

    private static string BuildMawBankBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        RelicActivationRow(
            sb,
            agg.Activations.ToString(),
            "Activations — completed room entries where Maw Bank was still active.");
        Row3(
            sb,
            "Gold gained",
            agg.GoldGained.ToString(),
            "",
            "Gold gained — the actual gold added by Maw Bank across its completed activations.");
        Row3(
            sb,
            "Shops skipped",
            agg.MawBankShopsSkipped.ToString(),
            "",
            "Shops skipped — shops entered while Maw Bank was active and left without spending gold.");
        Row3(
            sb,
            "Gold spent outside shops",
            agg.MawBankGoldSpentOutsideShops.ToString(),
            "",
            "Gold spent outside shops — actual gold spent while Maw Bank was active and the current room was not a shop.");
        return sb.ToString();
    }

    private static string BuildOldCoinBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(
            sb,
            "Granted gold spent",
            $"{agg.OldCoinGoldSpent}/{agg.OldCoinGoldGranted}",
            "",
            "Granted gold spent — how much of Old Coin's observed gold grant was later consumed by purchases.");
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
        RelicActivationRow(
            sb,
            agg.Activations.ToString(),
            "Total times triggered — the number of times this relic has activated.");
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

    private static string BuildWingedBootsBodyBBCode(
        RelicAggregate agg,
        int liveTimesUsed = 0)
    {
        var sb = new StringBuilder();
        var destinations = agg.WingedBootsDestinations
            ?? new List<WingedBootsDestinationAggregate>();

        for (var useNumber = 1; useNumber <= 3; useNumber++)
        {
            var destination = destinations.FirstOrDefault(
                entry => entry != null && entry.UseNumber == useNumber);
            var value = destination != null
                ? RunTracker.FormatWingedBootsDestination(destination.Destination)
                : useNumber <= liveTimesUsed
                    ? "not tracked"
                    : "not used yet";

            TextValueRow(sb, $"{Ordinal(useNumber)} floor destination", value, "");
        }

        return sb.ToString();
    }

    private static string Ordinal(int value)
        => value switch
        {
            1 => "1st",
            2 => "2nd",
            3 => "3rd",
            _ => value.ToString(),
        };

    private static string BuildLeafyPoulticeBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        RelicActivationRow(sb, agg.Activations.ToString());
        AppendMaxHpChangeRows(sb, agg, "Max HP lost", MaxHpLost(agg));
        AppendCardTransformationRows(sb, agg, expectedCount: 2);
        return sb.ToString();
    }

    private static string BuildRegalPillowBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        RelicActivationRow(sb, agg.Activations.ToString());
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
            TextValueRow(sb, "Removed card", StatsTooltip.EscapeBbcode(card), "");

        AppendMaxHpChangeRows(sb, agg, "Max HP lost", MaxHpLost(agg));
        return sb.ToString();
    }

    private static string BuildBloodVialBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        RelicActivationRow(sb, agg.Activations.ToString());
        AppendHealingStats(sb, agg);
        return sb.ToString();
    }

    private static string BuildToolboxBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        RelicActivationRow(sb, agg.Activations.ToString());
        Row3(sb, "Uncommon cards offered", agg.UncommonCardsOffered.ToString(), "");
        Row3(sb, "Rare cards offered", agg.RareCardsOffered.ToString(), "");
        Row3(sb, "Uncommon cards taken", agg.UncommonCardsTaken.ToString(), "");
        Row3(sb, "Rare cards taken", agg.RareCardsTaken.ToString(), "");
        return sb.ToString();
    }

    private static string BuildWhiteStarBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        RelicActivationRow(
            sb,
            agg.Activations.ToString(),
            "Activations — extra rare card rewards created by White Star after Elite victories.");
        DescribedIconRow(
            sb,
            ["card_rare"],
            [],
            "offered",
            agg.RareCardsOffered.ToString(),
            "Rares offered — Rare card options generated by White Star, including rerolled options.");
        DescribedIconRow(
            sb,
            ["attack_rare"],
            [],
            "offered",
            agg.RareAttackCardsOffered.ToString(),
            "Rare Attacks offered — Rare Attack options generated by White Star.");
        DescribedIconRow(
            sb,
            ["skill_rare"],
            [],
            "offered",
            agg.RareSkillCardsOffered.ToString(),
            "Rare Skills offered — Rare Skill options generated by White Star.");
        DescribedIconRow(
            sb,
            ["power_rare"],
            [],
            "offered",
            agg.RarePowerCardsOffered.ToString(),
            "Rare Powers offered — Rare Power options generated by White Star.");
        DescribedIconRow(
            sb,
            ["card_rare"],
            [],
            "reward screens declined",
            agg.RareCardRewardScreensDeclined.ToString(),
            "Rare card reward screens declined — White Star rewards terminally resolved without taking a Rare card.");
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
            TextValueRow(sb, "Granted", value, "");
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
            TextValueRow(sb, "Rare received", value, "");
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
            TextValueRow(sb, "Obtained", value, "");
        }

        return sb.ToString();
    }

    private static string BuildPaelsWingBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(sb, "common cards consumed", agg.CommonCardsConsumed.ToString(), "");
        Row3(sb, "uncommon cards consumed", agg.UncommonCardsConsumed.ToString(), "");
        Row3(sb, "rare cards consumed", agg.RareCardsConsumed.ToString(), "");
        var relics = agg.RelicsGranted.Values
            .Where(relic => relic.Count > 0)
            .OrderByDescending(relic => relic.Count)
            .ThenBy(relic => relic.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var relicsGained = relics.Sum(relic => Math.Max(0, relic.Count));
        Row3(sb, "Relics gained", relicsGained.ToString(), "");

        foreach (var relic in relics)
        {
            var value = RenderGrantedRelic(relic);
            TextValueRow(sb, "Relic gained", value, "");
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

    private static string RenderGrantedRelic(RelicGrantedAggregate relic)
    {
        var displayName = string.IsNullOrWhiteSpace(relic.DisplayName)
            ? RunTracker.FormatRelicIdForDisplay(relic.RelicId)
            : relic.DisplayName;
        var icon = StatConceptGlossary.RenderHintedInlineImage(
            ResolveGrantedRelicIconPath(relic.RelicId),
            displayName);
        return relic.Count > 1 ? $"{icon} ×{relic.Count}" : icon;
    }

    internal static string ResolveGrantedRelicIconPath(string? relicId)
    {
        if (string.IsNullOrWhiteSpace(relicId))
            return GenericRelicIconPath;

        try
        {
            var modelId = ModelId.Deserialize(relicId);
            var relicModel = ModelDb.GetByIdOrNull<RelicModel>(modelId);
            if (!string.IsNullOrWhiteSpace(relicModel?.IconPath))
                return relicModel.IconPath;

            if (string.Equals(modelId.Category, "RELIC", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(modelId.Entry))
            {
                return $"res://images/atlases/relic_atlas.sprites/"
                       + $"{modelId.Entry.ToLowerInvariant()}.tres";
            }
        }
        catch
        {
            // Historical data can outlive a removed model. Use the generic
            // relic artwork instead of restoring the old text-only value.
        }

        return GenericRelicIconPath;
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

            TextValueRow(sb, "Returned card", StatsTooltip.EscapeBbcode(displayName), "");
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
        RelicActivationRow(sb, agg.Activations.ToString());
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
        Row3(sb, "Non-upgraded attacks in deck", agg.MiniatureCannonNonUpgradedAttacksInDeck.ToString(), "");
        Row3(sb, "Upgraded attacks in combat", agg.MiniatureCannonUpgradedAttacksInCombat.ToString(), "");
        Row3(sb, "Non-upgraded attacks in combat", agg.MiniatureCannonNonUpgradedAttacksInCombat.ToString(), "");
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

    private static string BuildEmberTeaBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var attacksPerTurn = agg.EmberTeaActiveTurns <= 0
            ? 0m
            : (decimal)agg.EmberTeaAttacksPlayedWhileActive / agg.EmberTeaActiveTurns;
        var attacksPerCombat = agg.EmberTeaActiveCombats <= 0
            ? 0m
            : (decimal)agg.EmberTeaAttacksPlayedWhileActive / agg.EmberTeaActiveCombats;
        var hitsPerTurn = agg.EmberTeaActiveTurns <= 0
            ? 0m
            : (decimal)agg.EmberTeaHitsWhileActive / agg.EmberTeaActiveTurns;
        var hitsPerCombat = agg.EmberTeaActiveCombats <= 0
            ? 0m
            : (decimal)agg.EmberTeaHitsWhileActive / agg.EmberTeaActiveCombats;

        Row3(sb, "Attacks played while active", agg.EmberTeaAttacksPlayedWhileActive.ToString(), "");
        Row3(sb, "Avg attacks played per turn while active", FormatDecimal(attacksPerTurn), "");
        Row3(sb, "Avg attacks played per combat while active", FormatDecimal(attacksPerCombat), "");
        Row3(sb, "Hits while active", agg.EmberTeaHitsWhileActive.ToString(), "");
        Row3(sb, "Avg hits per turn while active", FormatDecimal(hitsPerTurn), "");
        Row3(sb, "Avg hits per combat while active", FormatDecimal(hitsPerCombat), "");
        return sb.ToString();
    }

    private static string BuildRedSkullBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var attacksPerTurn = agg.RedSkullActiveTurns <= 0
            ? 0m
            : (decimal)agg.RedSkullAttacksPlayedWhileActive / agg.RedSkullActiveTurns;
        var attacksPerCombat = agg.RedSkullActiveCombats <= 0
            ? 0m
            : (decimal)agg.RedSkullAttacksPlayedWhileActive / agg.RedSkullActiveCombats;
        var hitsPerTurn = agg.RedSkullActiveTurns <= 0
            ? 0m
            : (decimal)agg.RedSkullHitsWhileActive / agg.RedSkullActiveTurns;
        var hitsPerCombat = agg.RedSkullActiveCombats <= 0
            ? 0m
            : (decimal)agg.RedSkullHitsWhileActive / agg.RedSkullActiveCombats;

        Row3(sb, "Attacks played while active", agg.RedSkullAttacksPlayedWhileActive.ToString(), "");
        Row3(sb, "Avg attacks played while active per turn", FormatDecimal(attacksPerTurn), "");
        Row3(sb, "Avg attacks played while active per combat", FormatDecimal(attacksPerCombat), "");
        Row3(sb, "Hits while active", agg.RedSkullHitsWhileActive.ToString(), "");
        Row3(sb, "Avg hits while active per turn", FormatDecimal(hitsPerTurn), "");
        Row3(sb, "Avg hits while active per combat", FormatDecimal(hitsPerCombat), "");
        return sb.ToString();
    }

    private static string BuildToastyMittensBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var cardsPerCombat = agg.ToastyMittensCombats <= 0
            ? 0m
            : (decimal)agg.ToastyMittensCardsExhausted / agg.ToastyMittensCombats;
        var strengthPerCombat = agg.ToastyMittensCombats <= 0
            ? 0m
            : agg.StrengthAdded / agg.ToastyMittensCombats;

        Row3(sb, "Cards exhausted total", agg.ToastyMittensCardsExhausted.ToString(), "");
        Row3(sb, "Strength added total", FormatDecimal(agg.StrengthAdded), "");
        Row3(sb, "Cards exhausted per combat", FormatDecimal(cardsPerCombat), "");
        Row3(sb, "Strength added per combat", FormatDecimal(strengthPerCombat), "");
        return sb.ToString();
    }

    private static string BuildKunaiBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var averageEndCharge = agg.KunaiTurnEndChargeCount <= 0
            ? 0m
            : (decimal)agg.KunaiTurnEndChargeTotal / agg.KunaiTurnEndChargeCount;

        Row3(sb, "Attacks played", agg.KunaiAttacksPlayed.ToString(), "");
        RelicActivationRow(sb, agg.Activations.ToString());
        Row3(sb, "Dexterity gained", agg.KunaiDexterityGained.ToString(), "");
        Row3(sb, "Turns ended at 1 charge", agg.KunaiTurnsEndedAt1Charge.ToString(), "");
        Row3(sb, "Turns ended at 2 charges", agg.KunaiTurnsEndedAt2Charges.ToString(), "");
        Row3(sb, "Avg charge at turn end", FormatDecimal(averageEndCharge), "");
        return sb.ToString();
    }

    private static string BuildKusarigamaBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var turnsEndedAt0Charges = Math.Max(
            0,
            agg.KusarigamaTurnEndChargeCount
            - agg.KusarigamaTurnsEndedAt1Charge
            - agg.KusarigamaTurnsEndedAt2Charges);

        Row3(sb, "Attacks played", agg.KusarigamaAttacksPlayed.ToString(), "");
        AppendRelicDamageStats(
            sb,
            agg,
            triggerDescription: "Activations — the number of times this relic has activated.",
            averageLabel: "Damage per activation",
            averageDenominator: agg.Activations);
        Row3(sb, "Turns ended at 0 charges", turnsEndedAt0Charges.ToString(), "");
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
        RelicActivationRow(sb, agg.Activations.ToString());
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
        RelicActivationRow(sb, agg.Activations.ToString());
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

        RelicActivationRow(
            sb,
            agg.Activations.ToString(),
            "Times activated — the number of times this relic has activated.");
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

        RelicActivationRow(sb, agg.Activations.ToString());
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
        var energySavedPerTurn = agg.DiscountTurns <= 0
            ? 0m
            : (decimal)agg.BrilliantScarfEnergySavedForTurnAverage / agg.DiscountTurns;
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
        Row3(sb, EnergyLabel("saved / turn"), FormatDecimal(energySavedPerTurn), "");
        Row3(sb, EnergyLabel("saved / combat"), FormatDecimal(energySavedPerCombat), "");
        Row3(sb, EnergyLabel("saved / use"), FormatDecimal(energySavedPerUse), "");

        for (int energyCost = 0; energyCost <= 3; energyCost++)
        {
            Row3(
                sb,
                BrilliantScarfCostLabel(energyCost, starCost: 0),
                BrilliantScarfCostCount(agg, energyCost, starCost: 0).ToString(),
                "",
                BrilliantScarfCostDescription(energyCost, starCost: 0));
        }

        foreach (var bucket in DynamicBrilliantScarfCostBuckets(agg))
        {
            Row3(
                sb,
                BrilliantScarfCostLabel(bucket.EnergyCost, bucket.StarCost),
                bucket.Count.ToString(),
                "",
                BrilliantScarfCostDescription(bucket.EnergyCost, bucket.StarCost));
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

        RelicActivationRow(
            sb,
            agg.Activations.ToString(),
            "Times triggered — the number of times this relic has activated.");
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

    private static string BuildBurningSticksBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        var averageActivationsPerCombat = agg.BurningSticksCombats <= 0
            ? 0m
            : (decimal)agg.Activations / agg.BurningSticksCombats;
        var averageGeneratedCardPlaysPerCombat = agg.BurningSticksCombats <= 0
            ? 0m
            : (decimal)agg.BurningSticksGeneratedCardPlays / agg.BurningSticksCombats;

        RelicActivationRow(
            sb,
            agg.Activations.ToString(),
            "Times this relic successfully duplicated an exhausted Skill.");
        Row3(
            sb,
            "Avg activations per combat",
            FormatDecimal(averageActivationsPerCombat),
            "",
            "Successful Burning Sticks activations divided by combats where it was held.");
        Row3(
            sb,
            "Avg times generated card played per combat",
            FormatDecimal(averageGeneratedCardPlaysPerCombat),
            "",
            "Finished plays of cards generated by Burning Sticks divided by combats where it was held.");
        Row3(
            sb,
            "Commons duplicated",
            agg.BurningSticksCommonCardsDuplicated.ToString(),
            "",
            "Common cards successfully duplicated by Burning Sticks.");
        Row3(
            sb,
            "Uncommons duplicated",
            agg.BurningSticksUncommonCardsDuplicated.ToString(),
            "",
            "Uncommon cards successfully duplicated by Burning Sticks.");
        Row3(
            sb,
            "Rares duplicated",
            agg.BurningSticksRareCardsDuplicated.ToString(),
            "",
            "Rare cards successfully duplicated by Burning Sticks.");
        return sb.ToString();
    }

    private static string BuildBingBongBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        Row3(
            sb,
            "Extra cards added",
            agg.BingBongExtraCardsAdded.ToString(),
            "",
            "Extra cards successfully added to the permanent deck by Bing Bong.");
        Row3(
            sb,
            "Commons added",
            agg.BingBongCommonCardsAdded.ToString(),
            "",
            "Non-Curse Common cards successfully added to the permanent deck by Bing Bong.");
        Row3(
            sb,
            "Uncommons added",
            agg.BingBongUncommonCardsAdded.ToString(),
            "",
            "Non-Curse Uncommon cards successfully added to the permanent deck by Bing Bong.");
        Row3(
            sb,
            "Rares added",
            agg.BingBongRareCardsAdded.ToString(),
            "",
            "Non-Curse Rare cards successfully added to the permanent deck by Bing Bong.");
        Row3(
            sb,
            "Curses added",
            agg.BingBongCurseCardsAdded.ToString(),
            "",
            "Curse cards successfully added to the permanent deck by Bing Bong.");
        return sb.ToString();
    }

    private static string BuildJuzuBraceletBodyBBCode(RelicAggregate agg)
    {
        var sb = new StringBuilder();
        ConceptRow(
            sb,
            "unknown_room",
            agg.QuestionMarkSitesEntered.ToString(),
            "Question-mark map sites entered while Juzu Bracelet was held.");
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
        var averageActivationTurn = agg.CentennialPuzzleActivationTurnSamples <= 0
            ? 0m
            : (decimal)agg.CentennialPuzzleActivationTurnTotal
                / agg.CentennialPuzzleActivationTurnSamples;
        RelicActivationRow(sb, agg.Activations.ToString());
        Row3(sb, "Triggered this combat", triggeredThisCombat ? "true" : "false", "");
        Row3(sb, "Cards drawn total", agg.AdditionalCardsDrawn.ToString(), "");
        Row3(sb, "Avg cards drawn per combat", FormatDecimal(averageDrawn), "");
        Row3(
            sb,
            "Avg activation turn",
            FormatDecimal(averageActivationTurn),
            "",
            "The average player turn number when Centennial Puzzle activated.");
        Row3(
            sb,
            "Activated during your turn",
            agg.CentennialPuzzlePlayerTurnActivations.ToString(),
            "",
            "Times Centennial Puzzle activated during your turn.");
        Row3(
            sb,
            "Activated during opponent's turn",
            agg.CentennialPuzzleOpponentTurnActivations.ToString(),
            "",
            "Times Centennial Puzzle activated during the opponent's turn.");
        Row3(
            sb,
            "Activated by Status",
            agg.CentennialPuzzleStatusActivations.ToString(),
            "",
            "Times a Status card caused the HP loss that activated Centennial Puzzle.");
        Row3(
            sb,
            "Activated by Curse",
            agg.CentennialPuzzleCurseActivations.ToString(),
            "",
            "Times a Curse card caused the HP loss that activated Centennial Puzzle.");
        Row3(
            sb,
            "Activated by enemy source",
            agg.CentennialPuzzleEnemySourceActivations.ToString(),
            "",
            "Times an enemy attack or enemy-applied debuff caused the HP loss that activated Centennial Puzzle.");
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
        ConceptRow(
            sb,
            "activation",
            agg.Activations.ToString(),
            "Times this relic has been activated.");
        ConceptRow(
            sb,
            "osty_summon_gained",
            FormatDecimal(agg.TotalOstyHpSummoned),
            "Total Osty summon gained from this relic.");
        return sb.ToString();
    }

    private static string VulnerableLabel(string suffix)
    {
        var path = NormalizeResourcePath(VulnerableIconPath);
        return $"{StatConceptGlossary.RenderInlineImage(path)} {suffix}";
    }

    private static string WeakLabel(string suffix)
    {
        var path = NormalizeResourcePath(WeakIconPath);
        return $"{StatConceptGlossary.RenderInlineImage(path)} {suffix}";
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
        return $"{StatConceptGlossary.RenderInlineImage(path)} {suffix}";
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

        foreach (var reason in NonRedundantHealingLostReasons(agg))
        {
            var reasonName = string.IsNullOrWhiteSpace(reason.DisplayName)
                ? "other/prevented causes"
                : StatsTooltip.EscapeBbcode(reason.DisplayName);
            DescribedIconRow(
                sb,
                ["healing_blocked"],
                [],
                $"{reasonPrefix} {reasonName}",
                FormatDecimal(reason.Amount),
                $"Healing from this relic that did not restore HP because of {reasonName}.");
        }
    }

    private static IReadOnlyList<HealingLostReasonAggregate> NonRedundantHealingLostReasons(
        RelicAggregate agg)
    {
        var reasons = agg.HealingLostReasons.Values
            .Where(reason => reason.Amount > 0m)
            .OrderByDescending(reason => reason.Amount)
            .ThenBy(reason => reason.DisplayName)
            .ToList();

        if (reasons.Count == 1 && reasons[0].Amount == agg.TotalHealingLost)
            return Array.Empty<HealingLostReasonAggregate>();

        return reasons;
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
            TextValueRow(sb, $"Transform {i + 1} source", CardTransformationDisplay(
                transformation?.SourceDisplayName,
                transformation?.SourceCardId), "");
            TextValueRow(sb, $"Transform {i + 1} result", CardTransformationDisplay(
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
        return $"{StatEnergyIcon.RenderInline(StatConceptGlossary.IconSlotSize)} {suffix}";
    }

    private static string DrawLabel(string suffix)
    {
        var path = NormalizeResourcePath(DrawIconPath);
        return $"{StatConceptGlossary.RenderInlineImage(path)} {suffix}";
    }

    private static string BrilliantScarfCostLabel(int energyCost, int starCost)
    {
        var energyIcon = StatEnergyIcon.RenderInline(16);
        if (starCost > 0)
        {
            var starIcon = InlineIcon(StarIconPath);
            return $"{Math.Max(0, energyCost)} {energyIcon} {Math.Max(0, starCost)} {starIcon} cost reduced";
        }

        return $"{Math.Max(0, energyCost)} {energyIcon} cost reduced";
    }

    private static string BrilliantScarfCostDescription(int energyCost, int starCost)
    {
        var normalizedEnergy = Math.Max(0, energyCost);
        var normalizedStars = Math.Max(0, starCost);
        var starPart = normalizedStars > 0
            ? $" and {normalizedStars} Star{(normalizedStars == 1 ? string.Empty : "s")}"
            : string.Empty;
        return $"Cards discounted by Brilliant Scarf with a cost of "
               + $"{normalizedEnergy} Energy{starPart}.";
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
        return StatConceptGlossary.RenderInlineImage(normalized);
    }

    private static string VigorLabel(string suffix)
    {
        var path = NormalizeResourcePath(VigorIconPath);
        return $"{StatConceptGlossary.RenderInlineImage(path)} {suffix}";
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

    private static void Row3(
        StringBuilder sb,
        string label,
        string value,
        string pct,
        string? fullDescription = null)
    {
        var presentation = RelicStatRowVocabulary.Create(label, fullDescription);
        DescribedIconRow(
            sb,
            presentation.ConceptIds,
            presentation.DenominatorConceptIds,
            presentation.Label,
            value,
            presentation.FullDescription,
            pct);
    }

    private static void ConceptRow(
        StringBuilder sb,
        string conceptId,
        string value,
        string fullDescription,
        string pct = "")
    {
        DescribedIconRow(
            sb,
            [conceptId],
            [],
            string.Empty,
            value,
            fullDescription,
            pct);
    }

    private static void RelicActivationRow(
        StringBuilder sb,
        string value,
        string fullDescription = "Activations — the number of times this relic has activated.")
    {
        ConceptRow(
            sb,
            "activation",
            value,
            fullDescription);
    }

    private static void DescribedIconRow(
        StringBuilder sb,
        IReadOnlyList<string> conceptIds,
        IReadOnlyList<string> denominatorConceptIds,
        string label,
        string value,
        string fullDescription,
        string pct = "")
    {
        BeginOrContinueScalarTable(sb);
        sb.Append("[cell expand=0 padding=0,0,10,0]");
        sb.Append(StatConceptGlossary.RenderInformationHint(fullDescription));
        sb.Append("[/cell]");
        sb.Append("[cell expand=4 padding=0,0,12,0]");
        AppendConceptLabel(
            sb,
            conceptIds,
            denominatorConceptIds,
            label);
        sb.Append("[/cell]");
        sb.Append($"[cell expand=0 padding=0,0,12,0][right][b]{value}[/b][/right][/cell]");
        sb.Append($"[cell expand=0 padding=0,0,4,0][right][color=#b5b5b5]{pct}[/color][/right][/cell]");
        sb.Append(StatsTableClose);
    }

    private static void BeginOrContinueScalarTable(StringBuilder sb)
    {
        if (TryReopenTrailingScalarTable(sb))
            return;

        sb.Append(ScalarStatsTableOpen);
    }

    private static bool TryReopenTrailingScalarTable(StringBuilder sb)
    {
        if (!EndsWith(sb, StatsTableClose))
            return false;

        var lastTableStart = LastIndexOf(sb, "[table=");
        if (lastTableStart < 0
            || !MatchesAt(sb, lastTableStart, ScalarStatsTableOpen))
        {
            return false;
        }

        sb.Length -= StatsTableClose.Length;
        return true;
    }

    private static bool EndsWith(StringBuilder sb, string suffix)
    {
        if (sb.Length < suffix.Length)
            return false;

        return MatchesAt(sb, sb.Length - suffix.Length, suffix);
    }

    private static int LastIndexOf(StringBuilder sb, string value)
    {
        for (var start = sb.Length - value.Length; start >= 0; start--)
        {
            if (MatchesAt(sb, start, value))
                return start;
        }

        return -1;
    }

    private static bool MatchesAt(StringBuilder sb, int start, string value)
    {
        if (start < 0 || start + value.Length > sb.Length)
            return false;

        for (var index = 0; index < value.Length; index++)
        {
            if (sb[start + index] != value[index])
                return false;
        }

        return true;
    }

    private static void TextValueRow(StringBuilder sb, string label, string value, string pct)
    {
        var presentation = RelicStatRowVocabulary.Create(label);
        DescribedIconFlowRow(
            sb,
            presentation.ConceptIds,
            presentation.DenominatorConceptIds,
            presentation.Label,
            value,
            presentation.FullDescription,
            pct);
    }

    private static void DescribedIconFlowRow(
        StringBuilder sb,
        IReadOnlyList<string> conceptIds,
        IReadOnlyList<string> denominatorConceptIds,
        string label,
        string value,
        string fullDescription,
        string pct = "")
    {
        sb.Append("[table=2]");
        sb.Append("[cell expand=0 padding=0,0,10,0]");
        sb.Append(StatConceptGlossary.RenderInformationHint(fullDescription));
        sb.Append("[/cell]");
        sb.Append("[cell expand=4 padding=0,0,4,0]");
        AppendConceptLabel(
            sb,
            conceptIds,
            denominatorConceptIds,
            label);
        if (!string.IsNullOrEmpty(value))
        {
            sb.Append($"  [b]{value}[/b]");
        }
        if (!string.IsNullOrEmpty(pct))
        {
            sb.Append($"  [color=#b5b5b5]{pct}[/color]");
        }
        sb.Append("[/cell]");
        sb.Append(StatsTableClose);
    }

    private static void AppendConceptLabel(
        StringBuilder sb,
        IReadOnlyList<string> conceptIds,
        IReadOnlyList<string> denominatorConceptIds,
        string label)
    {
        for (var index = 0; index < conceptIds.Count; index++)
        {
            if (index > 0) sb.Append(' ');
            if (denominatorConceptIds.Contains(
                    conceptIds[index],
                    StringComparer.Ordinal))
            {
                sb.Append("[color=#b5b5b5]/[/color] ");
            }
            sb.Append(StatConceptGlossary.RenderHintedGlyph(conceptIds[index]));
        }

        if (string.IsNullOrWhiteSpace(label)) return;

        if (conceptIds.Count > 0) sb.Append(' ');
        sb.Append($"[color=#e0e0e0]{label}[/color]");
    }

}
