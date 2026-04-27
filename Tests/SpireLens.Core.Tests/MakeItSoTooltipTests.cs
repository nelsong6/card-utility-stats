using System.Reflection;
using System.Text;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

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

        Assert.Contains("Trigger progress", text);
        Assert.Contains("[b]2/3[/b]", text);
    }

    [Fact]
    public void AppendMakeItSoStats_CompactView_RendersTriggerProgress()
    {
        var sb = new StringBuilder();

        _ = AppendMakeItSoStatsMethod.Invoke(null, new object?[] { sb, new CardAggregate(), true, 1, 3 });
        var text = sb.ToString();

        Assert.Contains("Trigger progress", text);
        Assert.Contains("[b]1/3[/b]", text);
    }

    [Fact]
    public void AppendMakeItSoStats_NoCounter_OmitsTriggerProgressRow()
    {
        var sb = new StringBuilder();

        _ = AppendMakeItSoStatsMethod.Invoke(null, new object?[] { sb, new CardAggregate(), false, null, 0 });
        var text = sb.ToString();

        Assert.DoesNotContain("Trigger progress", text);
    }

    [Fact]
    public void AppendMakeItSoStats_FullView_RendersTriggeredCount()
    {
        var sb = new StringBuilder();
        var agg = new CardAggregate
        {
            TimesSummonedToHand = 2,
        };

        _ = AppendMakeItSoStatsMethod.Invoke(null, new object?[] { sb, agg, false, null, 0 });
        var text = sb.ToString();

        Assert.Contains("Triggered", text);
        Assert.Contains("[b]2[/b]", text);
    }

    [Fact]
    public void AppendMakeItSoStats_CompactView_SkipsTriggeredCount()
    {
        var sb = new StringBuilder();
        var agg = new CardAggregate
        {
            TimesSummonedToHand = 2,
        };

        _ = AppendMakeItSoStatsMethod.Invoke(null, new object?[] { sb, agg, true, null, 0 });
        var text = sb.ToString();

        Assert.DoesNotContain("Triggered", text);
    }
}
