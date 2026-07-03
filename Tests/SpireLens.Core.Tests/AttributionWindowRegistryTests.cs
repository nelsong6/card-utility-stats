using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

/// <summary>
/// Pins the arbitration core of the AttributionWindow registry (#250). Relic
/// attribution windows are verifiable end-to-end only in live combat, so the
/// logic that decides WHICH window claims an event — exclusive consumption,
/// FIFO ordering, owner keying, staleness — is tested here directly.
/// </summary>
public class AttributionWindowRegistryTests
{
    private const int H = 100; // arbitrary history count

    [Fact]
    public void TryConsume_NoWindow_ReturnsNull()
    {
        var r = new AttributionWindowRegistry();
        Assert.Null(r.TryConsume(AttributionEventKind.PlayerBlockGain, H));
    }

    [Fact]
    public void Consume_IsExclusive_SecondEventGetsNothing()
    {
        var r = new AttributionWindowRegistry();
        r.Arm("orichalcum", AttributionEventKind.PlayerBlockGain, H);

        Assert.Equal("orichalcum", r.TryConsume(AttributionEventKind.PlayerBlockGain, H));
        // The single window was consumed; a second block gain in the same turn
        // must not re-claim it (this is the double-attribution fix).
        Assert.Null(r.TryConsume(AttributionEventKind.PlayerBlockGain, H));
    }

    [Fact]
    public void TwoWindowsSameKind_EachClaimsOneEvent_FIFO()
    {
        // Orichalcum + Cloak Clasp both legitimately gain block in one
        // BeforeSideTurnEnd — two distinct events, each must claim its own
        // window, oldest first.
        var r = new AttributionWindowRegistry();
        r.Arm("orichalcum", AttributionEventKind.PlayerBlockGain, H);
        r.Arm("cloak_clasp", AttributionEventKind.PlayerBlockGain, H);

        Assert.Equal("orichalcum", r.TryConsume(AttributionEventKind.PlayerBlockGain, H));
        Assert.Equal("cloak_clasp", r.TryConsume(AttributionEventKind.PlayerBlockGain, H));
        Assert.Null(r.TryConsume(AttributionEventKind.PlayerBlockGain, H));
    }

    [Fact]
    public void DifferentKinds_DoNotClaimEachOthersEvents()
    {
        var r = new AttributionWindowRegistry();
        r.Arm("happy_flower", AttributionEventKind.PlayerEnergyGain, H);

        Assert.Null(r.TryConsume(AttributionEventKind.PlayerBlockGain, H));
        Assert.Equal("happy_flower", r.TryConsume(AttributionEventKind.PlayerEnergyGain, H));
    }

    [Fact]
    public void OwnerKeyed_OnlyMatchesSameOwnerReference()
    {
        var r = new AttributionWindowRegistry();
        var p1 = new object();
        var p2 = new object();
        r.Arm("gremlin_horn", AttributionEventKind.PlayerEnergyGain, H, ownerId: p1);

        // A different player's energy gain must not claim p1's window.
        Assert.Null(r.TryConsume(AttributionEventKind.PlayerEnergyGain, H, ownerId: p2));
        Assert.Equal("gremlin_horn", r.TryConsume(AttributionEventKind.PlayerEnergyGain, H, ownerId: p1));
    }

    [Fact]
    public void Stale_WindowBeyondMaxHistoryAdvance_IsNotClaimed()
    {
        var r = new AttributionWindowRegistry();
        r.Arm("happy_flower", AttributionEventKind.PlayerEnergyGain, H, maxHistoryAdvance: 0);

        // One history entry later, a maxHistoryAdvance:0 window is stale.
        Assert.Null(r.TryConsume(AttributionEventKind.PlayerEnergyGain, H + 1));
        // And it was pruned, so it can't be claimed even back at H.
        Assert.Null(r.TryConsume(AttributionEventKind.PlayerEnergyGain, H));
    }

    [Fact]
    public void NeverStale_WindowSurvivesArbitraryHistoryAdvance()
    {
        var r = new AttributionWindowRegistry();
        r.Arm("orichalcum", AttributionEventKind.PlayerBlockGain, H, maxHistoryAdvance: -1);

        Assert.Equal("orichalcum", r.TryConsume(AttributionEventKind.PlayerBlockGain, H + 9999));
    }

    [Fact]
    public void Disarm_RemovesMatchingWindow()
    {
        var r = new AttributionWindowRegistry();
        r.Arm("bone_flute", AttributionEventKind.PlayerBlockGain, H);
        r.Disarm("bone_flute", AttributionEventKind.PlayerBlockGain);

        Assert.Null(r.TryConsume(AttributionEventKind.PlayerBlockGain, H));
    }

    [Fact]
    public void ArmedCount_ReflectsOverlap()
    {
        var r = new AttributionWindowRegistry();
        r.Arm("orichalcum", AttributionEventKind.PlayerBlockGain, H);
        r.Arm("cloak_clasp", AttributionEventKind.PlayerBlockGain, H);
        Assert.Equal(2, r.ArmedCount(AttributionEventKind.PlayerBlockGain));
        Assert.Equal(0, r.ArmedCount(AttributionEventKind.PlayerEnergyGain));
    }
}
