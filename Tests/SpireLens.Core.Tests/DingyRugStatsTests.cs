using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class DingyRugStatsTests
{
    private const string RelicId = "RELIC.DINGY_RUG";

    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildDingyRugBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildDingyRugBodyBBCode not found.");

    [Fact]
    public void NativeHook_ModifiesCardRewardCreationOptions()
    {
        var method = typeof(DingyRug).GetMethod(
            nameof(DingyRug.ModifyCardRewardCreationOptions),
            [typeof(Player), typeof(CardCreationOptions)]);

        Assert.NotNull(method);
        Assert.Equal(typeof(CardCreationOptions), method!.ReturnType);
        Assert.Equal("player", method.GetParameters()[0].Name);
        Assert.Equal("options", method.GetParameters()[1].Name);
    }

    [Fact]
    public void RelicAggregate_SharedCardRewardFieldsDefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.CardRewardsAffected);
        Assert.Empty(agg.CardRewardCategories);
    }

    [Fact]
    public void RelicAggregate_JsonRoundtripPreservesDingyRugStats()
    {
        var run = new RunData();
        run.RelicAggregates[RelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"card_rewards_affected\"", json);
        Assert.Contains("\"card_reward_categories\"", json);
        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[RelicId];
        Assert.Equal(3, agg.CardRewardsAffected);
        Assert.Equal(6, agg.CardRewardCategories["ironclad"].Count);
        Assert.Equal(2, agg.CardRewardCategories["ironclad"].Taken);
        Assert.Equal(3, agg.CardRewardCategories["colorless"].Count);
        Assert.Equal(0, agg.CardRewardCategories["colorless"].Taken);
    }

    [Fact]
    public void Tooltip_ShowsRewardCountAndAllFinalVisibleCardPools()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("Card rewards affected", body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("card"), body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("offered"), body);
        Assert.Contains(StatConceptGlossary.RenderHintedGlyph("taken"), body);
        Assert.Contains("Ironclad cards offered/taken", body);
        Assert.Contains("Silent cards offered/taken", body);
        Assert.Contains("Regent cards offered/taken", body);
        Assert.Contains("Necrobinder cards offered/taken", body);
        Assert.Contains("Defect cards offered/taken", body);
        Assert.Contains("Colorless cards offered/taken", body);
        Assert.Contains("while Dingy Rug was held", body);
        Assert.Contains("[b]6/2[/b]", body);
        Assert.Contains("[b]3/0[/b]", body);
        Assert.DoesNotContain("Energy generated", body);
    }

    [Fact]
    public void Tooltip_ShowsZeroRowsBeforeAnyRewardsAreObserved()
    {
        var body = BuildBody(new RelicAggregate());

        Assert.Contains("Ironclad cards offered/taken", body);
        Assert.Contains("Colorless cards offered/taken", body);
        Assert.Contains("[b]0/0[/b]", body);
    }

    [Fact]
    public void RelicTooltipDispatch_RecognizesDingyRug()
    {
        var relic = (DingyRug)RuntimeHelpers.GetUninitializedObject(typeof(DingyRug));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            PopulatedAggregate(),
            floorCount: null,
            out var title,
            out var body);

        Assert.True(recognized);
        Assert.Equal("Dingy Rug", title);
        Assert.Contains("Colorless cards offered/taken", body);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildBodyMethod.Invoke(null, [agg])
            ?? throw new InvalidOperationException("BuildDingyRugBodyBBCode returned null."));

    private static RelicAggregate PopulatedAggregate()
        => new()
        {
            CardRewardsAffected = 3,
            CardRewardCategories =
            {
                ["ironclad"] = new CardRewardCategoryAggregate { DisplayName = "Ironclad", Count = 6, Taken = 2 },
                ["colorless"] = new CardRewardCategoryAggregate { DisplayName = "Colorless", Count = 3 },
            },
        };
}
