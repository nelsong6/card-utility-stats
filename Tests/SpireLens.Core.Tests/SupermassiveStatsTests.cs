using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using MegaCrit.Sts2.Core.Models.Cards;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class SupermassiveStatsTests
{
    private static readonly MethodInfo AppendSupermassiveStatsMethod =
        typeof(CardHoverShowPatch).GetMethod(
            "AppendSupermassiveStats",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("AppendSupermassiveStats not found.");

    [Fact]
    public void Tooltip_Supermassive_ShowsCardsCreatedThisCombatAtZero()
    {
        var body = BuildBody(
            RuntimeHelpers.GetUninitializedObject(typeof(Supermassive)),
            0);

        Assert.Contains("Cards created this combat", body);
        Assert.Contains("[b]0[/b]", body);
    }

    [Fact]
    public void Tooltip_Supermassive_ShowsCardsCreatedThisCombatCount()
    {
        var body = BuildBody(
            RuntimeHelpers.GetUninitializedObject(typeof(Supermassive)),
            7);

        Assert.Contains("Cards created this combat", body);
        Assert.Contains("[b]7[/b]", body);
    }

    [Fact]
    public void Tooltip_SupermassiveStat_DoesNotShowOnOtherCards()
    {
        var body = BuildBody(
            RuntimeHelpers.GetUninitializedObject(typeof(Pounce)),
            7);

        Assert.DoesNotContain("Cards created this combat", body);
    }

    private static string BuildBody(object cardModel, int cardsCreatedThisCombat)
    {
        var sb = new StringBuilder();
        _ = AppendSupermassiveStatsMethod.Invoke(
            null,
            new[] { (object)sb, cardModel, cardsCreatedThisCombat });
        return sb.ToString();
    }
}
