using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class LeadPaperweightStatsTests
{
    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildLeadPaperweightBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildLeadPaperweightBodyBBCode not found.");

    [Fact]
    public void HarmonyTarget_AfterObtained_ReturnsTask()
    {
        var method = typeof(LeadPaperweight).GetMethod(
            nameof(LeadPaperweight.AfterObtained),
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            Type.EmptyTypes,
            modifiers: null);

        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);
    }

    [Fact]
    public void TrackingMath_RecordsOffersActualDeckCardAndFloor()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordLeadPaperweightChoiceForTest(
            agg,
            floor: 12,
            options:
            [
                new RelicCardRewardOptionAggregate
                {
                    CardId = "CARD.PANACHE",
                    DisplayName = "Panache",
                    Taken = true,
                },
                new RelicCardRewardOptionAggregate
                {
                    CardId = "CARD.MASTER_OF_STRATEGY",
                    DisplayName = "Master of Strategy",
                    Taken = false,
                },
            ],
            receivedCardId: "CARD.PANACHE",
            receivedDisplayName: "Panache",
            skipped: false);

        Assert.Equal(12, agg.FloorAcquired);
        var screen = Assert.Single(agg.CardRewardScreens);
        Assert.True(screen.Resolved);
        Assert.Equal(12, screen.Floor);
        Assert.Equal(2, screen.Cards.Count);
        Assert.True(screen.Cards[0].Taken);
        Assert.False(screen.Cards[1].Taken);
        Assert.Equal(1, agg.CardsGranted["CARD.PANACHE"].Count);
        Assert.Equal(0, agg.CardChoicesSkipped);
    }

    [Fact]
    public void TrackingMath_RecordsTrueSkipWithoutInventingAGrantedCard()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordLeadPaperweightChoiceForTest(
            agg,
            floor: 6,
            options:
            [
                new RelicCardRewardOptionAggregate
                {
                    CardId = "CARD.PANACHE",
                    DisplayName = "Panache",
                },
                new RelicCardRewardOptionAggregate
                {
                    CardId = "CARD.MASTER_OF_STRATEGY",
                    DisplayName = "Master of Strategy",
                },
            ],
            receivedCardId: null,
            receivedDisplayName: null,
            skipped: true);

        Assert.Equal(1, agg.CardChoicesSkipped);
        Assert.Empty(agg.CardsGranted);
        Assert.All(Assert.Single(agg.CardRewardScreens).Cards, card => Assert.False(card.Taken));
    }

    [Fact]
    public void Tooltip_ShowsFloorAndBothChoiceOutcomes()
    {
        var agg = new RelicAggregate
        {
            FloorAcquired = 12,
            CardRewardScreens =
            {
                new RelicCardRewardScreenAggregate
                {
                    ScreenNumber = 1,
                    Floor = 12,
                    Resolved = true,
                    Cards =
                    {
                        new RelicCardRewardOptionAggregate
                        {
                            CardId = "CARD.PANACHE",
                            DisplayName = "Panache",
                            Taken = true,
                        },
                        new RelicCardRewardOptionAggregate
                        {
                            CardId = "CARD.MASTER_OF_STRATEGY",
                            DisplayName = "Master of Strategy",
                            Taken = false,
                        },
                    },
                },
            },
        };

        var body = BuildBody(agg);

        Assert.Contains("Floor acquired", body);
        Assert.Contains("[b]12[/b]", body);
        Assert.Contains("Panache", body);
        Assert.Contains("Master of Strategy", body);
        var takenIcon = StatConceptGlossary.RenderHintedGlyph("taken");
        Assert.Contains($"[b]{takenIcon}[/b]", body);
        Assert.Contains($"[b]not {takenIcon}[/b]", body);
        Assert.DoesNotContain("[b]taken[/b]", body);
        Assert.DoesNotContain("[b]not taken[/b]", body);
    }

    [Fact]
    public void TooltipDispatch_RecognizesLeadPaperweight()
    {
        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            (LeadPaperweight)RuntimeHelpers.GetUninitializedObject(typeof(LeadPaperweight)),
            new RelicAggregate(),
            floorCount: null,
            bloodSoakedRoseCurseAgg: null,
            cursedPearlCurseAgg: null,
            neowsBonesCurseAggs: null,
            storybookBrightestFlameAgg: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Lead Paperweight", title);
        Assert.Contains("Card choice", body);
    }

    private static string BuildBody(RelicAggregate agg, int? fallbackFloor = null)
        => (string)(BuildBodyMethod.Invoke(null, new object?[] { agg, fallbackFloor })
            ?? throw new InvalidOperationException("BuildLeadPaperweightBodyBBCode returned null."));
}
