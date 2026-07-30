using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class AmethystAubergineStatsTests
{
    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildAmethystAubergineBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "BuildAmethystAubergineBodyBBCode not found.");

    [Fact]
    public void Patch_TargetsAmethystAubergineRewardModifier()
    {
        var target = typeof(AmethystAubergine).GetMethod(
            nameof(AmethystAubergine.TryModifyRewards),
            [typeof(Player), typeof(List<Reward>), typeof(AbstractRoom)]);

        Assert.NotNull(target);
        Assert.Equal(typeof(bool), target!.ReturnType);
    }

    [Fact]
    public void RunTracker_AmethystAubergineHelper_TracksTriggersAndObservedGold()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordAmethystAubergineTriggerForTest(agg, 15);
        RunTracker.RecordAmethystAubergineTriggerForTest(agg, 20);
        RunTracker.RecordAmethystAubergineTriggerForTest(agg, -5);

        Assert.Equal(3, agg.Activations);
        Assert.Equal(35, agg.GoldGained);
    }

    [Fact]
    public void RelicTooltip_AmethystAubergine_ShowsRequestedRows()
    {
        var body = BuildBody(new RelicAggregate
        {
            Activations = 3,
            GoldGained = 45,
        });

        Assert.Contains("successful Amethyst Aubergine reward additions", body);
        Assert.Contains("Extra gold received", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[b]45[/b]", body);
    }

    [Fact]
    public void RelicTooltip_AmethystAubergine_DispatchesForModel()
    {
        var relic = (AmethystAubergine)RuntimeHelpers.GetUninitializedObject(
            typeof(AmethystAubergine));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate(),
            floorCount: null,
            out var title,
            out _);

        Assert.True(recognized);
        Assert.Equal("Amethyst Aubergine", title);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException(
                "BuildAmethystAubergineBodyBBCode returned null."));
}
