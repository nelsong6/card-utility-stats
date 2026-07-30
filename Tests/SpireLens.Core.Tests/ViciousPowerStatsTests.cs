using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class ViciousPowerStatsTests
{
    private const string ViciousPowerId = "POWER.VICIOUS";

    private static readonly MethodInfo AppendViciousPowerStatsMethod =
        typeof(CardHoverShowPatch).GetMethod(
            "AppendViciousPowerStats",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("AppendViciousPowerStats not found.");

    [Fact]
    public void ViciousTrigger_RequiresPositiveOwnerAppliedVulnerable()
    {
        Assert.True(RunTracker.ViciousTriggerQualifiesForTest(
            amount: 2m,
            applierIsOwner: true,
            changedPowerIsVulnerable: true));
        Assert.False(RunTracker.ViciousTriggerQualifiesForTest(
            amount: 0m,
            applierIsOwner: true,
            changedPowerIsVulnerable: true));
        Assert.False(RunTracker.ViciousTriggerQualifiesForTest(
            amount: 2m,
            applierIsOwner: false,
            changedPowerIsVulnerable: true));
        Assert.False(RunTracker.ViciousTriggerQualifiesForTest(
            amount: 2m,
            applierIsOwner: true,
            changedPowerIsVulnerable: false));
    }

    [Fact]
    public void PowerAggregate_ViciousCardsDrawn_DefaultsAndSerializes()
    {
        var empty = new PowerAggregate();
        Assert.Equal(0, empty.ViciousCardsDrawn);

        var run = new RunData();
        run.MetaStats.PowerAggregates[ViciousPowerId] = CreateAggregate(11);

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"vicious_cards_drawn\"", json);
        Assert.NotNull(restored);
        Assert.Equal(
            11,
            restored!.MetaStats.PowerAggregates[ViciousPowerId].ViciousCardsDrawn);
    }

    [Fact]
    public void RunTracker_ViciousCardsDrawn_RecordsOnlyPositiveObservedCounts()
    {
        var agg = new PowerAggregate();

        RunTracker.RecordViciousCardsDrawnForTest(agg, 5);
        RunTracker.RecordViciousCardsDrawnForTest(agg, 6);
        RunTracker.RecordViciousCardsDrawnForTest(agg, 0);
        RunTracker.RecordViciousCardsDrawnForTest(agg, -1);

        Assert.Equal(11, agg.ViciousCardsDrawn);
    }

    [Fact]
    public void Promotion_MergesViciousPowerTotals()
    {
        var run = new RunData();
        run.MetaStats.PowerAggregates[ViciousPowerId] = CreateAggregate(5);
        var pending = new PendingCombat();
        pending.MetaStats.PowerAggregates[ViciousPowerId] = CreateAggregate(6);

        RunTracker.PromotePendingCombatIntoRun(pending, run);

        Assert.Equal(
            11,
            run.MetaStats.PowerAggregates[ViciousPowerId].ViciousCardsDrawn);
    }

    [Fact]
    public void ViciousTooltip_ProjectsSharedPowerCardsDrawn()
    {
        var sb = new StringBuilder();
        var card = (Vicious)RuntimeHelpers.GetUninitializedObject(typeof(Vicious));
        var metaStats = new RunMetaStats();
        metaStats.PowerAggregates[ViciousPowerId] = CreateAggregate(11);

        _ = AppendViciousPowerStatsMethod.Invoke(
            null,
            new object?[] { sb, card, metaStats });

        Assert.Contains("cards drawn", sb.ToString());
        Assert.Contains("[b]11[/b]", sb.ToString());
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void Patch_TargetsViciousPowerCallbackWithExpectedParameters()
    {
        var target = typeof(ViciousPower).GetMethod(
            nameof(ViciousPower.AfterPowerAmountChanged),
            new[]
            {
                typeof(PlayerChoiceContext),
                typeof(PowerModel),
                typeof(decimal),
                typeof(Creature),
                typeof(CardModel),
            });

        Assert.NotNull(target);
    }

    private static PowerAggregate CreateAggregate(int cardsDrawn) =>
        new()
        {
            PowerId = ViciousPowerId,
            DisplayName = "Vicious",
            ViciousCardsDrawn = cardsDrawn,
        };
}
