using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class ConsumingShadowPowerStatsTests
{
    private const string CardId = "CARD.CONSUMING_SHADOW";
    private const string PowerId = "POWER.CONSUMING_SHADOW";

    private static readonly MethodInfo AppendMetaPowerLifetimeStatsMethod =
        typeof(CardHoverShowPatch).GetMethod(
            "AppendMetaPowerLifetimeStats",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "AppendMetaPowerLifetimeStats not found.");

    [Fact]
    public void MetaPowerRegistry_MapsConsumingShadowCardToPower()
    {
        var found = MetaPowerRegistry.TryGetByCardId(CardId, out var definition);

        Assert.True(found);
        Assert.NotNull(definition);
        Assert.Equal(PowerId, definition!.PowerId);
        Assert.Equal("Consuming Shadow", definition.DisplayName);
    }

    [Fact]
    public void PowerAggregate_OrbsEvokedDefaultsAndSerializes()
    {
        Assert.Equal(0, new PowerAggregate().OrbsEvoked);

        var run = new RunData();
        run.MetaStats.PowerAggregates[PowerId] = CreateAggregate(7);

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(
            json,
            RunStorage.Options);

        Assert.Contains("\"orbs_evoked\"", json);
        Assert.NotNull(restored);
        Assert.Equal(
            7,
            restored!.MetaStats.PowerAggregates[PowerId].OrbsEvoked);
    }

    [Fact]
    public void TrackingHelper_RecordsOnlyPositiveCompletedEvokes()
    {
        var aggregate = new PowerAggregate();

        RunTracker.RecordConsumingShadowOrbEvokedForTest(aggregate, 7);
        RunTracker.RecordConsumingShadowOrbEvokedForTest(aggregate, 0);
        RunTracker.RecordConsumingShadowOrbEvokedForTest(aggregate, -2);

        Assert.Equal(7, aggregate.OrbsEvoked);
    }

    [Fact]
    public void Promotion_MergesConsumingShadowEvokes()
    {
        var run = new RunData();
        run.MetaStats.PowerAggregates[PowerId] = CreateAggregate(3);
        var pending = new PendingCombat();
        pending.MetaStats.PowerAggregates[PowerId] = CreateAggregate(4);

        RunTracker.PromotePendingCombatIntoRun(pending, run);

        Assert.Equal(7, run.MetaStats.PowerAggregates[PowerId].OrbsEvoked);
    }

    [Fact]
    public void Tooltip_ShowsSharedOrbsEvokedTotal()
    {
        var definition = MetaPowerRegistry.All.Single(candidate =>
            candidate.PowerId == PowerId);
        var aggregate = CreateAggregate(7);
        var metaStats = new RunMetaStats();
        metaStats.PowerAggregates[PowerId] = aggregate;
        var sb = new StringBuilder();

        _ = AppendMetaPowerLifetimeStatsMethod.Invoke(
            null,
            [sb, definition, aggregate, metaStats, false]);

        var body = sb.ToString();
        Assert.Contains("Orbs evoked", body);
        Assert.Contains("[b]7[/b]", body);
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void Attribution_UsesExactNativeCallbacks()
    {
        var callback = AccessTools.Method(
            typeof(ConsumingShadowPower),
            nameof(ConsumingShadowPower.AfterSideTurnEnd),
            [
                typeof(PlayerChoiceContext),
                typeof(CombatSide),
                typeof(IEnumerable<Creature>),
            ]);
        var evokeLast = AccessTools.Method(
            typeof(OrbCmd),
            nameof(OrbCmd.EvokeLast),
            [
                typeof(PlayerChoiceContext),
                typeof(Player),
                typeof(bool),
            ]);
        var afterOrbEvoked = AccessTools.Method(
            typeof(Hook),
            nameof(Hook.AfterOrbEvoked),
            [
                typeof(PlayerChoiceContext),
                typeof(ICombatState),
                typeof(OrbModel),
                typeof(IEnumerable<Creature>),
            ]);

        Assert.NotNull(callback);
        Assert.NotNull(evokeLast);
        Assert.NotNull(afterOrbEvoked);
        Assert.Equal("participants", callback!.GetParameters()[2].Name);
        Assert.Equal("dequeue", evokeLast!.GetParameters()[2].Name);
        Assert.Equal("orb", afterOrbEvoked!.GetParameters()[2].Name);
    }

    private static PowerAggregate CreateAggregate(int orbsEvoked)
        => new()
        {
            PowerId = PowerId,
            DisplayName = "Consuming Shadow",
            OrbsEvoked = orbsEvoked,
        };
}
