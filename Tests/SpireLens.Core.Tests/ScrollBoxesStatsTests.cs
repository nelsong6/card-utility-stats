using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class ScrollBoxesStatsTests
{
    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildScrollBoxesBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildScrollBoxesBodyBBCode not found.");

    [Fact]
    public void HarmonyTarget_AfterObtained_ReturnsTask()
    {
        var method = typeof(ScrollBoxes).GetMethod(
            nameof(ScrollBoxes.AfterObtained),
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            Type.EmptyTypes,
            modifiers: null);

        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);
    }

    [Fact]
    public void TrackingMath_RecordsOneObservedBundleAndDuplicateCards()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordScrollBoxesBundleForTest(
            agg,
            [
                ("CARD.CLAW", "Claw"),
                ("CARD.CLAW", "Claw"),
                ("CARD.CLAW", "Claw"),
            ]);
        RunTracker.RecordScrollBoxesBundleForTest(
            agg,
            [("", "Missing id")]);

        Assert.Equal(1, agg.Activations);
        Assert.Single(agg.CardsGranted);
        Assert.Equal(3, agg.CardsGranted["CARD.CLAW"].Count);
        Assert.Equal("Claw", agg.CardsGranted["CARD.CLAW"].DisplayName);
    }

    [Fact]
    public void Tooltip_ShowsBundleTotalAndReceivedCards()
    {
        var agg = new RelicAggregate
        {
            Activations = 1,
            CardsGranted =
            {
                ["CARD.STRIKE"] = new RelicCardAggregate
                {
                    CardId = "CARD.STRIKE",
                    DisplayName = "Strike",
                    Count = 2,
                },
                ["CARD.SHRUG_IT_OFF"] = new RelicCardAggregate
                {
                    CardId = "CARD.SHRUG_IT_OFF",
                    DisplayName = "Shrug It Off",
                    Count = 1,
                },
            },
        };

        var body = BuildBody(agg);

        Assert.Contains("Bundles chosen", body);
        Assert.Contains("Cards received", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("Strike x2", body);
        Assert.Contains("Shrug It Off", body);
    }

    [Fact]
    public void TooltipDispatch_RecognizesScrollBoxes()
    {
        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            (ScrollBoxes)RuntimeHelpers.GetUninitializedObject(typeof(ScrollBoxes)),
            new RelicAggregate { Activations = 1 },
            floorCount: null,
            bloodSoakedRoseCurseAgg: null,
            cursedPearlCurseAgg: null,
            neowsBonesCurseAggs: null,
            storybookBrightestFlameAgg: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Scroll Boxes", title);
        Assert.Contains("Bundles chosen", body);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildScrollBoxesBodyBBCode returned null."));
}
