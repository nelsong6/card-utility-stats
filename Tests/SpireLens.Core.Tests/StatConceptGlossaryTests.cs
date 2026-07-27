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
                "attack",
                "average",
                "block",
                "block_wasted",
                "card",
                "charge",
                "combat",
                "attack_common",
                "power_common",
                "skill_common",
                "damage",
                "dexterity",
                "dexterity_gained",
                "discard",
                "draw",
                "energy",
                "energy_wasted",
                "exhaust",
                "floor",
                "gold",
                "healing_blocked",
                "healing_gained",
                "healing_wasted",
                "information",
                "max_hp",
                "nimble",
                "osty_summon_gained",
                "potion",
                "power",
                "attack_rare",
                "card_rare",
                "power_rare",
                "skill_rare",
                "relic",
                "skill",
                "stars",
                "strength",
                "strength_gained",
                "turn",
                "attack_uncommon",
                "card_uncommon",
                "power_uncommon",
                "skill_uncommon",
                "unknown_room",
                "upgraded",
                "vigor",
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
        Assert.Equal(20, StatConceptGlossary.IconSlotSize);
    }

    [Fact]
    public void Glossary_RenderersIncludeNativeHintsAndConfiguredGlyphs()
    {
        var activation = StatConceptGlossary.RenderHintedGlyph("activation");
        var attack = StatConceptGlossary.RenderHintedGlyph("attack");
        var average = StatConceptGlossary.RenderHintedGlyph("average");
        var block = StatConceptGlossary.RenderHintedGlyph("block");
        var blockWasted = StatConceptGlossary.RenderHintedGlyph("block_wasted");
        var card = StatConceptGlossary.RenderHintedGlyph("card");
        var rareCard = StatConceptGlossary.RenderHintedGlyph("card_rare");
        var uncommonCard = StatConceptGlossary.RenderHintedGlyph("card_uncommon");
        var charge = StatConceptGlossary.RenderHintedGlyph("charge");
        var combat = StatConceptGlossary.RenderHintedGlyph("combat");
        var dexterityGained = StatConceptGlossary.RenderHintedGlyph("dexterity_gained");
        var floor = StatConceptGlossary.RenderHintedGlyph("floor");
        var healingBlocked = StatConceptGlossary.RenderHintedGlyph("healing_blocked");
        var healingGained = StatConceptGlossary.RenderHintedGlyph("healing_gained");
        var healingWasted = StatConceptGlossary.RenderHintedGlyph("healing_wasted");
        var informationConcept = StatConceptGlossary.RenderHintedGlyph("information");
        var nimble = StatConceptGlossary.RenderHintedGlyph("nimble");
        var summon = StatConceptGlossary.RenderHintedGlyph("osty_summon_gained");
        var power = StatConceptGlossary.RenderHintedGlyph("power");
        var skill = StatConceptGlossary.RenderHintedGlyph("skill");
        var strengthGained = StatConceptGlossary.RenderHintedGlyph("strength_gained");
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
            attack,
            average,
            block,
            blockWasted,
            card,
            rareCard,
            uncommonCard,
            charge,
            combat,
            dexterityGained,
            floor,
            healingBlocked,
            healingGained,
            healingWasted,
            informationConcept,
            nimble,
            summon,
            power,
            skill,
            strengthGained,
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
        Assert.Contains("[hint=\"Card:", card);
        Assert.Contains("res://images/ui/reward_screen/reward_icon_card.png", card);
        Assert.Contains("[hint=\"Rare card:", rareCard);
        Assert.Contains("color=#EFC850", rareCard);
        Assert.Contains("[hint=\"Uncommon card:", uncommonCard);
        Assert.Contains("color=#87CEEB", uncommonCard);
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
        Assert.Contains("[hint=\"Floor:", floor);
        Assert.Contains(
            "res://images/atlases/ui_atlas.sprites/top_bar/top_bar_floor.tres",
            floor);
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
        Assert.Contains("[hint=\"Information:", informationConcept);
        Assert.Contains(
            "user://SpireLens/generated-icons/information-",
            informationConcept);
        Assert.Contains("[hint=\"Nimble:", nimble);
        Assert.Contains("res://images/enchantments/nimble.png", nimble);
        Assert.Contains(
            "res://images/atlases/relic_atlas.sprites/bound_phylactery.tres",
            summon);
        Assert.Contains("[hint=\"Power:", power);
        Assert.Contains(
            "res://images/packed/card_library/type_sort_power.png",
            power);
        Assert.Contains("[hint=\"Skill:", skill);
        Assert.Contains(
            "res://images/packed/card_library/type_sort_skill.png",
            skill);
        Assert.Contains("[hint=\"Strength gained:", strengthGained);
        Assert.Contains(
            "user://SpireLens/generated-icons/strength_gained-",
            strengthGained);
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
