using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class TinyMailboxStatsTests
{
    private const string TinyMailboxRelicId = "RELIC.TINY_MAILBOX";

    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildTinyMailboxBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "BuildTinyMailboxBodyBBCode not found.");

    [Fact]
    public void Patch_TargetsTinyMailboxRestHealRewardCallback()
    {
        var target = typeof(TinyMailbox).GetMethod(
            nameof(TinyMailbox.TryModifyRestSiteHealRewards));

        Assert.NotNull(target);
        Assert.Equal(
            new[]
            {
                typeof(Player),
                typeof(System.Collections.Generic.List<Reward>),
                typeof(bool),
            },
            target!.GetParameters()
                .Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void RelicAggregate_TinyMailboxFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.TinyMailboxPotionsOffered);
        Assert.Equal(0, agg.TinyMailboxPotionsTaken);
        Assert.Equal(0, agg.TinyMailboxCommonPotionsOffered);
        Assert.Equal(0, agg.TinyMailboxUncommonPotionsOffered);
        Assert.Equal(0, agg.TinyMailboxRarePotionsOffered);
        Assert.Equal(0, agg.TinyMailboxFruitJuicesOffered);
        Assert.Equal(0, agg.TinyMailboxCampfiresNotRested);
    }

    [Fact]
    public void RelicAggregate_TinyMailboxFields_JsonRoundtripPreservesValues()
    {
        var run = new RunData();
        run.RelicAggregates[TinyMailboxRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(
            json,
            RunStorage.Options);

        Assert.Contains("\"tiny_mailbox_potions_offered\"", json);
        Assert.Contains("\"tiny_mailbox_potions_taken\"", json);
        Assert.Contains("\"tiny_mailbox_fruit_juices_offered\"", json);
        Assert.Contains("\"tiny_mailbox_campfires_not_rested\"", json);
        Assert.NotNull(restored);
        AssertAggregate(restored!.RelicAggregates[TinyMailboxRelicId]);
    }

    [Fact]
    public void RunTracker_TinyMailboxHelpers_CountRarityAndFruitJuiceOverlap()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordTinyMailboxActivationForTest(agg, 2);
        RunTracker.RecordTinyMailboxPotionOfferedForTest(
            agg,
            PotionRarity.Common,
            isFruitJuice: false);
        RunTracker.RecordTinyMailboxPotionOfferedForTest(
            agg,
            PotionRarity.Uncommon,
            isFruitJuice: false);
        RunTracker.RecordTinyMailboxPotionOfferedForTest(
            agg,
            PotionRarity.Rare,
            isFruitJuice: false);
        RunTracker.RecordTinyMailboxPotionOfferedForTest(
            agg,
            PotionRarity.Rare,
            isFruitJuice: true);
        RunTracker.RecordTinyMailboxPotionTakenForTest(agg, 3);
        RunTracker.RecordTinyMailboxCampfireNotRestedForTest(agg, 2);

        AssertAggregate(agg);
    }

    [Fact]
    public void RelicAggregate_TinyMailboxFields_Merge()
    {
        var target = PopulatedAggregate();

        RunTracker.MergeRelicAggregateInto(target, PopulatedAggregate());

        Assert.Equal(4, target.Activations);
        Assert.Equal(8, target.TinyMailboxPotionsOffered);
        Assert.Equal(6, target.TinyMailboxPotionsTaken);
        Assert.Equal(2, target.TinyMailboxCommonPotionsOffered);
        Assert.Equal(2, target.TinyMailboxUncommonPotionsOffered);
        Assert.Equal(4, target.TinyMailboxRarePotionsOffered);
        Assert.Equal(2, target.TinyMailboxFruitJuicesOffered);
        Assert.Equal(4, target.TinyMailboxCampfiresNotRested);
    }

    [Fact]
    public void RelicTooltip_TinyMailbox_ShowsRestAndPotionRows()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("Rest-site heals where Tiny Mailbox", body);
        Assert.Contains(
            StatConceptGlossary.RenderHintedGlyph("offered"),
            body);
        Assert.Contains(
            StatConceptGlossary.RenderHintedGlyph("taken"),
            body);
        Assert.Contains(
            StatConceptGlossary.RenderHintedGlyph("potion_common"),
            body);
        Assert.Contains(
            StatConceptGlossary.RenderHintedGlyph("potion_uncommon"),
            body);
        Assert.Contains(
            StatConceptGlossary.RenderHintedGlyph("potion_rare"),
            body);
        Assert.DoesNotContain("Common potions offered", body);
        Assert.DoesNotContain("Uncommon potions offered", body);
        Assert.DoesNotContain("Rare potions offered", body);
        Assert.Contains(
            StatConceptGlossary.RenderHintedGlyph("fruit_juice"),
            body);
        Assert.DoesNotContain(
            StatConceptGlossary.RenderHintedGlyph("potion_gained"),
            body);
        Assert.DoesNotContain("Fruit Juices offered", body);
        Assert.Contains("Potions offered/taken", body);
        Assert.Contains("[b]4/3[/b]", body);
        Assert.Contains("not rested", body);
    }

    private static RelicAggregate PopulatedAggregate()
        => new()
        {
            Activations = 2,
            TinyMailboxPotionsOffered = 4,
            TinyMailboxPotionsTaken = 3,
            TinyMailboxCommonPotionsOffered = 1,
            TinyMailboxUncommonPotionsOffered = 1,
            TinyMailboxRarePotionsOffered = 2,
            TinyMailboxFruitJuicesOffered = 1,
            TinyMailboxCampfiresNotRested = 2,
        };

    private static void AssertAggregate(RelicAggregate agg)
    {
        Assert.Equal(2, agg.Activations);
        Assert.Equal(4, agg.TinyMailboxPotionsOffered);
        Assert.Equal(3, agg.TinyMailboxPotionsTaken);
        Assert.Equal(1, agg.TinyMailboxCommonPotionsOffered);
        Assert.Equal(1, agg.TinyMailboxUncommonPotionsOffered);
        Assert.Equal(2, agg.TinyMailboxRarePotionsOffered);
        Assert.Equal(1, agg.TinyMailboxFruitJuicesOffered);
        Assert.Equal(2, agg.TinyMailboxCampfiresNotRested);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildBodyMethod.Invoke(null, new object[] { agg })
                    ?? throw new InvalidOperationException(
                        "Builder returned null."));
}
