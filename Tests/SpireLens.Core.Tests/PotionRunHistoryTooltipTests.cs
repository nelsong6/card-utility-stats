using System;
using System.Reflection;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class PotionRunHistoryTooltipTests
{
    private static readonly MethodInfo BuildTooltipBodyMethod =
        typeof(PotionCompendiumHistoryUi).GetMethod(
            "BuildTooltipBody",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildTooltipBody not found.");

    [Fact]
    public void NotTakenPotionTooltip_PutsEventOnItsOwnRow()
    {
        var entry = new PotionRunHistoryEntry
        {
            SeenFloor = 4,
            SeenLocationKind = "Event",
            SeenLocationName = "The Legends Were True",
            AcquisitionMethod = "Potion reward",
        };

        var body = BuildBody(entry, "in_progress");

        Assert.Contains("Seen  [b]Floor 4[/b]\n", body);
        Assert.Contains("Event  [b]The Legends Were True[/b]\n", body);
        Assert.DoesNotContain("Floor 4 · Event", body);
    }

    [Fact]
    public void TakenPotionTooltip_PutsAcquiredAndUsedLocationsOnTheirOwnRows()
    {
        var entry = new PotionRunHistoryEntry
        {
            Acquired = true,
            AcquiredFloor = 5,
            AcquiredLocationKind = "Shop",
            AcquiredLocationName = "Merchant",
            AcquisitionMethod = "Purchased",
            Used = true,
            UsedFloor = 7,
            UsedLocationKind = "Elite combat",
            UsedLocationName = "Lagavulin",
        };

        var body = BuildBody(entry, "in_progress");

        Assert.Contains("Acquired  [b]Floor 5[/b]\n", body);
        Assert.Contains("Shop  [b]Merchant[/b]\n", body);
        Assert.Contains("Used  [b]Floor 7[/b]\n", body);
        Assert.Contains("Elite combat  [b]Lagavulin[/b]", body);
    }

    private static string BuildBody(PotionRunHistoryEntry entry, string outcome)
        => (string)(BuildTooltipBodyMethod.Invoke(null, [entry, outcome])
            ?? throw new InvalidOperationException("BuildTooltipBody returned null."));
}
