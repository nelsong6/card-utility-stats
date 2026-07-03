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
}
