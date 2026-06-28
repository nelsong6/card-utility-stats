using System;
using System.Globalization;

namespace SpireLens.Config;

internal static class BuildMetadataFormatter
{
    public static string FormatBuildDate(string buildTimestampUtc, BuildDisplayTimeZone selectedTimeZone)
    {
        if (!DateTimeOffset.TryParse(
                buildTimestampUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var utc))
            return buildTimestampUtc;

        if (!TryResolveTimeZone(selectedTimeZone, out var zone, out var label))
            return utc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

        var converted = TimeZoneInfo.ConvertTime(utc, zone);
        return $"{converted:yyyy-MM-dd HH:mm:ss zzz} ({label})";
    }

    public static string GetTimeZoneId(BuildDisplayTimeZone selectedTimeZone)
    {
        return selectedTimeZone switch
        {
            BuildDisplayTimeZone.Pacific => "America/Los_Angeles",
            BuildDisplayTimeZone.Eastern => "America/New_York",
            BuildDisplayTimeZone.Central => "America/Chicago",
            BuildDisplayTimeZone.Mountain => "America/Denver",
            BuildDisplayTimeZone.UTC => "UTC",
            BuildDisplayTimeZone.Local => TimeZoneInfo.Local.Id,
            _ => "America/Los_Angeles",
        };
    }

    private static bool TryResolveTimeZone(
        BuildDisplayTimeZone selectedTimeZone,
        out TimeZoneInfo zone,
        out string label)
    {
        var candidates = selectedTimeZone switch
        {
            BuildDisplayTimeZone.Pacific => ("Pacific", new[] { "Pacific Standard Time", "America/Los_Angeles" }),
            BuildDisplayTimeZone.Eastern => ("Eastern", new[] { "Eastern Standard Time", "America/New_York" }),
            BuildDisplayTimeZone.Central => ("Central", new[] { "Central Standard Time", "America/Chicago" }),
            BuildDisplayTimeZone.Mountain => ("Mountain", new[] { "Mountain Standard Time", "America/Denver" }),
            BuildDisplayTimeZone.UTC => ("UTC", new[] { "UTC" }),
            BuildDisplayTimeZone.Local => ("Local", new[] { TimeZoneInfo.Local.Id }),
            _ => ("Pacific", new[] { "Pacific Standard Time", "America/Los_Angeles" }),
        };

        label = candidates.Item1;
        foreach (var id in candidates.Item2)
        {
            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(id);
                return true;
            }
            catch
            {
                // Try the next platform-specific id.
            }
        }

        zone = TimeZoneInfo.Utc;
        return false;
    }
}
