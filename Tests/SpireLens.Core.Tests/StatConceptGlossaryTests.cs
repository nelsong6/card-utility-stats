using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

public class StatConceptGlossaryTests
{
    [Fact]
    public void Glossary_LoadsTheInitialConceptVocabulary()
    {
        Assert.Equal(
            [
                "activation",
                "all",
                "attack",
                "average",
                "block",
                "block_gained",
                "block_wasted",
                "campfire",
                "card",
                "charge",
                "combat",
                "attack_common",
                "potion_common",
                "power_common",
                "relic_common",
                "skill_common",
                "hp",
                "damage",
                "dexterity",
                "dexterity_gained",
                "discard",
                "draw",
                "elite",
                "energy",
                "energy_gained",
                "energy_wasted",
                "exhaust",
                "floor",
                "fruit_juice",
                "glam",
                "gold",
                "gold_gained",
                "healing_blocked",
                "healing_gained",
                "healing_wasted",
                "in",
                "information",
                "kill",
                "max_hp",
                "max_hp_gained",
                "shop",
                "nimble",
                "offered",
                "osty_summon_gained",
                "per",
                "potion",
                "potion_gained",
                "power",
                "attack_rare",
                "card_rare",
                "potion_rare",
                "power_rare",
                "relic_rare",
                "skill_rare",
                "relic",
                "relic_gained",
                "skill",
                "stars",
                "strength",
                "strength_gained",
                "swift",
                "taken",
                "turn",
                "attack_uncommon",
                "card_uncommon",
                "potion_uncommon",
                "power_uncommon",
                "relic_uncommon",
                "skill_uncommon",
                "unknown_room",
                "upgraded",
                "vigor",
                "vigor_gained",
                "vulnerable",
                "wasted",
                "weak",
            ],
            StatConceptGlossary.Concepts.Select(concept => concept.Id));
        Assert.Equal(
            StatConceptDisplayType.EmbeddedImage,
            StatConceptGlossary.Concepts.Single(concept => concept.Id == "activation").Display.Type);
        Assert.Equal(
            StatConceptDisplayType.EmbeddedImage,
            StatConceptGlossary.Concepts.Single(concept => concept.Id == "healing_blocked").Display.Type);
        Assert.Equal(
            StatConceptDisplayType.EmbeddedImage,
            StatConceptGlossary.Concepts.Single(concept => concept.Id == "healing_gained").Display.Type);
        Assert.Equal(
            StatConceptDisplayType.GameResourceBadge,
            StatConceptGlossary.Concepts.Single(concept => concept.Id == "dexterity_gained").Display.Type);
        Assert.Equal(
            StatConceptDisplayType.GameResourceBadge,
            StatConceptGlossary.Concepts.Single(concept => concept.Id == "strength_gained").Display.Type);
        Assert.Equal(
            StatConceptDisplayType.GameResourceOverlay,
            StatConceptGlossary.Concepts.Single(concept => concept.Id == "elite").Display.Type);
        Assert.All(
            [
                "block_gained",
                "energy_gained",
                "gold_gained",
                "max_hp_gained",
                "potion_gained",
                "relic_gained",
                "vigor_gained",
            ],
            conceptId => Assert.Equal(
                StatConceptDisplayType.GameResourceBadge,
                StatConceptGlossary.Concepts
                    .Single(concept => concept.Id == conceptId)
                    .Display.Type));
        Assert.Equal(20, StatConceptGlossary.IconSlotSize);
    }

    [Fact]
    public void Glossary_RenderersIncludeNativeHintsAndConfiguredGlyphs()
    {
        var activation = StatConceptGlossary.RenderHintedGlyph("activation");
        var all = StatConceptGlossary.RenderHintedGlyph("all");
        var attack = StatConceptGlossary.RenderHintedGlyph("attack");
        var average = StatConceptGlossary.RenderHintedGlyph("average");
        var block = StatConceptGlossary.RenderHintedGlyph("block");
        var blockWasted = StatConceptGlossary.RenderHintedGlyph("block_wasted");
        var campfire = StatConceptGlossary.RenderHintedGlyph("campfire");
        var card = StatConceptGlossary.RenderHintedGlyph("card");
        var rareCard = StatConceptGlossary.RenderHintedGlyph("card_rare");
        var uncommonCard = StatConceptGlossary.RenderHintedGlyph("card_uncommon");
        var commonPotion = StatConceptGlossary.RenderHintedGlyph("potion_common");
        var rarePotion = StatConceptGlossary.RenderHintedGlyph("potion_rare");
        var uncommonPotion = StatConceptGlossary.RenderHintedGlyph("potion_uncommon");
        var charge = StatConceptGlossary.RenderHintedGlyph("charge");
        var combat = StatConceptGlossary.RenderHintedGlyph("combat");
        var dexterityGained = StatConceptGlossary.RenderHintedGlyph("dexterity_gained");
        var elite = StatConceptGlossary.RenderHintedGlyph("elite");
        var floor = StatConceptGlossary.RenderHintedGlyph("floor");
        var glam = StatConceptGlossary.RenderHintedGlyph("glam");
        var healingBlocked = StatConceptGlossary.RenderHintedGlyph("healing_blocked");
        var healingGained = StatConceptGlossary.RenderHintedGlyph("healing_gained");
        var healingWasted = StatConceptGlossary.RenderHintedGlyph("healing_wasted");
        var inScope = StatConceptGlossary.RenderHintedGlyph("in");
        var informationConcept = StatConceptGlossary.RenderHintedGlyph("information");
        var kill = StatConceptGlossary.RenderHintedGlyph("kill");
        var nimble = StatConceptGlossary.RenderHintedGlyph("nimble");
        var offered = StatConceptGlossary.RenderHintedGlyph("offered");
        var summon = StatConceptGlossary.RenderHintedGlyph("osty_summon_gained");
        var per = StatConceptGlossary.RenderHintedGlyph("per");
        var power = StatConceptGlossary.RenderHintedGlyph("power");
        var commonRelic = StatConceptGlossary.RenderHintedGlyph("relic_common");
        var rareRelic = StatConceptGlossary.RenderHintedGlyph("relic_rare");
        var uncommonRelic = StatConceptGlossary.RenderHintedGlyph("relic_uncommon");
        var skill = StatConceptGlossary.RenderHintedGlyph("skill");
        var shop = StatConceptGlossary.RenderHintedGlyph("shop");
        var strengthGained = StatConceptGlossary.RenderHintedGlyph("strength_gained");
        var swift = StatConceptGlossary.RenderHintedGlyph("swift");
        var taken = StatConceptGlossary.RenderHintedGlyph("taken");
        var turn = StatConceptGlossary.RenderHintedGlyph("turn");
        var unknownRoom = StatConceptGlossary.RenderHintedGlyph("unknown_room");
        var upgraded = StatConceptGlossary.RenderHintedGlyph("upgraded");
        var energy = StatConceptGlossary.RenderHintedGlyph("energy");
        var energyWasted = StatConceptGlossary.RenderHintedGlyph("energy_wasted");
        var wasted = StatConceptGlossary.RenderHintedGlyph("wasted");
        var information = StatConceptGlossary.RenderInformationHint(
            "Times this relic has been activated.");

        var defaultGlyphs = new[]
        {
            activation,
            all,
            attack,
            average,
            block,
            blockWasted,
            campfire,
            card,
            rareCard,
            uncommonCard,
            commonPotion,
            rarePotion,
            uncommonPotion,
            charge,
            combat,
            dexterityGained,
            elite,
            floor,
            glam,
            healingBlocked,
            healingGained,
            healingWasted,
            inScope,
            informationConcept,
            kill,
            nimble,
            offered,
            summon,
            per,
            power,
            commonRelic,
            rareRelic,
            uncommonRelic,
            skill,
            shop,
            strengthGained,
            swift,
            taken,
            turn,
            unknownRoom,
            upgraded,
            energy,
            energyWasted,
            wasted,
        };
        Assert.All(
            defaultGlyphs,
            glyph => Assert.Contains("[img width=20 height=20", glyph));
        Assert.All(
            defaultGlyphs,
            glyph => Assert.DoesNotContain("[font_size=", glyph));

        Assert.Contains("[hint=\"Activation:", activation);
        Assert.Contains(
            "user://SpireLens/generated-icons/activation-",
            activation);
        Assert.Contains("[hint=\"All:", all);
        Assert.Contains(
            "user://SpireLens/generated-icons/all-",
            all);
        Assert.Contains("[hint=\"Attack:", attack);
        Assert.Contains(
            "res://images/packed/card_library/type_sort_attack.png",
            attack);
        Assert.Contains("[hint=\"Average:", average);
        Assert.Contains(
            "user://SpireLens/generated-icons/average-",
            average);
        Assert.Contains("[hint=\"Block:", block);
        Assert.Contains("res://images/ui/combat/block.png", block);
        Assert.Contains("[hint=\"Block wasted:", blockWasted);
        Assert.Contains(
            "user://SpireLens/generated-icons/block_wasted-",
            blockWasted);
        Assert.Contains("[hint=\"Campfire:", campfire);
        Assert.Contains(
            "res://images/atlases/ui_atlas.sprites/map/icons/map_rest.tres",
            campfire);
        Assert.Contains("[hint=\"Card:", card);
        Assert.Contains("res://images/ui/reward_screen/reward_icon_card.png", card);
        Assert.Contains("[hint=\"Rare card:", rareCard);
        Assert.Contains("color=#EFC850", rareCard);
        Assert.Contains("[hint=\"Uncommon card:", uncommonCard);
        Assert.Contains("color=#87CEEB", uncommonCard);
        Assert.Contains("[hint=\"Common potion:", commonPotion);
        Assert.Contains("potion_icon.png", commonPotion);
        Assert.Contains("color=#B5B5B5", commonPotion);
        Assert.Contains("[hint=\"Rare potion:", rarePotion);
        Assert.Contains("potion_icon.png", rarePotion);
        Assert.Contains("color=#EFC850", rarePotion);
        Assert.Contains("[hint=\"Uncommon potion:", uncommonPotion);
        Assert.Contains("potion_icon.png", uncommonPotion);
        Assert.Contains("color=#87CEEB", uncommonPotion);
        Assert.Contains("[hint=\"Charge:", charge);
        Assert.Contains(
            "user://SpireLens/generated-icons/charge-",
            charge);
        Assert.Contains("[hint=\"Combat:", combat);
        Assert.Contains(
            "res://images/atlases/ui_atlas.sprites/map/icons/map_monster.tres",
            combat);
        Assert.Contains("[hint=\"Dexterity gained:", dexterityGained);
        Assert.Contains(
            "user://SpireLens/generated-icons/dexterity_gained-",
            dexterityGained);
        Assert.Contains("[hint=\"Elite:", elite);
        Assert.Contains(
            "user://SpireLens/generated-icons/elite-",
            elite);
        Assert.Contains("[hint=\"Floor:", floor);
        Assert.Contains(
            "res://images/atlases/ui_atlas.sprites/top_bar/top_bar_floor.tres",
            floor);
        Assert.Contains("[hint=\"Glam:", glam);
        Assert.Contains("res://images/enchantments/glam.png", glam);
        Assert.Contains("[hint=\"Osty summon gained:", summon);
        Assert.Contains("[hint=\"Healing blocked:", healingBlocked);
        var healingBlockedDisplay = StatConceptGlossary.Concepts
            .Single(concept => concept.Id == "healing_blocked")
            .Display;
        Assert.Equal("Assets.healing-blocked.png", healingBlockedDisplay.Value);
        Assert.Contains(
            "user://SpireLens/generated-icons/healing_blocked-",
            healingBlocked);
        Assert.Contains("[hint=\"Healing gained:", healingGained);
        Assert.Contains(
            "user://SpireLens/generated-icons/healing_gained-",
            healingGained);
        Assert.Contains("[hint=\"Healing wasted:", healingWasted);
        Assert.Contains(
            "user://SpireLens/generated-icons/healing_wasted-",
            healingWasted);
        Assert.Contains("[hint=\"In:", inScope);
        Assert.Contains(
            "user://SpireLens/generated-icons/in-",
            inScope);
        Assert.Contains("[hint=\"Information:", informationConcept);
        Assert.Contains(
            "user://SpireLens/generated-icons/information-",
            informationConcept);
        Assert.Contains("[hint=\"Kill:", kill);
        Assert.Contains("res://images/ui/emote/skull.png", kill);
        Assert.Contains("[hint=\"Nimble:", nimble);
        Assert.Contains("res://images/enchantments/nimble.png", nimble);
        Assert.Contains("[hint=\"Offered:", offered);
        Assert.Contains(
            "user://SpireLens/generated-icons/offered-",
            offered);
        Assert.Contains(
            "res://images/atlases/relic_atlas.sprites/bound_phylactery.tres",
            summon);
        Assert.Contains("[hint=\"Per:", per);
        Assert.Contains(
            "user://SpireLens/generated-icons/per-",
            per);
        Assert.Contains("[hint=\"Power:", power);
        Assert.Contains(
            "res://images/packed/card_library/type_sort_power.png",
            power);
        Assert.Contains("[hint=\"Common relic:", commonRelic);
        Assert.Contains("color=#B5B5B5", commonRelic);
        Assert.Contains("[hint=\"Rare relic:", rareRelic);
        Assert.Contains("color=#EFC850", rareRelic);
        Assert.Contains("[hint=\"Uncommon relic:", uncommonRelic);
        Assert.Contains("color=#87CEEB", uncommonRelic);
        Assert.Contains("[hint=\"Skill:", skill);
        Assert.Contains(
            "res://images/packed/card_library/type_sort_skill.png",
            skill);
        Assert.Contains("[hint=\"Merchant:", shop);
        Assert.Contains(
            "res://images/atlases/ui_atlas.sprites/map/icons/map_shop.tres",
            shop);
        Assert.Contains("[hint=\"Strength gained:", strengthGained);
        Assert.Contains(
            "user://SpireLens/generated-icons/strength_gained-",
            strengthGained);
        Assert.Contains("[hint=\"Swift:", swift);
        Assert.Contains("res://images/enchantments/swift.png", swift);
        Assert.Contains("[hint=\"Taken:", taken);
        Assert.Contains(
            "user://SpireLens/generated-icons/taken-",
            taken);
        Assert.Contains("[hint=\"Turn:", turn);
        Assert.Contains(
            "user://SpireLens/generated-icons/turn-",
            turn);
        Assert.Contains("[hint=\"Unknown room:", unknownRoom);
        Assert.Contains(
            "res://images/atlases/ui_atlas.sprites/map/icons/map_unknown.tres",
            unknownRoom);
        Assert.Contains("[hint=\"Upgraded:", upgraded);
        Assert.Contains(
            "res://images/ui/cards/upgrade_preview/upgrade_arrow.png",
            upgraded);
        Assert.Contains("[hint=\"Energy:", energy);
        Assert.Contains(
            "res://images/packed/sprite_fonts/ironclad_energy_icon.png",
            energy);
        Assert.Contains("[hint=\"Energy wasted:", energyWasted);
        Assert.Contains(
            "user://SpireLens/generated-icons/energy_wasted-",
            energyWasted);
        Assert.Contains("[hint=\"Wasted:", wasted);
        Assert.Contains(
            "user://SpireLens/generated-icons/wasted-",
            wasted);
        Assert.Contains("[img width=20 height=20", information);
        Assert.Contains(
            "user://SpireLens/generated-icons/information-",
            information);
        Assert.Contains("Times this relic has been activated.", information);
    }
}
