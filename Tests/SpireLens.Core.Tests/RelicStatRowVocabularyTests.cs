using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

public class RelicStatRowVocabularyTests
{
    [Theory]
    [InlineData(
        "avg block retained per turn",
        "retained",
        "average,block,turn")]
    [InlineData(
        "Avg cards added per floor",
        "added",
        "average,card,floor")]
    [InlineData(
        "Avg activations per combat",
        "",
        "average,activation,combat")]
    [InlineData(
        "Turns ended at 1 charge",
        "ended at 1",
        "turn,charge")]
    [InlineData(
        "Cards upgraded",
        "",
        "card,upgraded")]
    [InlineData(
        "Cards drawn total",
        "total",
        "draw")]
    [InlineData(
        "Avg cards drawn per combat",
        "",
        "average,draw,combat")]
    [InlineData(
        "Rare cards drawn",
        "",
        "card_rare,draw")]
    [InlineData(
        "Triggered this combat",
        "",
        "activation,combat")]
    [InlineData(
        "HP healed",
        "",
        "healing_gained")]
    [InlineData(
        "healing lost",
        "",
        "healing_blocked")]
    [InlineData(
        "Max HP gained",
        "gained",
        "max_hp")]
    [InlineData(
        "Non-upgraded attacks in combat",
        "Non-upgraded",
        "attack,combat")]
    [InlineData(
        "Commons picked",
        "picked",
        "card")]
    [InlineData(
        "Uncommons picked",
        "picked",
        "card_uncommon")]
    [InlineData(
        "Rares picked",
        "picked",
        "card_rare")]
    [InlineData(
        "Attacks copied",
        "copied",
        "attack")]
    [InlineData(
        "Powers copied",
        "copied",
        "power")]
    [InlineData(
        "Skills copied",
        "copied",
        "skill")]
    [InlineData(
        "Gold loss blocked",
        "loss blocked",
        "gold")]
    [InlineData(
        "Total damage dealt",
        "Total dealt",
        "damage")]
    [InlineData(
        "Strength gained per activation",
        "gained",
        "strength,activation")]
    [InlineData(
        "Cards exhausted per combat",
        "",
        "card,exhaust,combat")]
    [InlineData(
        "Potions gained",
        "gained",
        "potion")]
    public void Create_ReplacesKnownRelicConceptWords(
        string label,
        string expectedLabel,
        string expectedConcepts)
    {
        var presentation = RelicStatRowVocabulary.Create(label);

        Assert.Equal(expectedLabel, presentation.Label);
        Assert.Equal(
            expectedConcepts,
            string.Join(",", presentation.ConceptIds));
        Assert.Contains(label, presentation.FullDescription);
    }

    [Fact]
    public void Create_ReplacesLegacyBlockImageWithHintedBlockConcept()
    {
        const string label =
            "[img=16x16]res://images/ui/combat/block.png[/img] "
            + "avg block gained per combat";

        var presentation = RelicStatRowVocabulary.Create(label);

        Assert.Equal("gained", presentation.Label);
        Assert.Equal(
            ["average", "block", "combat"],
            presentation.ConceptIds);
        Assert.DoesNotContain("block.png", presentation.Label);
    }

    [Fact]
    public void Create_PromotesNativeIconsIntoHintedConcepts()
    {
        const string energyIcon =
            "[img=16x16]res://images/atlases/potion_atlas.sprites/energy_potion.tres[/img]";

        var presentation = RelicStatRowVocabulary.Create(
            $"{energyIcon} Avg energy gained per combat");

        Assert.DoesNotContain(energyIcon, presentation.Label);
        Assert.Equal("gained", presentation.Label);
        Assert.Equal(
            ["average", "energy", "combat"],
            presentation.ConceptIds);
        Assert.Contains("energy", presentation.FullDescription);
    }
}
