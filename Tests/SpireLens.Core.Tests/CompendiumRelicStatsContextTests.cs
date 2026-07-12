using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class CompendiumRelicStatsContextTests
{
    [Fact]
    public void ShouldShowStatsForVisibility_OnlyShowsVisibleRelics()
    {
        Assert.True(CompendiumRelicStatsContext.ShouldShowStatsForVisibility(ModelVisibility.Visible));
        Assert.False(CompendiumRelicStatsContext.ShouldShowStatsForVisibility(ModelVisibility.None));
        Assert.False(CompendiumRelicStatsContext.ShouldShowStatsForVisibility(ModelVisibility.NotSeen));
        Assert.False(CompendiumRelicStatsContext.ShouldShowStatsForVisibility(ModelVisibility.Locked));
    }

    [Fact]
    public void TryBuildRelicTooltipForRun_UsesSavedRunAggregate()
    {
        var run = new RunData { FloorReached = 5 };
        run.RelicAggregates["RELIC.ANCHOR"] = new RelicAggregate
        {
            Activations = 1,
            AdditionalBlockGained = 10,
        };

        var ok = CompendiumRelicStatsContext.TryBuildRelicTooltipForRun(
            Uninitialized<FakeAnchor>(),
            run,
            out var title,
            out var body);

        Assert.True(ok);
        Assert.Equal("???", title);
        Assert.Contains("Activations", body);
        Assert.Contains("[b]1[/b]", body);
        Assert.Contains("block gained", body);
        Assert.Contains("[b]10[/b]", body);
    }

    private static T Uninitialized<T>() where T : class
    {
        return (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    }
}
