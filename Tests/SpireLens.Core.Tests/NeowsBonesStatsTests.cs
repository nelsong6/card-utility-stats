using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class NeowsBonesStatsTests
{
    private const string NeowsBonesRelicId = "RELIC.NEOWS_BONES";
    private const string InjuryCardId = "CARD.INJURY#1";

    private static readonly MethodInfo BuildNeowsBonesBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildNeowsBonesBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildNeowsBonesBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_NeowsBonesFields_DefaultToEmpty()
    {
        var relicAgg = new RelicAggregate();
        var curseAgg = new CardAggregate();

        Assert.Empty(relicAgg.RelicsGranted);
        Assert.Empty(relicAgg.CardsGranted);
        Assert.Equal(0, curseAgg.CombatsInDeck);
        Assert.Equal(0, curseAgg.TimesDrawn);
        Assert.Equal(0, curseAgg.TimesDiscarded);
        Assert.Equal(0, curseAgg.Plays);
        Assert.Equal(0, curseAgg.TimesExhausted);
    }

    [Fact]
    public void RelicAggregate_NeowsBonesFields_JsonRoundtrip_PreservesRelicsAndCurseStats()
    {
        var run = new RunData();
        run.RelicAggregates[NeowsBonesRelicId] = NeowsBonesAggregate();
        run.Aggregates[InjuryCardId] = InjuryAggregate();

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("relics_granted", json);
        Assert.Contains("cards_granted", json);
        Assert.Contains(InjuryCardId, json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var relicAgg = restored!.RelicAggregates[NeowsBonesRelicId];
        Assert.Equal(1, relicAgg.RelicsGranted["RELIC.NEOWS_TALISMAN"].Count);
        Assert.Equal("Neow's Talisman", relicAgg.RelicsGranted["RELIC.NEOWS_TALISMAN"].DisplayName);
        Assert.Equal(1, relicAgg.RelicsGranted["RELIC.NEOWS_TORMENT"].Count);
        Assert.Equal("Neow's Torment", relicAgg.RelicsGranted["RELIC.NEOWS_TORMENT"].DisplayName);
        Assert.Equal(1, relicAgg.CardsGranted["CARD.INJURY"].Count);
        Assert.Equal("Injury", relicAgg.CardsGranted["CARD.INJURY"].DisplayName);

        var curseAgg = restored.Aggregates[InjuryCardId];
        Assert.Equal(3, curseAgg.CombatsInDeck);
        Assert.Equal(5, curseAgg.TimesDrawn);
        Assert.Equal(2, curseAgg.TimesDiscarded);
        Assert.Equal(1, curseAgg.Plays);
        Assert.Equal(2, curseAgg.TimesExhausted);
    }

    [Fact]
    public void RunTracker_NeowsBonesHelpers_RecordRelicsAndCurse()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordNeowsBonesRelicObtainedForTest(agg, "RELIC.NEOWS_TALISMAN", "Neow's Talisman");
        RunTracker.RecordNeowsBonesRelicObtainedForTest(agg, "RELIC.NEOWS_TORMENT", "Neow's Torment");
        RunTracker.RecordNeowsBonesCurseGrantedForTest(agg, "CARD.INJURY", "Injury");
        RunTracker.RecordNeowsBonesRelicObtainedForTest(agg, null, null);
        RunTracker.RecordNeowsBonesCurseGrantedForTest(agg, null, null);

        Assert.Equal(2, agg.RelicsGranted.Count);
        Assert.Equal(1, agg.RelicsGranted["RELIC.NEOWS_TALISMAN"].Count);
        Assert.Equal(1, agg.RelicsGranted["RELIC.NEOWS_TORMENT"].Count);
        Assert.Single(agg.CardsGranted);
        Assert.Equal(1, agg.CardsGranted["CARD.INJURY"].Count);
        Assert.Equal("Injury", agg.CardsGranted["CARD.INJURY"].DisplayName);
    }

    [Fact]
    public void MergeRelicAggregateInto_NeowsBonesFields_MergesRelicsAndCurse()
    {
        var target = new RelicAggregate();
        var source = NeowsBonesAggregate();

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(1, target.RelicsGranted["RELIC.NEOWS_TALISMAN"].Count);
        Assert.Equal(1, target.RelicsGranted["RELIC.NEOWS_TORMENT"].Count);
        Assert.Equal(1, target.CardsGranted["CARD.INJURY"].Count);
        Assert.Equal("Injury", target.CardsGranted["CARD.INJURY"].DisplayName);
    }

    [Fact]
    public void RelicTooltip_NeowsBones_ShowsRelicsAndCurseStats()
    {
        var body = BuildBody(
            NeowsBonesAggregate(),
            new Dictionary<string, CardAggregate>
            {
                ["CARD.INJURY"] = InjuryAggregate(),
            });

        Assert.Contains("Neow relics obtained", body);
        Assert.Contains("Neow relic", body);
        Assert.Contains("Neow's Talisman", body);
        Assert.Contains("Neow's Torment", body);
        Assert.Contains("Curses added", body);
        Assert.Contains("Curse added", body);
        Assert.Contains("Injury", body);
        Assert.Contains("Injury combats", body);
        Assert.Contains("Injury drawn", body);
        Assert.Contains("Injury discarded", body);
        Assert.Contains("Injury played", body);
        Assert.Contains("Injury exhausted", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[b]5[/b]", body);
    }

    [Fact]
    public void RelicTooltip_NeowsBones_ShowsZeroRowsWithoutStats()
    {
        var body = BuildBody(new RelicAggregate(), new Dictionary<string, CardAggregate>());

        Assert.Contains("Neow relics obtained", body);
        Assert.Contains("Curses added", body);
        Assert.Contains("Curse combats", body);
        Assert.Contains("Curse drawn", body);
        Assert.Contains("Curse discarded", body);
        Assert.Contains("Curse played", body);
        Assert.Contains("Curse exhausted", body);
        Assert.Contains("[b]0[/b]", body);
    }

    private static RelicAggregate NeowsBonesAggregate()
        => new()
        {
            RelicsGranted =
            {
                ["RELIC.NEOWS_TALISMAN"] = new RelicGrantedAggregate
                {
                    RelicId = "RELIC.NEOWS_TALISMAN",
                    DisplayName = "Neow's Talisman",
                    Count = 1,
                },
                ["RELIC.NEOWS_TORMENT"] = new RelicGrantedAggregate
                {
                    RelicId = "RELIC.NEOWS_TORMENT",
                    DisplayName = "Neow's Torment",
                    Count = 1,
                },
            },
            CardsGranted =
            {
                ["CARD.INJURY"] = new RelicCardAggregate
                {
                    CardId = "CARD.INJURY",
                    DisplayName = "Injury",
                    Count = 1,
                },
            },
        };

    private static CardAggregate InjuryAggregate()
        => new()
        {
            CombatsInDeck = 3,
            TimesDrawn = 5,
            TimesDiscarded = 2,
            Plays = 1,
            TimesExhausted = 2,
        };

    private static string BuildBody(
        RelicAggregate relicAgg,
        IReadOnlyDictionary<string, CardAggregate> curseAggregates)
        => (string)(BuildNeowsBonesBodyMethod.Invoke(null, new object?[] { relicAgg, curseAggregates })
            ?? throw new InvalidOperationException("BuildNeowsBonesBodyBBCode returned null."));
}
