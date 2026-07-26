using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class LeafyPoulticeStatsTests
{
    private const string LeafyPoulticeRelicId = "RELIC.LEAFY_POULTICE";

    private static readonly MethodInfo BuildLeafyPoulticeBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildLeafyPoulticeBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildLeafyPoulticeBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_LeafyPoulticeFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Null(agg.OriginalMaxHp);
        Assert.Null(agg.NewMaxHp);
        Assert.Empty(agg.CardTransformations);
    }

    [Fact]
    public void RelicAggregate_LeafyPoulticeFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[LeafyPoulticeRelicId] = new RelicAggregate
        {
            Activations = 1,
            OriginalMaxHp = 70m,
            NewMaxHp = 58m,
            CardTransformations =
            {
                new RelicCardTransformationAggregate
                {
                    SourceCardId = "CARD.STRIKE_IRONCLAD",
                    SourceDisplayName = "Strike",
                    ResultCardId = "CARD.BASH",
                    ResultDisplayName = "Bash",
                },
                new RelicCardTransformationAggregate
                {
                    SourceCardId = "CARD.DEFEND_IRONCLAD",
                    SourceDisplayName = "Defend",
                    ResultCardId = "CARD.SHRUG_IT_OFF",
                    ResultDisplayName = "Shrug It Off",
                },
            },
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("original_max_hp", json);
        Assert.Contains("new_max_hp", json);
        Assert.Contains("card_transformations", json);
        Assert.Contains("source_card_id", json);
        Assert.Contains("result_card_id", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[LeafyPoulticeRelicId];
        Assert.Equal(1, agg.Activations);
        Assert.Equal(70m, agg.OriginalMaxHp);
        Assert.Equal(58m, agg.NewMaxHp);
        Assert.Equal(2, agg.CardTransformations.Count);
        Assert.Equal("CARD.STRIKE_IRONCLAD", agg.CardTransformations[0].SourceCardId);
        Assert.Equal("Strike", agg.CardTransformations[0].SourceDisplayName);
        Assert.Equal("CARD.BASH", agg.CardTransformations[0].ResultCardId);
        Assert.Equal("Bash", agg.CardTransformations[0].ResultDisplayName);
        Assert.Equal("CARD.DEFEND_IRONCLAD", agg.CardTransformations[1].SourceCardId);
        Assert.Equal("Defend", agg.CardTransformations[1].SourceDisplayName);
        Assert.Equal("CARD.SHRUG_IT_OFF", agg.CardTransformations[1].ResultCardId);
        Assert.Equal("Shrug It Off", agg.CardTransformations[1].ResultDisplayName);
    }

    [Fact]
    public void RunTracker_RecordLeafyPoulticeMaxHpChangedForTest_RecordsSnapshot()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordLeafyPoulticeMaxHpChangedForTest(agg, 70m, 58m);

        Assert.Equal(1, agg.Activations);
        Assert.Equal(70m, agg.OriginalMaxHp);
        Assert.Equal(58m, agg.NewMaxHp);
    }

    [Fact]
    public void RunTracker_RecordRelicCardTransformationForTest_RecordsTransformPair()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordRelicCardTransformationForTest(
            agg,
            "CARD.STRIKE_IRONCLAD",
            "Strike",
            "CARD.BASH",
            "Bash");

        Assert.Single(agg.CardTransformations);
        var transformation = agg.CardTransformations[0];
        Assert.Equal("CARD.STRIKE_IRONCLAD", transformation.SourceCardId);
        Assert.Equal("Strike", transformation.SourceDisplayName);
        Assert.Equal("CARD.BASH", transformation.ResultCardId);
        Assert.Equal("Bash", transformation.ResultDisplayName);
    }

    [Fact]
    public void RelicTooltip_LeafyPoultice_ShowsMaxHpLossRows()
    {
        var body = BuildBody(new RelicAggregate
        {
            Activations = 1,
            OriginalMaxHp = 70m,
            NewMaxHp = 58m,
            CardTransformations =
            {
                new RelicCardTransformationAggregate
                {
                    SourceCardId = "CARD.STRIKE_IRONCLAD",
                    SourceDisplayName = "Strike",
                    ResultCardId = "CARD.BASH",
                    ResultDisplayName = "Bash",
                },
                new RelicCardTransformationAggregate
                {
                    SourceCardId = "CARD.DEFEND_IRONCLAD",
                    SourceDisplayName = "Defend",
                    ResultCardId = "CARD.SHRUG_IT_OFF",
                    ResultDisplayName = "Shrug It Off",
                },
            },
        });

        Assert.Contains("Activations", body);
        Assert.Contains("Original max HP", body);
        Assert.Contains("New max HP", body);
        Assert.Contains("Max HP lost", body);
        Assert.Contains("Transform 1 source", body);
        Assert.Contains("Transform 1 result", body);
        Assert.Contains("Transform 2 source", body);
        Assert.Contains("Transform 2 result", body);
        Assert.Contains("[b]1[/b]", body);
        Assert.Contains("[b]70[/b]", body);
        Assert.Contains("[b]58[/b]", body);
        Assert.Contains("[b]12[/b]", body);
        Assert.Contains("[b]Strike[/b]", body);
        Assert.Contains("[b]Bash[/b]", body);
        Assert.Contains("[b]Defend[/b]", body);
        Assert.Contains("[b]Shrug It Off[/b]", body);
        Assert.Contains(
            "[color=#e0e0e0]Transform 2 result[/color]  [b]Shrug It Off[/b]",
            body);
        Assert.DoesNotContain("[right][b]Shrug It Off[/b]", body);
    }

    [Fact]
    public void RelicTooltip_LeafyPoultice_ShowsZeroRowsWithoutStats()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Activations", body);
        Assert.Contains("Original max HP", body);
        Assert.Contains("New max HP", body);
        Assert.Contains("Max HP lost", body);
        Assert.Contains("Transform 1 source", body);
        Assert.Contains("Transform 1 result", body);
        Assert.Contains("Transform 2 source", body);
        Assert.Contains("Transform 2 result", body);
        Assert.Equal(8, CountOccurrences(body, "[b]0[/b]"));
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildLeafyPoulticeBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildLeafyPoulticeBodyBBCode returned null."));

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
