using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class WongosMysteryTicketStatsTests
{
    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildWongosMysteryTicketBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "BuildWongosMysteryTicketBodyBBCode not found.");

    [Fact]
    public void Patches_TargetTicketRewardInjectionAndRelicClaim()
    {
        var rewardModifier = typeof(WongosMysteryTicket).GetMethod(
            nameof(WongosMysteryTicket.TryModifyRewards),
            [typeof(Player), typeof(List<Reward>), typeof(AbstractRoom)]);
        var rewardClaim = typeof(RelicReward).GetMethod(
            "OnSelect",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(rewardModifier);
        Assert.Equal(typeof(bool), rewardModifier!.ReturnType);
        Assert.NotNull(rewardClaim);
        Assert.Equal(typeof(Task<bool>), rewardClaim!.ReturnType);
    }

    [Fact]
    public void RunTracker_TicketActivation_PreservesFirstObservedFloors()
    {
        var aggregate = new RelicAggregate();

        RunTracker.RecordWongosMysteryTicketActivationForTest(
            aggregate,
            pickupFloor: 8,
            activationFloor: 15);
        RunTracker.RecordWongosMysteryTicketActivationForTest(
            aggregate,
            pickupFloor: 9,
            activationFloor: 16);

        Assert.Equal(8, aggregate.FloorAcquired);
        Assert.Equal(15, aggregate.FloorActivated);
    }

    [Fact]
    public void RunTracker_TicketRelicReceived_TracksExactRelics()
    {
        var aggregate = new RelicAggregate();

        RunTracker.RecordWongosMysteryTicketRelicReceivedForTest(
            aggregate,
            "RELIC.KUNAI",
            "Kunai");
        RunTracker.RecordWongosMysteryTicketRelicReceivedForTest(
            aggregate,
            "RELIC.KUNAI",
            "Kunai");
        RunTracker.RecordWongosMysteryTicketRelicReceivedForTest(
            aggregate,
            "RELIC.BAG_OF_PREPARATION",
            "Bag of Preparation");

        Assert.Equal(2, aggregate.RelicsGranted.Count);
        Assert.Equal(2, aggregate.RelicsGranted["RELIC.KUNAI"].Count);
        Assert.Equal(
            1,
            aggregate.RelicsGranted["RELIC.BAG_OF_PREPARATION"].Count);
    }

    [Fact]
    public void RelicTooltip_Ticket_ShowsFloorDistanceAndReceivedRelics()
    {
        var aggregate = new RelicAggregate
        {
            FloorAcquired = 8,
            FloorActivated = 15,
            RelicsGranted =
            {
                ["RELIC.KUNAI"] = new RelicGrantedAggregate
                {
                    RelicId = "RELIC.KUNAI",
                    DisplayName = "Kunai",
                    Count = 2,
                },
                ["RELIC.BAG_OF_PREPARATION"] = new RelicGrantedAggregate
                {
                    RelicId = "RELIC.BAG_OF_PREPARATION",
                    DisplayName = "Bag of Preparation",
                    Count = 1,
                },
            },
        };

        var body = BuildBody(aggregate);

        Assert.Contains("Floors ascended before activating", body);
        Assert.Contains("[b]7[/b]", body);
        Assert.Contains("Relics received", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("Kunai x2", body);
        Assert.Contains("Bag of Preparation", body);
    }

    [Fact]
    public void RelicTooltip_Ticket_ShowsNotYetBeforeActivation()
    {
        var body = BuildBody(new RelicAggregate
        {
            FloorAcquired = 8,
        });

        Assert.Contains("[b]not yet[/b]", body);
        Assert.Contains("[b]0[/b]", body);
    }

    [Fact]
    public void RelicTooltip_Ticket_DispatchesForModel()
    {
        var relic = (WongosMysteryTicket)
            RuntimeHelpers.GetUninitializedObject(
                typeof(WongosMysteryTicket));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate(),
            floorCount: null,
            out var title,
            out _);

        Assert.True(recognized);
        Assert.Equal("Wongo's Mystery Ticket", title);
    }

    private static string BuildBody(RelicAggregate aggregate)
        => (string)(BuildBodyMethod.Invoke(null, [aggregate])
            ?? throw new InvalidOperationException(
                "BuildWongosMysteryTicketBodyBBCode returned null."));
}
