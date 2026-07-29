using System;
using System.Reflection;
using System.Text.Json;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class SilkenTressStatsTests
{
    private const string SilkenTressRelicId = "RELIC.SILKEN_TRESS";

    private static readonly MethodInfo BuildSilkenTressBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildSilkenTressBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildSilkenTressBodyBBCode not found.");

    [Fact]
    public void RelicAggregate_SilkenTressFields_DefaultToEmpty()
    {
        var agg = new RelicAggregate();

        Assert.Empty(agg.SilkenTressGlamCards);
    }

    [Fact]
    public void RelicAggregate_SilkenTressFields_JsonRoundtripPreservesCards()
    {
        var run = new RunData();
        run.RelicAggregates[SilkenTressRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"silken_tress_glam_cards\"", json);
        Assert.NotNull(restored);
        Assert.Equal(
            new[] { "Pommel Strike" },
            restored!.RelicAggregates[SilkenTressRelicId].SilkenTressGlamCards);
    }

    [Fact]
    public void RunTracker_SilkenTressHelper_IgnoresBlankNames()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordSilkenTressGlamCardsForTest(
            agg,
            new[] { "", "Pommel Strike", "  " });

        Assert.Equal(new[] { "Pommel Strike" }, agg.SilkenTressGlamCards);
    }

    [Fact]
    public void RelicAggregate_SilkenTressFields_Merge()
    {
        var target = new RelicAggregate
        {
            SilkenTressGlamCards = { "Bash" },
        };

        RunTracker.MergeRelicAggregateInto(target, PopulatedAggregate());

        Assert.Equal(
            new[] { "Bash", "Pommel Strike" },
            target.SilkenTressGlamCards);
    }

    [Fact]
    public void RelicTooltip_SilkenTress_ShowsTakenGlamCard()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("card"), body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("glam"), body);
        Assert.Contains("[b]+[/b]", body);
        Assert.DoesNotContain("Card enchanted with Glam", body);
        Assert.Contains("Pommel Strike", body);
    }

    [Fact]
    public void RelicTooltip_SilkenTress_ShowsNoneBeforeCardIsTaken()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("card"), body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("glam"), body);
        Assert.Contains("[b]+[/b]", body);
        Assert.DoesNotContain("Card enchanted with Glam", body);
        Assert.Contains("none", body);
    }

    private static RelicAggregate PopulatedAggregate()
        => new()
        {
            SilkenTressGlamCards = { "Pommel Strike" },
        };

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildSilkenTressBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildSilkenTressBodyBBCode returned null."));
}
