using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Models.Cards;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

/// <summary>
/// The curse/status split under "Exhausted others" is a subset breakdown: the
/// total always renders when non-zero, and each subset row appears only once
/// that kind of card has actually been exhausted.
/// </summary>
public class ExhaustedOtherCardTypesTooltipTests
{
    private static Pounce MakeCard() =>
        (Pounce)RuntimeHelpers.GetUninitializedObject(typeof(Pounce));

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void CardTooltip_ShowsCurseAndStatusSubsetsUnderExhaustedOthers()
    {
        var full = CardHoverShowPatch.BuildHistoricalBodyBBCode(
            MakeCard(),
            new CardAggregate
            {
                TimesExhaustedOtherCards = 7,
                TimesExhaustedOtherCurses = 2,
                TimesExhaustedOtherStatusCards = 3,
            },
            new RunMetaStats());

        Assert.Contains("Exhausted others", full);
        Assert.Contains("of which curses", full);
        Assert.Contains("of which status cards", full);
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void CardTooltip_OmitsSubsetRowsThatNeverHappened()
    {
        var full = CardHoverShowPatch.BuildHistoricalBodyBBCode(
            MakeCard(),
            new CardAggregate
            {
                TimesExhaustedOtherCards = 4,
                TimesExhaustedOtherCurses = 0,
                TimesExhaustedOtherStatusCards = 1,
            },
            new RunMetaStats());

        Assert.Contains("Exhausted others", full);
        Assert.DoesNotContain("of which curses", full);
        Assert.Contains("of which status cards", full);
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void CardTooltip_OmitsTheWholeBlockWhenNothingWasExhausted()
    {
        var full = CardHoverShowPatch.BuildHistoricalBodyBBCode(
            MakeCard(),
            new CardAggregate(),
            new RunMetaStats());

        Assert.DoesNotContain("Exhausted others", full);
        Assert.DoesNotContain("of which curses", full);
        Assert.DoesNotContain("of which status cards", full);
    }
}
