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

    private static readonly MethodInfo BuildTooltipTitleMethod =
        typeof(PotionCompendiumHistoryUi).GetMethod(
            "BuildTooltipTitle",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildTooltipTitle not found.");

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

        var body = BuildBody(
            entry,
            "in_progress",
            PotionTimelineOccurrence.SeenNotTaken);

        Assert.Contains("Seen  [b]Floor 4[/b]\n", body);
        Assert.Contains("Event  [b]The Legends Were True[/b]\n", body);
        Assert.DoesNotContain("Floor 4 · Event", body);
    }

    [Fact]
    public void PotionLifecycleTooltips_SeparateAcquisitionFromUse()
    {
        var entry = new PotionRunHistoryEntry
        {
            Sequence = 3,
            DisplayName = "Swift Potion",
            Acquired = true,
            AcquiredFloor = 5,
            AcquiredLocationKind = "Shop",
            AcquiredLocationName = "Merchant",
            AcquisitionMethod = "Purchased",
            Used = true,
            UsedFloor = 7,
            UsedLocationKind = "Elite combat",
            UsedLocationName = "Lagavulin",
            UsedTurn = 2,
        };

        var acquiredBody = BuildBody(
            entry,
            "in_progress",
            PotionTimelineOccurrence.Acquired);
        var usedBody = BuildBody(
            entry,
            "in_progress",
            PotionTimelineOccurrence.Used);
        var title = (string)(BuildTooltipTitleMethod.Invoke(null, [entry])
            ?? throw new InvalidOperationException("BuildTooltipTitle returned null."));

        Assert.Equal("Swift Potion 3", title);
        Assert.Contains("Acquired  [b]Floor 5[/b]\n", acquiredBody);
        Assert.Contains("Shop  [b]Merchant[/b]\n", acquiredBody);
        Assert.DoesNotContain("Used  ", acquiredBody);
        Assert.Contains("Used  [b]Floor 7[/b]\n", usedBody);
        Assert.Contains("Elite combat  [b]Lagavulin[/b]\n", usedBody);
        Assert.Contains("Turn  [b]2[/b]", usedBody);
        Assert.DoesNotContain("Acquired  ", usedBody);
    }

    private static string BuildBody(
        PotionRunHistoryEntry entry,
        string outcome,
        PotionTimelineOccurrence occurrence)
        => (string)(BuildTooltipBodyMethod.Invoke(null, [entry, outcome, occurrence])
            ?? throw new InvalidOperationException("BuildTooltipBody returned null."));
}
