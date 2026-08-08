using System;
using System.Text;
using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

/// <summary>
/// Pins <see cref="StatsTooltip.EscapeBbcode"/>: dynamic display names injected
/// into the RichTextLabel body must not be interpretable as BBCode tags, or a
/// modded card/relic/status name containing '[' would break tooltip rendering.
/// </summary>
public class StatsTooltipBbcodeTests
{
    [Fact]
    public void EscapeBbcode_LeavesPlainTextUntouched()
    {
        Assert.Equal("Poison", StatsTooltip.EscapeBbcode("Poison"));
        Assert.Equal("Weak (2)", StatsTooltip.EscapeBbcode("Weak (2)"));
    }

    [Fact]
    public void EscapeBbcode_NeutralizesOpeningBracket()
    {
        Assert.Equal("[lb]b]bold[lb]/b]", StatsTooltip.EscapeBbcode("[b]bold[/b]"));
    }

    [Fact]
    public void EscapeBbcode_EscapesEveryOpeningBracket()
    {
        Assert.Equal("[lb][lb][lb]", StatsTooltip.EscapeBbcode("[[["));
    }

    [Fact]
    public void EscapeBbcode_LeavesLoneClosingBracketInert()
    {
        // A ']' with no matching '[' is not a tag, so it needs no escaping.
        Assert.Equal("x]y", StatsTooltip.EscapeBbcode("x]y"));
    }

    [Fact]
    public void EscapeBbcode_HandlesNullAndEmpty()
    {
        Assert.Equal(string.Empty, StatsTooltip.EscapeBbcode(null));
        Assert.Equal(string.Empty, StatsTooltip.EscapeBbcode(string.Empty));
    }

    [Fact]
    public void ScalarStatRows_ShareNaturalWidthTableAndLeftAlignEveryColumn()
    {
        var body = new StringBuilder();
        StatsTooltip.AppendScalarStatRow(
            body,
            StatsTooltip.CreateStatRowPresentation("Short"),
            "0");
        StatsTooltip.AppendScalarStatRow(
            body,
            StatsTooltip.CreateStatRowPresentation("The longest semantic label"),
            "false",
            "100%");

        var markup = body.ToString();

        Assert.Equal(1, markup.Split("[table=4]", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, markup.Split("[left][b]", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, markup.Split("[left][color=#b5b5b5]", StringSplitOptions.None).Length - 1);
        Assert.Contains("[left][b]0[/b][/left]", markup);
        Assert.Contains("[left][b]false[/b][/left]", markup);
        Assert.DoesNotContain("[right]", markup);
        Assert.True(StatsTooltip.ContainsScalarStatTable(markup));
    }
}
