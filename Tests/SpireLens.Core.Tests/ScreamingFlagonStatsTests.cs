using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class ScreamingFlagonStatsTests
{
    private const string ScreamingFlagonRelicId = "RELIC.SCREAMING_FLAGON";

    private static readonly MethodInfo BuildBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildScreamingFlagonBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildScreamingFlagonBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void HarmonyTarget_BeforeSideTurnEnd_ReturnsTask()
    {
        var method = typeof(ScreamingFlagon).GetMethod(
            nameof(ScreamingFlagon.BeforeSideTurnEnd),
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            [typeof(PlayerChoiceContext), typeof(CombatSide), typeof(IEnumerable<Creature>)],
            modifiers: null);

        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);
    }

    [Fact]
    public void RelicAggregate_ScreamingFlagonDamageStats_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[ScreamingFlagonRelicId] = new RelicAggregate
        {
            Activations = 3,
            TotalDamageAttempted = 72,
            TotalDamageDealt = 55,
            TotalDamageBlocked = 9,
            TotalDamageOverkill = 8,
            TotalTargets = 5,
            Kills = 2,
            ScreamingFlagonTurnEndHandSizeTotal = 13,
            ScreamingFlagonTurns = 5,
            ScreamingFlagonCombats = 2,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);
        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[ScreamingFlagonRelicId];
        Assert.Equal(3, agg.Activations);
        Assert.Equal(72, agg.TotalDamageAttempted);
        Assert.Equal(55, agg.TotalDamageDealt);
        Assert.Equal(9, agg.TotalDamageBlocked);
        Assert.Equal(8, agg.TotalDamageOverkill);
        Assert.Equal(5, agg.TotalTargets);
        Assert.Equal(2, agg.Kills);
        Assert.Equal(13, agg.ScreamingFlagonTurnEndHandSizeTotal);
        Assert.Equal(5, agg.ScreamingFlagonTurns);
        Assert.Equal(2, agg.ScreamingFlagonCombats);
    }

    [Fact]
    public void RelicAggregate_ScreamingFlagonTurnEndHandSizes_IncludeEmptyHands()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordScreamingFlagonTurnForTest(agg, 4);
        RunTracker.RecordScreamingFlagonTurnForTest(agg, 0);
        RunTracker.RecordScreamingFlagonTurnForTest(agg, 1);
        RunTracker.RecordScreamingFlagonCombatForTest(agg, 2);

        Assert.Equal(5, agg.ScreamingFlagonTurnEndHandSizeTotal);
        Assert.Equal(3, agg.ScreamingFlagonTurns);
        Assert.Equal(2, agg.ScreamingFlagonCombats);
    }

    [Fact]
    public void MergeRelicAggregateInto_ScreamingFlagonHandSizeFields_Accumulate()
    {
        var target = new RelicAggregate
        {
            ScreamingFlagonTurnEndHandSizeTotal = 5,
            ScreamingFlagonTurns = 2,
            ScreamingFlagonCombats = 1,
        };
        var source = new RelicAggregate
        {
            ScreamingFlagonTurnEndHandSizeTotal = 8,
            ScreamingFlagonTurns = 3,
            ScreamingFlagonCombats = 1,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(13, target.ScreamingFlagonTurnEndHandSizeTotal);
        Assert.Equal(5, target.ScreamingFlagonTurns);
        Assert.Equal(2, target.ScreamingFlagonCombats);
    }

    [Fact]
    public void RelicAggregate_ScreamingFlagonDamageRecording_SplitsObservedDamage()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordScreamingFlagonDamageForTest(
            agg,
            [
                (BlockedDamage: 4, UnblockedDamage: 12, OverkillDamage: 0, WasTargetKilled: false),
                (BlockedDamage: 0, UnblockedDamage: 7, OverkillDamage: 5, WasTargetKilled: true),
            ]);

        Assert.Equal(28, agg.TotalDamageAttempted);
        Assert.Equal(19, agg.TotalDamageDealt);
        Assert.Equal(4, agg.TotalDamageBlocked);
        Assert.Equal(5, agg.TotalDamageOverkill);
        Assert.Equal(2, agg.TotalTargets);
        Assert.Equal(1, agg.Kills);
    }

    [Fact]
    public void RelicTooltip_ScreamingFlagon_ShowsAoeDamageRows()
    {
        var body = BuildBody(new RelicAggregate
        {
            Activations = 3,
            TotalDamageAttempted = 72,
            TotalDamageDealt = 55,
            TotalDamageBlocked = 9,
            TotalDamageOverkill = 8,
            TotalTargets = 5,
            Kills = 2,
            ScreamingFlagonTurnEndHandSizeTotal = 13,
            ScreamingFlagonTurns = 5,
            ScreamingFlagonCombats = 2,
        });

        Assert.Contains("Activations", body);
        Assert.Contains("Damage attempted", body);
        Assert.Contains("Damage dealt", body);
        Assert.Contains("Damage blocked", body);
        Assert.Contains("Overkill", body);
        Assert.Contains("Kills", body);
        Assert.Contains("Targets hit", body);
        Assert.Contains("Damage per activation", body);
        Assert.Contains("[b]18.33[/b]", body);
        Assert.Contains("turn end per turn", body);
        Assert.Contains("turn end per combat", body);
        Assert.Contains("[b]2.6[/b]", body);
        Assert.Contains("[b]6.5[/b]", body);
    }

    [Fact]
    public void RelicTooltip_ScreamingFlagonModelRecognition_UsesGameRelicId()
    {
        var relic = (ScreamingFlagon)RuntimeHelpers.GetUninitializedObject(typeof(ScreamingFlagon));

        Assert.Equal(ScreamingFlagonRelicId, RelicHoverShowPatch.GetStatsAggregateId(relic));

        var recognized = RelicHoverShowPatch.TryBuildBodyBBCode(
            relic,
            new RelicAggregate(),
            floorCount: null,
            bloodSoakedRoseCurseAgg: null,
            cursedPearlCurseAgg: null,
            neowsBonesCurseAggs: null,
            storybookBrightestFlameAgg: null,
            out var title,
            out _);

        Assert.True(recognized);
        Assert.Equal("Screaming Flagon", title);
    }

    private static string BuildBody(RelicAggregate agg)
        => (string)(BuildBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildScreamingFlagonBodyBBCode returned null."));
}
