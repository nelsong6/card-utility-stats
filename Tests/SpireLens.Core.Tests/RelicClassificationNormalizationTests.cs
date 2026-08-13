using System;
using System.Collections.Generic;
using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

/// <summary>
/// The relic classification store reconciles its document against the relics the
/// game actually defines. On a cold start that database does not exist yet when
/// the Core loads, so the reconciliation has to be deferrable and the rules have
/// to hold whenever it eventually runs.
/// </summary>
public class RelicClassificationNormalizationTests
{
    /// <summary>
    /// The regression guard for the cold-start bug: the game populates ModelDb
    /// one initialization step after it loads mods, so the store must be able to
    /// ask "is the relic database up yet?" and get a plain answer instead of a
    /// KeyNotFoundException for CHARACTER.IRONCLAD. Nothing in this test host
    /// ever calls ModelDb.Init, which is exactly the cold-start state.
    /// </summary>
    [Fact]
    public void LiveRelicDatabaseReadiness_ReportsNotReadyInsteadOfThrowing()
    {
        Assert.False(RelicClassificationStore.IsLiveRelicDatabaseReady());
    }

    [Fact]
    public void Normalization_DefaultsRelicsTheDocumentNeverListedToCombat()
    {
        var combat = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RELIC.AKABEKO" };
        var nonCombat = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RELIC.FROZEN_EGG" };
        var cutoffs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var changed = RelicClassificationStore.NormalizeClassifications(
            combat,
            nonCombat,
            cutoffs,
            new[] { "RELIC.AKABEKO", "RELIC.FROZEN_EGG", "RELIC.BRAND_NEW_RELIC" });

        Assert.True(changed);
        Assert.Contains("RELIC.BRAND_NEW_RELIC", combat);
        Assert.DoesNotContain("RELIC.BRAND_NEW_RELIC", nonCombat);
        Assert.Contains("RELIC.FROZEN_EGG", nonCombat);
        Assert.DoesNotContain("RELIC.FROZEN_EGG", combat);
    }

    [Fact]
    public void Normalization_DropsRelicIdsTheGameNoLongerDefines()
    {
        var combat = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RELIC.AKABEKO",
            "RELIC.CUT_FROM_THE_GAME",
        };
        var nonCombat = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RELIC.ALSO_CUT",
        };
        var cutoffs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var changed = RelicClassificationStore.NormalizeClassifications(
            combat,
            nonCombat,
            cutoffs,
            new[] { "RELIC.AKABEKO" });

        Assert.True(changed);
        Assert.Equal("RELIC.AKABEKO", Assert.Single(combat));
        Assert.Empty(nonCombat);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(0, false)]
    [InlineData(4, false)]
    [InlineData(-1, false)]
    public void Normalization_KeepsOnlyCutoffsInsideTheSupportedTurnRange(int turn, bool kept)
    {
        var combat = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RELIC.ANCHOR" };
        var nonCombat = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cutoffs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["RELIC.ANCHOR"] = turn,
        };

        RelicClassificationStore.NormalizeClassifications(
            combat,
            nonCombat,
            cutoffs,
            new[] { "RELIC.ANCHOR" });

        Assert.Equal(kept, cutoffs.ContainsKey("RELIC.ANCHOR"));
    }

    [Fact]
    public void Normalization_DropsCutoffsThatNoLongerNameACombatRelic()
    {
        var combat = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nonCombat = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RELIC.ANCHOR" };
        var cutoffs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["RELIC.ANCHOR"] = 1,
            ["RELIC.CUT_FROM_THE_GAME"] = 2,
        };

        var changed = RelicClassificationStore.NormalizeClassifications(
            combat,
            nonCombat,
            cutoffs,
            new[] { "RELIC.ANCHOR" });

        Assert.True(changed);
        Assert.Empty(cutoffs);
    }

    /// <summary>
    /// A deferred pass runs against a document that is usually already correct.
    /// Reporting no change is what keeps it from rewriting the user's file on
    /// every cold start.
    /// </summary>
    [Fact]
    public void Normalization_ReportsNoChangeWhenTheDocumentAlreadyMatchesTheGame()
    {
        var combat = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RELIC.ANCHOR" };
        var nonCombat = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RELIC.FROZEN_EGG" };
        var cutoffs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["RELIC.ANCHOR"] = 1,
        };

        var changed = RelicClassificationStore.NormalizeClassifications(
            combat,
            nonCombat,
            cutoffs,
            new[] { "RELIC.ANCHOR", "RELIC.FROZEN_EGG" });

        Assert.False(changed);
        Assert.Equal("RELIC.ANCHOR", Assert.Single(combat));
        Assert.Equal("RELIC.FROZEN_EGG", Assert.Single(nonCombat));
        Assert.Equal(1, cutoffs["RELIC.ANCHOR"]);
    }

    [Fact]
    public void Normalization_MatchesRelicIdsCaseInsensitively()
    {
        var combat = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "relic.anchor" };
        var nonCombat = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cutoffs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["relic.anchor"] = 2,
        };

        var changed = RelicClassificationStore.NormalizeClassifications(
            combat,
            nonCombat,
            cutoffs,
            new[] { "RELIC.ANCHOR" });

        Assert.False(changed);
        Assert.Single(combat);
        Assert.Equal(2, cutoffs["RELIC.ANCHOR"]);
    }
}
