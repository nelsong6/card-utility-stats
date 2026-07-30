using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class GnarledHammerStatsTests
{
    private const string GnarledHammerRelicId = "RELIC.GNARLED_HAMMER";

    private static readonly MethodInfo BuildGnarledHammerBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildGnarledHammerBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildGnarledHammerBodyBBCode not found.");

    [Fact]
    public void Patch_TargetsGnarledHammerAfterObtained()
    {
        var target = typeof(GnarledHammer).GetMethod(
            nameof(GnarledHammer.AfterObtained),
            Type.EmptyTypes);

        Assert.NotNull(target);
    }

    [Fact]
    public void RelicAggregate_GnarledHammerFields_DefaultToEmpty()
    {
        var agg = new RelicAggregate();

        Assert.NotNull(agg.SharpEnchantedCards);
        Assert.Empty(agg.SharpEnchantedCards);
    }

    [Fact]
    public void RelicAggregate_GnarledHammerFields_JsonRoundtripPreservesCards()
    {
        var run = new RunData();
        run.RelicAggregates[GnarledHammerRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"sharp_enchanted_cards\"", json);
        Assert.NotNull(restored);
        Assert.Equal(
            new[] { "Pommel Strike", "Uppercut+", "Pommel Strike" },
            restored!.RelicAggregates[GnarledHammerRelicId].SharpEnchantedCards);
    }

    [Fact]
    public void RunTracker_GnarledHammerHelper_PreservesDuplicatesAndIgnoresBlankNames()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordGnarledHammerSharpCardsForTest(
            agg,
            new[] { "Pommel Strike", "", "Uppercut+", "Pommel Strike" });

        Assert.Equal(
            new[] { "Pommel Strike", "Uppercut+", "Pommel Strike" },
            agg.SharpEnchantedCards);
    }

    [Fact]
    public void RelicAggregate_GnarledHammerFields_Merge()
    {
        var target = new RelicAggregate
        {
            SharpEnchantedCards = { "Bash" },
        };

        RunTracker.MergeRelicAggregateInto(target, PopulatedAggregate());

        Assert.Equal(
            new[] { "Bash", "Pommel Strike", "Uppercut+", "Pommel Strike" },
            target.SharpEnchantedCards);
    }

    [Fact]
    public void RelicTooltip_GnarledHammer_ShowsEverySharpEnchantedCard()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("Cards enchanted with Sharp", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("Sharp-enchanted card", body);
        Assert.Contains("Pommel Strike", body);
        Assert.Contains("Uppercut+", body);
        Assert.Equal(2, CountOccurrences(body, "Pommel Strike"));
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void RelicTooltip_GnarledHammer_DispatchesForModel()
    {
        var relic = (GnarledHammer)RuntimeHelpers.GetUninitializedObject(typeof(GnarledHammer));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            PopulatedAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Gnarled Hammer", title);
        Assert.Contains("Cards enchanted with Sharp", body);
    }

    [Fact]
    public void RelicAggregate_OlderShapeWithoutGnarledHammerFields_DefaultsToEmpty()
    {
        var agg = JsonSerializer.Deserialize<RelicAggregate>("{}", RunStorage.Options);

        Assert.NotNull(agg);
        Assert.Empty(agg!.SharpEnchantedCards);
    }

    private static RelicAggregate PopulatedAggregate()
        => new()
        {
            SharpEnchantedCards =
            {
                "Pommel Strike",
                "Uppercut+",
                "Pommel Strike",
            },
        };

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildGnarledHammerBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildGnarledHammerBodyBBCode returned null."));
}
