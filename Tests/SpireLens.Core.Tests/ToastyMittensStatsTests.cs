using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class ToastyMittensStatsTests
{
    private const string ToastyMittensRelicId = "RELIC.TOASTY_MITTENS";

    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildToastyMittensBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildToastyMittensBodyBBCode not found.");

    [Fact]
    public void Patch_TargetsToastyMittensBeforeHandDrawWithExpectedParameters()
    {
        var target = typeof(ToastyMittens).GetMethod(
            nameof(ToastyMittens.BeforeHandDraw),
            new[]
            {
                typeof(Player),
                typeof(PlayerChoiceContext),
                typeof(ICombatState),
            });

        Assert.NotNull(target);
        Assert.Equal(
            new[]
            {
                typeof(Player),
                typeof(PlayerChoiceContext),
                typeof(ICombatState),
            },
            target!.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void RelicAggregate_ToastyMittensFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.ToastyMittensCardsExhausted);
        Assert.Equal(0m, agg.StrengthAdded);
        Assert.Equal(0, agg.ToastyMittensCombats);
    }

    [Fact]
    public void RelicAggregate_ToastyMittensFields_JsonRoundtripPreservesValues()
    {
        var run = new RunData();
        run.RelicAggregates[ToastyMittensRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"toasty_mittens_cards_exhausted\"", json);
        Assert.Contains("\"strength_added\"", json);
        Assert.Contains("\"toasty_mittens_combats\"", json);
        Assert.NotNull(restored);
        AssertPopulatedAggregate(restored!.RelicAggregates[ToastyMittensRelicId]);
    }

    [Fact]
    public void RunTracker_ToastyMittensHelper_AccumulatesAndClamps()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordToastyMittensForTest(agg, 7, 10m, 4);
        RunTracker.RecordToastyMittensForTest(agg, -1, -2m, -3);

        AssertPopulatedAggregate(agg);
    }

    [Fact]
    public void RelicAggregate_ToastyMittensFields_Merge()
    {
        var target = new RelicAggregate
        {
            ToastyMittensCardsExhausted = 3,
            StrengthAdded = 4m,
            ToastyMittensCombats = 1,
        };
        var source = new RelicAggregate
        {
            ToastyMittensCardsExhausted = 4,
            StrengthAdded = 6m,
            ToastyMittensCombats = 3,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        AssertPopulatedAggregate(target);
    }

    [Fact]
    public void RelicTooltip_ToastyMittens_ShowsRequestedTotalsAndCombatAverages()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("Cards exhausted total", body);
        Assert.Contains("Strength added total", body);
        Assert.Contains("Cards exhausted per combat", body);
        Assert.Contains("Strength added per combat", body);
        Assert.Contains("[b]7[/b]", body);
        Assert.Contains("[b]10[/b]", body);
        Assert.Contains("[b]1.75[/b]", body);
        Assert.Contains("[b]2.5[/b]", body);
    }

    [Fact]
    public void RelicTooltip_ToastyMittens_ShowsZeroAveragesWithoutCombats()
    {
        var body = BuildBody(new RelicAggregate
        {
            ToastyMittensCardsExhausted = 2,
            StrengthAdded = 3m,
        });

        Assert.Contains("Cards exhausted per combat", body);
        Assert.Contains("Strength added per combat", body);
        Assert.Equal(2, CountOccurrences(body, "[b]0[/b]"));
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void RelicTooltip_ToastyMittens_DispatchesForModel()
    {
        var relic = (ToastyMittens)RuntimeHelpers.GetUninitializedObject(typeof(ToastyMittens));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            PopulatedAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Toasty Mittens", title);
        Assert.Contains("Cards exhausted total", body);
    }

    private static RelicAggregate PopulatedAggregate()
        => new()
        {
            ToastyMittensCardsExhausted = 7,
            StrengthAdded = 10m,
            ToastyMittensCombats = 4,
        };

    private static void AssertPopulatedAggregate(RelicAggregate agg)
    {
        Assert.Equal(7, agg.ToastyMittensCardsExhausted);
        Assert.Equal(10m, agg.StrengthAdded);
        Assert.Equal(4, agg.ToastyMittensCombats);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildToastyMittensBodyBBCode returned null."));

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
