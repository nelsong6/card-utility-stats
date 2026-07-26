using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class FakeBloodVialStatsTests
{
    [Fact]
    public void Tooltip_MatchesBloodVialStatsWithObscuredTitle()
    {
        var aggregate = new RelicAggregate
        {
            Activations = 3,
            TotalHealingAttempted = 3,
            TotalHealingRestored = 2,
            TotalHealingLost = 1,
        };
        var bloodVial = (BloodVial)RuntimeHelpers.GetUninitializedObject(typeof(BloodVial));
        var fakeBloodVial = (FakeBloodVial)RuntimeHelpers.GetUninitializedObject(typeof(FakeBloodVial));

        var bloodVialRecognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            bloodVial,
            aggregate,
            floorCount: null,
            out var bloodVialTitle,
            out var bloodVialBody);
        var fakeBloodVialRecognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            fakeBloodVial,
            aggregate,
            floorCount: null,
            out var fakeBloodVialTitle,
            out var fakeBloodVialBody);

        Assert.True(bloodVialRecognized);
        Assert.True(fakeBloodVialRecognized);
        Assert.Equal("Blood Vial", bloodVialTitle);
        Assert.Equal("Blood Vial???", fakeBloodVialTitle);
        Assert.Equal(bloodVialBody, fakeBloodVialBody);
    }
}
