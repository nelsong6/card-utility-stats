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
                "healing_blocked",
                "healing_gained",
                "osty_summon_gained",
            ],
            StatConceptGlossary.Concepts.Select(concept => concept.Id));
        Assert.Equal(
            StatConceptDisplayType.StyledText,
            StatConceptGlossary.Concepts.Single(concept => concept.Id == "activation").Display.Type);
        Assert.Equal(
            StatConceptDisplayType.GameResourceGroup,
            StatConceptGlossary.Concepts.Single(concept => concept.Id == "healing_blocked").Display.Type);
        Assert.Equal(
            StatConceptDisplayType.GameResource,
            StatConceptGlossary.Concepts.Single(concept => concept.Id == "healing_gained").Display.Type);
    }

    [Fact]
    public void Glossary_RenderersIncludeNativeHintsAndConfiguredGlyphs()
    {
        var activation = StatConceptGlossary.RenderHintedGlyph("activation");
        var healingBlocked = StatConceptGlossary.RenderHintedGlyph("healing_blocked");
        var healingGained = StatConceptGlossary.RenderHintedGlyph("healing_gained");
        var summon = StatConceptGlossary.RenderHintedGlyph("osty_summon_gained");
        var information = StatConceptGlossary.RenderInformationHint(
            "Times this relic has been activated.");

        Assert.Contains("[hint=\"Activation:", activation);
        Assert.Contains("[color=#F4C95D][b]A[/b][/color]", activation);
        Assert.Contains("[hint=\"Osty summon gained:", summon);
        Assert.Contains("[hint=\"Healing blocked:", healingBlocked);
        Assert.Contains(
            "res://images/atlases/ui_atlas.sprites/top_bar/top_bar_heart.tres",
            healingBlocked);
        Assert.Contains("res://images/ui/combat/block.png", healingBlocked);
        Assert.Contains("[hint=\"Healing gained:", healingGained);
        Assert.Contains(
            "res://images/atlases/power_atlas.sprites/regen_power.tres",
            healingGained);
        Assert.Contains(
            "res://images/atlases/relic_atlas.sprites/bound_phylactery.tres",
            summon);
        Assert.Contains("ⓘ", information);
        Assert.Contains("Times this relic has been activated.", information);
    }
}
