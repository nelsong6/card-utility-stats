using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class FakeLeesWaffleStatsTests
{
    [Fact]
    public void Patch_TargetsFakeLeesWaffleAfterObtained()
    {
        var target = typeof(FakeLeesWaffle).GetMethod(nameof(FakeLeesWaffle.AfterObtained));

        Assert.NotNull(target);
        Assert.Empty(target!.GetParameters());
    }

    [Fact]
    public void Tooltip_ShowsUsualObservedHealingStatsWithObscuredTitle()
    {
        var relic =
            (FakeLeesWaffle)RuntimeHelpers.GetUninitializedObject(typeof(FakeLeesWaffle));
        var aggregate = new RelicAggregate
        {
            Activations = 1,
            TotalHealingAttempted = 7m,
            TotalHealingRestored = 5m,
            TotalHealingLost = 2m,
        };

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            aggregate,
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Lee's Waffle???", title);
        Assert.Contains("Activations", body);
        Assert.Contains("HP healed", body);
        Assert.Contains("healing lost", body);
        Assert.Contains("[b]1[/b]", body);
        Assert.Contains("[b]5[/b]", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.DoesNotContain("Max HP gained", body);
    }
}
