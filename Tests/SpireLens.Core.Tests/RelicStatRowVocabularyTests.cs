using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

public class RelicStatRowVocabularyTests
{
    [Theory]
    [InlineData(
        "avg block retained per turn",
        "retained",
        "average,block,turn",
        "turn")]
    [InlineData(
        "Avg cards added per floor",
        "added",
        "average,card,floor",
        "floor")]
    [InlineData(
        "Avg activations per combat",
        "",
        "average,activation,combat",
        "combat")]
    [InlineData(
        "Turns ended at 1 charge",
        "ended at 1",
        "turn,charge",
        "")]
    [InlineData(
        "Cards upgraded",
        "",
        "upgraded",
        "")]
    [InlineData(
        "Attacks upgraded",
        "",
        "attack,upgraded",
        "")]
    [InlineData(
        "Skills upgraded",
        "",
        "skill,upgraded",
        "")]
    [InlineData(
        "Cards drawn total",
        "total",
        "draw",
        "")]
    [InlineData(
        "Avg cards drawn per combat",
        "",
        "average,draw,combat",
        "combat")]
    [InlineData(
        "Rare cards drawn",
        "",
        "card_rare,draw",
        "")]
    [InlineData(
        "Triggered this combat",
        "",
        "activation,in,combat",
        "")]
    [InlineData(
        "HP healed",
        "",
        "healing_gained",
        "")]
    [InlineData(
        "healing lost",
        "",
        "healing_blocked",
        "")]
    [InlineData(
        "healing wasted",
        "",
        "healing_wasted",
        "")]
    [InlineData(
        "1st turns ended with excess energy",
        "1st ended with",
        "turn,energy_wasted",
        "")]
    [InlineData(
        "avg excess block over 10 per turn",
        "over 10",
        "average,block_wasted,turn",
        "turn")]
    [InlineData(
        "Max HP gained",
        "",
        "max_hp_gained",
        "")]
    [InlineData(
        "Non-upgraded attacks in combat",
        "Non-upgraded",
        "attack,combat",
        "")]
    [InlineData(
        "Commons picked",
        "picked",
        "card",
        "")]
    [InlineData(
        "Uncommons picked",
        "picked",
        "card_uncommon",
        "")]
    [InlineData(
        "Rares picked",
        "picked",
        "card_rare",
        "")]
    [InlineData(
        "Attacks copied",
        "copied",
        "attack",
        "")]
    [InlineData(
        "Powers copied",
        "copied",
        "power",
        "")]
    [InlineData(
        "Skills copied",
        "copied",
        "skill",
        "")]
    [InlineData(
        "Gold loss blocked",
        "loss blocked",
        "gold",
        "")]
    [InlineData(
        "Total damage dealt",
        "Total dealt",
        "damage",
        "")]
    [InlineData(
        "Strength gained per activation",
        "",
        "strength_gained,activation",
        "activation")]
    [InlineData(
        "Strength added",
        "",
        "strength_gained",
        "")]
    [InlineData(
        "Dexterity gained",
        "",
        "dexterity_gained",
        "")]
    [InlineData(
        "Avg damage per skill played",
        "played",
        "average,damage,skill",
        "skill")]
    [InlineData(
        "Cards exhausted per combat",
        "",
        "exhaust,combat",
        "combat")]
    [InlineData(
        "Cards discarded",
        "",
        "discard",
        "")]
    [InlineData(
        "Potions gained",
        "",
        "potion_gained",
        "")]
    [InlineData(
        "Common potions",
        "",
        "potion_common",
        "")]
    [InlineData(
        "Uncommon potions",
        "",
        "potion_uncommon",
        "")]
    [InlineData(
        "Rare potions",
        "",
        "potion_rare",
        "")]
    [InlineData(
        "Total block gained",
        "Total",
        "block_gained",
        "")]
    [InlineData(
        "Avg energy gained per combat",
        "",
        "average,energy_gained,combat",
        "combat")]
    [InlineData(
        "Gold gained",
        "",
        "gold_gained",
        "")]
    [InlineData(
        "Kills",
        "",
        "kill",
        "")]
    [InlineData(
        "Relic gained",
        "",
        "relic_gained",
        "")]
    [InlineData(
        "Vigor gained",
        "",
        "vigor_gained",
        "")]
    [InlineData(
        "Rare cards offered",
        "",
        "card_rare,offered",
        "")]
    [InlineData(
        "Uncommon cards taken",
        "",
        "card_uncommon,taken",
        "")]
    [InlineData(
        "Targets hit per activation",
        "",
        "targets_hit,activation",
        "activation")]
    [InlineData(
        "Swift cards not taken",
        "not taken",
        "swift,card",
        "")]
    [InlineData(
        "Rare Attacks offered",
        "",
        "attack_rare,offered",
        "")]
    [InlineData(
        "Rare Powers offered",
        "",
        "power_rare,offered",
        "")]
    [InlineData(
        "Common Skills offered",
        "",
        "skill_common,offered",
        "")]
    [InlineData(
        "Elites slain",
        "",
        "elite,kill",
        "")]
    [InlineData(
        "Merchants visited",
        "visited",
        "shop",
        "")]
    [InlineData(
        "Campfires not rested",
        "not rested",
        "campfire",
        "")]
    [InlineData(
        "HP lost in events",
        "",
        "damage,in,all,unknown_room",
        "")]
    [InlineData(
        "Avg floors between merchants",
        "",
        "average,floor,shop",
        "")]
    public void Create_ReplacesKnownRelicConceptWords(
        string label,
        string expectedLabel,
        string expectedConcepts,
        string expectedDenominators)
    {
        var presentation = RelicStatRowVocabulary.Create(label);

        Assert.Equal(expectedLabel, presentation.Label);
        Assert.Equal(
            expectedConcepts,
            string.Join(",", presentation.ConceptIds));
        Assert.Equal(
            expectedDenominators,
            string.Join(",", presentation.DenominatorConceptIds));
        Assert.Equal(label, presentation.FullDescription);
        Assert.DoesNotContain(
            "tracked for this relic",
            presentation.FullDescription,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_ReplacesLegacyBlockImageWithHintedBlockConcept()
    {
        const string label =
            "[img=16x16]res://images/ui/combat/block.png[/img] "
            + "avg block gained per combat";

        var presentation = RelicStatRowVocabulary.Create(label);

        Assert.Equal("", presentation.Label);
        Assert.Equal(
            ["average", "block_gained", "combat"],
            presentation.ConceptIds);
        Assert.Equal(["combat"], presentation.DenominatorConceptIds);
        Assert.DoesNotContain("block.png", presentation.Label);
    }

    [Fact]
    public void Create_PromotesNativeIconsIntoHintedConcepts()
    {
        const string energyIcon =
            "[img=16x16]res://images/packed/sprite_fonts/ironclad_energy_icon.png[/img]";

        var presentation = RelicStatRowVocabulary.Create(
            $"{energyIcon} Avg energy gained per combat");

        Assert.DoesNotContain(energyIcon, presentation.Label);
        Assert.Equal("", presentation.Label);
        Assert.Equal(
            ["average", "energy_gained", "combat"],
            presentation.ConceptIds);
        Assert.Equal(["combat"], presentation.DenominatorConceptIds);
        Assert.Contains("energy", presentation.FullDescription);
    }

    [Fact]
    public void SharedRowPresentation_DeduplicatesExplicitIconsAndLabelProse()
    {
        var presentation = StatsTooltip.CreateStatRowPresentation(
            "Potions offered in all combats",
            "Potions offered in combat rewards.",
            ["potion", "offered", "in", "all", "combat"],
            []);

        Assert.Equal("", presentation.Label);
        Assert.Equal(
            ["potion", "offered", "in", "all", "combat"],
            presentation.ConceptIds);
        Assert.Empty(presentation.DenominatorConceptIds);
    }
}
