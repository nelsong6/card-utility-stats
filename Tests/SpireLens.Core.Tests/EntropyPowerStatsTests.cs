using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Cards;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class EntropyPowerStatsTests
{
    private const string EntropyPowerId = "POWER.ENTROPY";

    private static readonly MethodInfo AppendEntropyPowerStatsMethod =
        typeof(CardHoverShowPatch).GetMethod(
            "AppendEntropyPowerStats",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "AppendEntropyPowerStats not found.");

    [Fact]
    public void EntropyGeneratedCards_CountOnlySuccessfulObservedResults()
    {
        var agg = new PowerAggregate();

        RunTracker.RecordEntropyGeneratedCardForTest(
            agg,
            success: true,
            CardRarity.Common,
            originalWasBound: true);
        RunTracker.RecordEntropyGeneratedCardForTest(
            agg,
            success: true,
            CardRarity.Uncommon,
            originalWasBound: false);
        RunTracker.RecordEntropyGeneratedCardForTest(
            agg,
            success: true,
            CardRarity.Rare,
            originalWasBound: true);
        RunTracker.RecordEntropyGeneratedCardForTest(
            agg,
            success: true,
            CardRarity.Basic,
            originalWasBound: false);
        RunTracker.RecordEntropyGeneratedCardForTest(
            agg,
            success: false,
            CardRarity.Rare,
            originalWasBound: true);

        Assert.Equal(4, agg.EntropyCardsGenerated);
        Assert.Equal(2, agg.EntropyChainsOfBindingBroken);
        Assert.Equal(1, agg.EntropyCommonCardsGenerated);
        Assert.Equal(1, agg.EntropyUncommonCardsGenerated);
        Assert.Equal(1, agg.EntropyRareCardsGenerated);
    }

    [Fact]
    public void Promotion_MergesEntropyPowerStatsAndCombatDenominator()
    {
        var run = new RunData();
        run.MetaStats.PowerAggregates[EntropyPowerId] = new PowerAggregate
        {
            PowerId = EntropyPowerId,
            DisplayName = "Entropy",
            EntropyChainsOfBindingBroken = 1,
            EntropyCardsGenerated = 2,
            EntropyCommonCardsGenerated = 1,
            EntropyUncommonCardsGenerated = 1,
            CombatsActive = 1,
        };
        var pending = new PendingCombat();
        pending.MetaStats.PowerAggregates[EntropyPowerId] = new PowerAggregate
        {
            PowerId = EntropyPowerId,
            DisplayName = "Entropy",
            EntropyChainsOfBindingBroken = 1,
            EntropyCardsGenerated = 5,
            EntropyCommonCardsGenerated = 2,
            EntropyUncommonCardsGenerated = 1,
            EntropyRareCardsGenerated = 2,
            CombatsActive = 1,
        };

        RunTracker.PromotePendingCombatIntoRun(pending, run);

        AssertRepresentativeAggregate(
            run.MetaStats.PowerAggregates[EntropyPowerId]);
    }

    [Fact]
    public void EntropyTooltip_FullViewShowsRequestedBreakdownAndAverage()
    {
        var body = AppendEntropyPowerStats(
            CreateRepresentativeAggregate(),
            compact: false);

        Assert.Contains("Times Chains of Binding broken", body);
        Assert.Contains("Commons generated", body);
        Assert.Contains("Uncommons generated", body);
        Assert.Contains("Rares generated", body);
        Assert.Contains("Avg cards generated per combat", body);
        Assert.Contains("[b]3.5[/b]", body);
    }

    [Fact]
    public void EntropyTooltip_CompactViewKeepsOnlyChainBreakCount()
    {
        var body = AppendEntropyPowerStats(
            CreateRepresentativeAggregate(),
            compact: true);

        Assert.Contains("Times Chains of Binding broken", body);
        Assert.DoesNotContain("Commons generated", body);
        Assert.DoesNotContain("Uncommons generated", body);
        Assert.DoesNotContain("Rares generated", body);
        Assert.DoesNotContain("Avg cards generated per combat", body);
    }

    private static PowerAggregate CreateRepresentativeAggregate()
        => new()
        {
            PowerId = EntropyPowerId,
            DisplayName = "Entropy",
            EntropyChainsOfBindingBroken = 2,
            EntropyCardsGenerated = 7,
            EntropyCommonCardsGenerated = 3,
            EntropyUncommonCardsGenerated = 2,
            EntropyRareCardsGenerated = 2,
            CombatsActive = 2,
        };

    private static void AssertRepresentativeAggregate(PowerAggregate agg)
    {
        Assert.Equal(EntropyPowerId, agg.PowerId);
        Assert.Equal("Entropy", agg.DisplayName);
        Assert.Equal(2, agg.EntropyChainsOfBindingBroken);
        Assert.Equal(7, agg.EntropyCardsGenerated);
        Assert.Equal(3, agg.EntropyCommonCardsGenerated);
        Assert.Equal(2, agg.EntropyUncommonCardsGenerated);
        Assert.Equal(2, agg.EntropyRareCardsGenerated);
        Assert.Equal(2, agg.CombatsActive);
    }

    private static string AppendEntropyPowerStats(
        PowerAggregate agg,
        bool compact)
    {
        var sb = new StringBuilder();
        var card = (Entropy)RuntimeHelpers.GetUninitializedObject(
            typeof(Entropy));
        var metaStats = new RunMetaStats();
        metaStats.PowerAggregates[EntropyPowerId] = agg;
        _ = AppendEntropyPowerStatsMethod.Invoke(
            null,
            new object?[] { sb, card, metaStats, compact });
        return sb.ToString();
    }
}
