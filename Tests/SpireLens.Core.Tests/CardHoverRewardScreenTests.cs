using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class CardHoverRewardScreenTests
{
    [Fact]
    public void IsCardRewardSelectionSurfaceType_MatchesCardRewardSelectionScreen()
    {
        Assert.True(CardHoverShowPatch.IsCardRewardSelectionSurfaceType(typeof(NCardRewardSelectionScreen)));
    }

    [Fact]
    public void IsCardRewardSelectionSurfaceType_DoesNotMatchOrdinaryCardSurfaces()
    {
        Assert.False(CardHoverShowPatch.IsCardRewardSelectionSurfaceType(typeof(NCardHolder)));
        Assert.False(CardHoverShowPatch.IsCardRewardSelectionSurfaceType(typeof(NDeckViewScreen)));
        Assert.False(CardHoverShowPatch.IsCardRewardSelectionSurfaceType(null));
    }
}
