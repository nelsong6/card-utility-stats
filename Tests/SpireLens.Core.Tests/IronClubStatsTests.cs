using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class IronClubStatsTests
{
    private const string IronClubRelicId = "RELIC.IRON_CLUB";

    private static readonly MethodInfo BuildIronClubBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildIronClubBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildIronClubBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_IronClubFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.AdditionalCardsDrawn);
        Assert.Equal(0, agg.IronClubCombats);
        Assert.Equal(0, agg.IronClubCombatsEndedOn3Charges);
    }

    [Fact]
    public void RelicAggregate_IronClubFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[IronClubRelicId] = new RelicAggregate
        {
            AdditionalCardsDrawn = 7,
            IronClubCombats = 4,
            IronClubCombatsEndedOn3Charges = 2,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("additional_cards_drawn", json);
        Assert.Contains("iron_club_combats", json);
        Assert.Contains("iron_club_combats_ended_on3_charges", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[IronClubRelicId];
        Assert.Equal(7, agg.AdditionalCardsDrawn);
        Assert.Equal(4, agg.IronClubCombats);
        Assert.Equal(2, agg.IronClubCombatsEndedOn3Charges);
    }

    [Fact]
    public void RunTracker_IronClubHelpers_AccumulateAndClamp()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordIronClubStatsForTest(agg, combats: 4, cardsDrawn: 7, combatsEndedOn3Charges: 2);
        RunTracker.RecordIronClubStatsForTest(agg, combats: -1, cardsDrawn: -2, combatsEndedOn3Charges: -3);
        RunTracker.RecordIronClubCombatEndChargeForTest(agg, 3);
        RunTracker.RecordIronClubCombatEndChargeForTest(agg, 2);
        RunTracker.RecordIronClubCombatEndChargeForTest(agg, -3);

        Assert.Equal(7, agg.AdditionalCardsDrawn);
        Assert.Equal(4, agg.IronClubCombats);
        Assert.Equal(3, agg.IronClubCombatsEndedOn3Charges);
    }

    [Fact]
    public void RelicTooltip_IronClub_ShowsDrawTotalsAverageAndChargeRows()
    {
        var body = BuildBody(new RelicAggregate
        {
            AdditionalCardsDrawn = 7,
            IronClubCombats = 4,
            IronClubCombatsEndedOn3Charges = 2,
        });

        Assert.Contains("Cards drawn total", body);
        Assert.Contains("Avg cards drawn per combat", body);
        Assert.Contains("Combats ended on 3 charges", body);
        Assert.Contains("[b]7[/b]", body);
        Assert.Contains("[b]1.75[/b]", body);
        Assert.Contains("[b]2[/b]", body);
    }

    [Fact]
    public void RelicTooltip_IronClub_ShowsZeroRowsForEmptyAggregate()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Cards drawn total", body);
        Assert.Contains("Avg cards drawn per combat", body);
        Assert.Contains("Combats ended on 3 charges", body);
        Assert.Equal(3, CountOccurrences(body, "[b]0[/b]"));
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildIronClubBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildIronClubBodyBBCode returned null."));

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
