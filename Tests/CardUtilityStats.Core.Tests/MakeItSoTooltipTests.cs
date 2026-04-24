using System.Reflection;
using System.Text;
using CardUtilityStats.Core;
using CardUtilityStats.Core.Patches;
using Xunit;

namespace CardUtilityStats.Core.Tests;

public class MakeItSoTooltipTests
{
    private static readonly MethodInfo AppendMakeItSoStatsMethod =
        typeof(CardHoverShowPatch).GetMethod(
            "AppendMakeItSoStats",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(StringBuilder), typeof(CardAggregate), typeof(bool), typeof(int?), typeof(int) },
            modifiers: null)
        ?? throw new InvalidOperationException("AppendMakeItSoStats overload not found.");

    [Fact]
    public void AppendMakeItSoStats_RendersTriggerProgress()
    {
        var sb = new StringBuilder();

        _ = AppendMakeItSoStatsMethod.Invoke(null, new object?[] { sb, new CardAggregate(), false, 2, 3 });
        var text = sb.ToString();

        // Player-legible label: "Trigger progress" not the internal "Skill counter"
        Assert.Contains("Trigger progress", text);
        Assert.Contains("[b]2/3[/b]", text);
    }

    [Fact]
    public void AppendMakeItSoStats_CompactViewRendersTriggerProgress()
    {
        var sb = new StringBuilder();

        _ = AppendMakeItSoStatsMethod.Invoke(null, new object?[] { sb, new CardAggregate(), true, 1, 3 });
        var text = sb.ToString();

        // Trigger progress is shown in compact (hand-hover) view — players need it mid-combat
        Assert.Contains("Trigger progress", text);
        Assert.Contains("[b]1/3[/b]", text);
    }

    [Fact]
    public void AppendMakeItSoStats_FullViewRendersTimesTriggered()
    {
        var sb = new StringBuilder();
        var agg = new CardAggregate
        {
            TimesSummonedToHand = 2,
        };

        _ = AppendMakeItSoStatsMethod.Invoke(null, new object?[] { sb, agg, false, null, 0 });
        var text = sb.ToString();

        // Player-legible label: "Times triggered" not the internal "Summoned to hand"
        Assert.Contains("Times triggered", text);
        Assert.Contains("[b]2[/b]", text);
    }

    [Fact]
    public void AppendMakeItSoStats_CompactViewSkipsTimesTriggered()
    {
        var sb = new StringBuilder();
        var agg = new CardAggregate
        {
            TimesSummonedToHand = 2,
        };

        _ = AppendMakeItSoStatsMethod.Invoke(null, new object?[] { sb, agg, true, null, 0 });
        var text = sb.ToString();

        // Full trigger history is a fuller-stats-view item; compact just shows live progress
        Assert.DoesNotContain("Times triggered", text);
    }
}
