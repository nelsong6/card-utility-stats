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
                "average",
                "block",
                "card",
                "charge",
                "combat",
                "floor",
                "healing_blocked",
                "healing_gained",
                "information",
                "osty_summon_gained",
                "turn",
                "upgraded",
            ],
            StatConceptGlossary.Concepts.Select(concept => concept.Id));
        Assert.Equal(
            StatConceptDisplayType.EmbeddedImage,
            StatConceptGlossary.Concepts.Single(concept => concept.Id == "activation").Display.Type);
        Assert.Equal(
            StatConceptDisplayType.EmbeddedImage,
            StatConceptGlossary.Concepts.Single(concept => concept.Id == "healing_blocked").Display.Type);
        Assert.Equal(
            StatConceptDisplayType.GameResource,
            StatConceptGlossary.Concepts.Single(concept => concept.Id == "healing_gained").Display.Type);
        Assert.Equal(20, StatConceptGlossary.IconSlotSize);
    }

    [Fact]
    public void Glossary_RenderersIncludeNativeHintsAndConfiguredGlyphs()
    {
        var activation = StatConceptGlossary.RenderHintedGlyph("activation");
        var average = StatConceptGlossary.RenderHintedGlyph("average");
        var block = StatConceptGlossary.RenderHintedGlyph("block");
        var card = StatConceptGlossary.RenderHintedGlyph("card");
        var charge = StatConceptGlossary.RenderHintedGlyph("charge");
        var combat = StatConceptGlossary.RenderHintedGlyph("combat");
        var floor = StatConceptGlossary.RenderHintedGlyph("floor");
        var healingBlocked = StatConceptGlossary.RenderHintedGlyph("healing_blocked");
        var healingGained = StatConceptGlossary.RenderHintedGlyph("healing_gained");
        var informationConcept = StatConceptGlossary.RenderHintedGlyph("information");
        var summon = StatConceptGlossary.RenderHintedGlyph("osty_summon_gained");
        var turn = StatConceptGlossary.RenderHintedGlyph("turn");
        var upgraded = StatConceptGlossary.RenderHintedGlyph("upgraded");
        var information = StatConceptGlossary.RenderInformationHint(
            "Times this relic has been activated.");

        var defaultGlyphs = new[]
        {
            activation,
            average,
            block,
            card,
            charge,
            combat,
            floor,
            healingBlocked,
            healingGained,
            informationConcept,
            summon,
            turn,
            upgraded,
        };
        Assert.All(
            defaultGlyphs,
            glyph => Assert.Contains("[img width=20 height=20", glyph));
        Assert.All(
            defaultGlyphs,
            glyph => Assert.DoesNotContain("[font_size=", glyph));

        Assert.Contains("[hint=\"Activation:", activation);
        Assert.Contains(
            "user://SpireLens/generated-icons/activation.tres",
            activation);
        Assert.Contains("[hint=\"Average:", average);
        Assert.Contains(
            "user://SpireLens/generated-icons/average.tres",
            average);
        Assert.Contains("[hint=\"Block:", block);
        Assert.Contains("res://images/ui/combat/block.png", block);
        Assert.Contains("[hint=\"Card:", card);
        Assert.Contains("res://images/ui/reward_screen/reward_icon_card.png", card);
        Assert.Contains("[hint=\"Charge:", charge);
        Assert.Contains(
            "user://SpireLens/generated-icons/charge.tres",
            charge);
        Assert.Contains("[hint=\"Combat:", combat);
        Assert.Contains(
            "res://images/atlases/ui_atlas.sprites/map/icons/map_monster.tres",
            combat);
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
            "user://SpireLens/generated-icons/healing_blocked.tres",
            healingBlocked);
        Assert.Contains("[hint=\"Healing gained:", healingGained);
        Assert.Contains(
            "res://images/atlases/power_atlas.sprites/regen_power.tres",
            healingGained);
        Assert.Contains("[hint=\"Information:", informationConcept);
        Assert.Contains(
            "user://SpireLens/generated-icons/information.tres",
            informationConcept);
        Assert.Contains(
            "res://images/atlases/relic_atlas.sprites/bound_phylactery.tres",
            summon);
        Assert.Contains("[hint=\"Turn:", turn);
        Assert.Contains(
            "user://SpireLens/generated-icons/turn.tres",
            turn);
        Assert.Contains("[hint=\"Upgraded:", upgraded);
        Assert.Contains(
            "res://images/ui/cards/upgrade_preview/upgrade_arrow.png",
            upgraded);
        Assert.Contains("[img width=20 height=20", information);
        Assert.Contains(
            "user://SpireLens/generated-icons/information.tres",
            information);
        Assert.Contains("Times this relic has been activated.", information);
    }
}
