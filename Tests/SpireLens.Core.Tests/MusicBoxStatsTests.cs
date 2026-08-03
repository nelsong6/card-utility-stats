using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class MusicBoxStatsTests
{
    private const string MusicBoxRelicId = "RELIC.MUSIC_BOX";

    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildMusicBoxBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildMusicBoxBodyBBCode not found.");

    [Fact]
    public void Patches_TargetMusicBoxCreationAndEtherealExhaustCallbacks()
    {
        var afterCardPlayed = typeof(MusicBox).GetMethod(nameof(MusicBox.AfterCardPlayed));
        var addGenerated = typeof(CardPileCmd).GetMethod(
            nameof(CardPileCmd.AddGeneratedCardToCombat),
            new[]
            {
                typeof(CardModel),
                typeof(PileType),
                typeof(Player),
                typeof(CardPilePosition),
            });
        var afterCardExhausted = typeof(Hook).GetMethod(
            nameof(Hook.AfterCardExhausted),
            new[]
            {
                typeof(ICombatState),
                typeof(PlayerChoiceContext),
                typeof(CardModel),
                typeof(bool),
            });

        Assert.NotNull(afterCardPlayed);
        Assert.Equal(
            new[] { typeof(PlayerChoiceContext), typeof(CardPlay) },
            afterCardPlayed!.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.NotNull(addGenerated);
        Assert.NotNull(afterCardExhausted);
        Assert.NotNull(typeof(Hook).GetMethod(nameof(Hook.AfterPlayerTurnStart)));
    }

    [Fact]
    public void RelicAggregate_MusicBoxFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        AssertMusicBoxAggregate(agg, 0, 0, 0, 0, 0, 0, 0);
    }

    [Fact]
    public void RunTracker_MusicBoxHelpers_CountSuccessfulAttacksAndRequestedRarities()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordMusicBoxAttackCreatedForTest(agg, true, CardRarity.Common);
        RunTracker.RecordMusicBoxAttackCreatedForTest(agg, true, CardRarity.Uncommon);
        RunTracker.RecordMusicBoxAttackCreatedForTest(agg, true, CardRarity.Rare);
        RunTracker.RecordMusicBoxAttackCreatedForTest(agg, true, CardRarity.Basic);
        RunTracker.RecordMusicBoxAttackCreatedForTest(agg, false, CardRarity.Rare);
        RunTracker.RecordMusicBoxEtherealExhaustForTest(agg, 3);
        RunTracker.RecordMusicBoxEtherealExhaustForTest(agg, -1);
        RunTracker.RecordMusicBoxTurnForTest(agg, 8);
        RunTracker.RecordMusicBoxTurnForTest(agg, -1);
        RunTracker.RecordMusicBoxCombatForTest(agg, 3);
        RunTracker.RecordMusicBoxCombatForTest(agg, -1);

        AssertMusicBoxAggregate(agg, 4, 1, 1, 1, 3, 8, 3);
    }

    [Fact]
    public void RelicAggregate_MusicBoxFields_JsonRoundtripPreservesValues()
    {
        var run = new RunData();
        run.RelicAggregates[MusicBoxRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"music_box_attacks_created\"", json);
        Assert.Contains("\"music_box_common_attacks_created\"", json);
        Assert.Contains("\"music_box_uncommon_attacks_created\"", json);
        Assert.Contains("\"music_box_rare_attacks_created\"", json);
        Assert.Contains("\"music_box_attacks_exhausted_by_ethereal\"", json);
        Assert.Contains("\"music_box_turns\"", json);
        Assert.Contains("\"music_box_combats\"", json);
        Assert.NotNull(restored);
        AssertMusicBoxAggregate(
            restored!.RelicAggregates[MusicBoxRelicId],
            6,
            2,
            2,
            1,
            3,
            4,
            2);
    }

    [Fact]
    public void RelicAggregate_MusicBoxFields_Merge()
    {
        var target = PopulatedAggregate();

        RunTracker.MergeRelicAggregateInto(target, PopulatedAggregate());

        AssertMusicBoxAggregate(target, 12, 4, 4, 2, 6, 8, 4);
    }

    [Fact]
    public void RelicTooltip_MusicBox_ShowsRequestedTotalsAndAverages()
    {
        var body = BuildBody(PopulatedAggregate());

        Assert.Contains("Attacks created by Musical Box", body);
        Assert.Contains("Common Attacks created by Musical Box", body);
        Assert.Contains("Uncommon Attacks created by Musical Box", body);
        Assert.Contains("Rare Attacks created by Musical Box", body);
        Assert.Contains("Average Attacks created by Musical Box per turn", body);
        Assert.Contains("Average Attacks created by Musical Box per combat", body);
        Assert.Contains("exhausted because of Ethereal", body);
        Assert.Contains("[b]1.5[/b]", body);
        Assert.Contains("[b]3[/b]", body);
    }

    [Fact]
    public void RelicAggregate_OlderShapeWithoutMusicBoxFields_DefaultsToZero()
    {
        var agg = JsonSerializer.Deserialize<RelicAggregate>("{}", RunStorage.Options);

        Assert.NotNull(agg);
        AssertMusicBoxAggregate(agg!, 0, 0, 0, 0, 0, 0, 0);
    }

    private static RelicAggregate PopulatedAggregate()
        => new()
        {
            MusicBoxAttacksCreated = 6,
            MusicBoxCommonAttacksCreated = 2,
            MusicBoxUncommonAttacksCreated = 2,
            MusicBoxRareAttacksCreated = 1,
            MusicBoxAttacksExhaustedByEthereal = 3,
            MusicBoxTurns = 4,
            MusicBoxCombats = 2,
        };

    private static void AssertMusicBoxAggregate(
        RelicAggregate agg,
        int attacksCreated,
        int common,
        int uncommon,
        int rare,
        int exhaustedByEthereal,
        int turns,
        int combats)
    {
        Assert.Equal(attacksCreated, agg.MusicBoxAttacksCreated);
        Assert.Equal(common, agg.MusicBoxCommonAttacksCreated);
        Assert.Equal(uncommon, agg.MusicBoxUncommonAttacksCreated);
        Assert.Equal(rare, agg.MusicBoxRareAttacksCreated);
        Assert.Equal(exhaustedByEthereal, agg.MusicBoxAttacksExhaustedByEthereal);
        Assert.Equal(turns, agg.MusicBoxTurns);
        Assert.Equal(combats, agg.MusicBoxCombats);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildBodyMethod.Invoke(null, new object[] { agg })
                    ?? throw new InvalidOperationException("Builder returned null."));
}
