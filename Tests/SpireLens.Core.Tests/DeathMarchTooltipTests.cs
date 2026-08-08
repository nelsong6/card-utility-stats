using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using MegaCrit.Sts2.Core.Models.Cards;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class DeathMarchTooltipTests
{
    private static readonly MethodInfo AppendDeathMarchStatsMethod =
        typeof(CardHoverShowPatch).GetMethod(
            "AppendDeathMarchStats",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("AppendDeathMarchStats not found.");

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void DeathMarchTooltip_AlwaysShowsCardsDrawnThisTurn(int cardsDrawnThisTurn)
    {
        var card = (DeathMarch)RuntimeHelpers.GetUninitializedObject(typeof(DeathMarch));
        var sb = new StringBuilder();

        _ = AppendDeathMarchStatsMethod.Invoke(
            null,
            new object?[] { sb, card, cardsDrawnThisTurn });

        var body = sb.ToString();
        Assert.Contains("Cards drawn this turn", body);
        Assert.Contains($"[b]{cardsDrawnThisTurn}[/b]", body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("draw"), body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("in"), body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("turn"), body);
        Assert.Contains("excluding the automatic opening-hand draw", body);
    }

    [Fact]
    public void DeathMarchTooltip_ClampsUnavailableOrInvalidCountsToZero()
    {
        var card = (DeathMarch)RuntimeHelpers.GetUninitializedObject(typeof(DeathMarch));
        var sb = new StringBuilder();

        _ = AppendDeathMarchStatsMethod.Invoke(null, new object?[] { sb, card, -1 });

        Assert.Contains("[b]0[/b]", sb.ToString());
    }
}
