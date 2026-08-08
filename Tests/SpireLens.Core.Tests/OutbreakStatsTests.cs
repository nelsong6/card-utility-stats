using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class OutbreakStatsTests
{
    private static readonly MethodInfo AppendOutbreakStatsMethod =
        typeof(CardHoverShowPatch).GetMethod(
            "AppendOutbreakStats",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("AppendOutbreakStats not found.");

    [Fact]
    public void CardAggregate_OutbreakDamage_DefaultsAndSerializes()
    {
        var empty = new CardAggregate();
        Assert.Equal(0, empty.OutbreakExtraPoisonTriggerDamage);

        var run = new RunData();
        run.Aggregates["CARD.OUTBREAK#1"] = CreateAggregate(37);

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"outbreak_extra_poison_trigger_damage\"", json);
        Assert.NotNull(restored);
        Assert.Equal(
            37,
            restored!.Aggregates["CARD.OUTBREAK#1"]
                .OutbreakExtraPoisonTriggerDamage);
    }

    [Fact]
    public void RunTracker_OutbreakDamage_RecordsOnlyPositiveObservedDamage()
    {
        var agg = new CardAggregate();

        RunTracker.RecordOutbreakExtraPoisonTriggerDamageForTest(agg, 16);
        RunTracker.RecordOutbreakExtraPoisonTriggerDamageForTest(agg, 21);
        RunTracker.RecordOutbreakExtraPoisonTriggerDamageForTest(agg, 0);
        RunTracker.RecordOutbreakExtraPoisonTriggerDamageForTest(agg, -1);

        Assert.Equal(37, agg.OutbreakExtraPoisonTriggerDamage);
    }

    [Fact]
    public void Promotion_MergesOutbreakCardDamage()
    {
        var run = new RunData();
        run.Aggregates["CARD.OUTBREAK#1"] = CreateAggregate(16);
        var pending = new PendingCombat();
        pending.CombatAggregates["CARD.OUTBREAK#1"] = CreateAggregate(21);

        RunTracker.PromotePendingCombatIntoRun(pending, run);

        Assert.Equal(
            37,
            run.Aggregates["CARD.OUTBREAK#1"]
                .OutbreakExtraPoisonTriggerDamage);
    }

    [Fact]
    public void OutbreakTooltip_ShowsPoisonIconAndExtraTriggerDamage()
    {
        var sb = new StringBuilder();
        var card = (Outbreak)RuntimeHelpers.GetUninitializedObject(
            typeof(Outbreak));

        _ = AppendOutbreakStatsMethod.Invoke(
            null,
            new object?[] { sb, card, CreateAggregate(37) });

        var body = sb.ToString();
        Assert.Contains("poison_power.tres", body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("damage"), body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("activation"), body);
        Assert.Contains("[b]37[/b]", body);
        Assert.Contains(
            StatConceptGlossary.RenderInformationHint(
                "Damage dealt by the extra Poison triggers caused by Outbreak."),
            body);
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void Patches_TargetPoisonTriggerAndItsExactDamageOverload()
    {
        var trigger = typeof(PoisonPower).GetMethod(
            nameof(PoisonPower.Trigger),
            Type.EmptyTypes);
        var damage = AccessTools.Method(
            typeof(CreatureCmd),
            nameof(CreatureCmd.Damage),
            new[]
            {
                typeof(PlayerChoiceContext),
                typeof(Creature),
                typeof(decimal),
                typeof(ValueProp),
                typeof(CardModel),
                typeof(CardPlay),
            });

        Assert.NotNull(trigger);
        Assert.NotNull(damage);
    }

    private static CardAggregate CreateAggregate(int damage) =>
        new()
        {
            OutbreakExtraPoisonTriggerDamage = damage,
        };
}
