using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

public class StatConceptGlossaryTests
{
    [Fact]
    public void Glossary_LoadsTheInitialConceptVocabulary()
    {
        Assert.Collection(
            StatConceptGlossary.Concepts,
            activation =>
            {
                Assert.Equal("activation", activation.Id);
                Assert.Equal("Activation", activation.Label);
                Assert.Equal(StatConceptDisplayType.StyledText, activation.Display.Type);
            },
            summon =>
            {
                Assert.Equal("osty_summon_gained", summon.Id);
                Assert.Equal("Osty summon gained", summon.Label);
                Assert.Equal(StatConceptDisplayType.GameResource, summon.Display.Type);
            });
    }

    [Fact]
    public void Glossary_RenderersIncludeNativeHintsAndConfiguredGlyphs()
    {
        var activation = StatConceptGlossary.RenderHintedGlyph("activation");
        var summon = StatConceptGlossary.RenderHintedGlyph("osty_summon_gained");
        var information = StatConceptGlossary.RenderInformationHint(
            "Times this relic has been activated.");

        Assert.Contains("[hint=\"Activation:", activation);
        Assert.Contains("[color=#F4C95D][b]A[/b][/color]", activation);
        Assert.Contains("[hint=\"Osty summon gained:", summon);
        Assert.Contains(
            "res://images/atlases/relic_atlas.sprites/bound_phylactery.tres",
            summon);
        Assert.Contains("ⓘ", information);
        Assert.Contains("Times this relic has been activated.", information);
    }
}
