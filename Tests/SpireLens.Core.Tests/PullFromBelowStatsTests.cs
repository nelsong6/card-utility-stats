using System.Reflection;
using System.Text;
using MegaCrit.Sts2.Core.Models.Cards;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class PullFromBelowStatsTests
{
    private static readonly MethodInfo AppendPullFromBelowStatsMethod =
        typeof(CardHoverShowPatch).GetMethod("AppendPullFromBelowStats", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("AppendPullFromBelowStats not found.");

    [Trait("Category", "RequiresLiveGame")]
    [Fact]
    public void Tooltip_PullFromBelow_ShowsEtherealCardsPlayedThisCombatAtZero()
    {
        var body = BuildBody(new PullFromBelow(), 0);

        Assert.Contains("Ethereal cards played this combat", body);
        Assert.Contains("[b]0[/b]", body);
    }

    [Trait("Category", "RequiresLiveGame")]
    [Fact]
    public void Tooltip_PullFromBelow_ShowsEtherealCardsPlayedThisCombatCount()
    {
        var body = BuildBody(new PullFromBelow(), 3);

        Assert.Contains("Ethereal cards played this combat", body);
        Assert.Contains("[b]3[/b]", body);
    }

    [Trait("Category", "RequiresLiveGame")]
    [Fact]
    public void Tooltip_PullFromBelow_DoesNotShowOnOtherCards()
    {
        var body = BuildBody(new Unleash(), 3);

        Assert.DoesNotContain("Ethereal cards played this combat", body);
    }

    private static string BuildBody(object cardModel, int etherealCardsPlayedThisCombat)
    {
        var sb = new StringBuilder();
        _ = AppendPullFromBelowStatsMethod.Invoke(null, new object?[]
        {
            sb,
            cardModel,
            etherealCardsPlayedThisCombat,
        });
        return sb.ToString();
    }
}
