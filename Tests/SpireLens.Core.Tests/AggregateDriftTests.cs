using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

/// <summary>
/// Reflection-exhaustive drift guard for the aggregate merge/clone functions.
///
/// The recurring bug class in this codebase (the doubled relic Activations bug,
/// omitted-field-on-merge/clone/pool) comes from hand-maintained parallel field
/// lists. These tests populate EVERY field of each aggregate with a distinct
/// value and assert:
///   - Clone(src) == src                    (a clone omitting a field fails)
///   - Merge(new, src) == src               (a merge omitting a field leaves it
///                                            default; a merge adding a field
///                                            TWICE leaves it doubled — both fail)
/// so any field added to an aggregate but not to its merge/clone fails the day
/// it is added. Add new aggregate stat fields to the Merge*Into functions only;
/// the clones delegate to them.
/// </summary>
public class AggregateDriftTests
{
    // Per-test incrementing seed → every populated field gets a distinct value.
    // xUnit runs tests within a class sequentially, so a static seed is safe;
    // each test resets it.
    private int _seed;

    private object? MakeScalar(Type t)
    {
        _seed++;
        if (t == typeof(int) || t == typeof(int?)) return _seed;
        if (t == typeof(long) || t == typeof(long?)) return (long)_seed;
        if (t == typeof(decimal) || t == typeof(decimal?)) return _seed + 0.5m;
        if (t == typeof(bool) || t == typeof(bool?)) return true;
        if (t == typeof(string)) return "s" + _seed;
        return null; // unhandled type → left at default (e.g. SerializableCard)
    }

    private void Populate(object obj, ISet<string> skip)
    {
        foreach (var p in obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (skip.Contains(p.Name)) continue;
            var t = p.PropertyType;

            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var valType = t.GetGenericArguments()[1];
                var dict = (IDictionary?)p.GetValue(obj);
                if (dict == null)
                {
                    dict = (IDictionary)Activator.CreateInstance(t)!;
                    if (p.CanWrite) p.SetValue(obj, dict);
                }
                var inner = Activator.CreateInstance(valType)!;
                Populate(inner, skip);
                dict[$"k{++_seed}"] = inner;
                continue;
            }

            var v = MakeScalar(t);
            if (v != null && p.CanWrite) p.SetValue(obj, v);
        }
    }

    // Compare SCALAR accumulating fields only. The inner reason/status
    // dictionaries are deliberately excluded: their merge helpers legitimately
    // re-key by an inner id field (so an arbitrary test key wouldn't survive),
    // and they have their own dedicated bucket tests. The scalar fields are
    // where the drift class lives (the doubled-Activations bug was a scalar).
    private static string ScalarJson(object o)
    {
        foreach (var p in o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var t = p.PropertyType;
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                ((IDictionary?)p.GetValue(o))?.Clear();
        }
        return JsonSerializer.Serialize(o, RunStorage.Options);
    }

    // Lineage/identity fields a merge intentionally does NOT accumulate.
    private static readonly HashSet<string> CardLineage = new()
    {
        "FloorAdded", "InitialUpgradeLevel", "Removed", "RemovedAtFloor", "RemovedSnapshot",
    };
    // RemovedSnapshot is a game type we can't reflectively populate.
    private static readonly HashSet<string> Unconstructable = new() { "RemovedSnapshot" };

    [Fact]
    public void CloneAggregate_CopiesEveryCardAggregateField()
    {
        _seed = 0;
        var src = new CardAggregate();
        Populate(src, Unconstructable);

        var clone = RunTracker.CloneAggregate(src);

        Assert.Equal(ScalarJson(src), ScalarJson(clone));
    }

    [Fact]
    public void MergeAggregateInto_Empty_ReproducesEveryAccumulatingCardField()
    {
        _seed = 0;
        var src = new CardAggregate();
        Populate(src, CardLineage);

        var target = new CardAggregate();
        RunTracker.MergeAggregateInto(target, src);

        Assert.Equal(ScalarJson(src), ScalarJson(target));
    }

    [Fact]
    public void MergeRelicAggregateInto_Empty_ReproducesEveryRelicField()
    {
        _seed = 0;
        var src = new RelicAggregate();
        Populate(src, new HashSet<string>());

        var target = new RelicAggregate();
        RunTracker.MergeRelicAggregateInto(target, src);

        Assert.Equal(ScalarJson(src), ScalarJson(target));
    }

    [Fact]
    public void CloneEnemyAggregate_CopiesEveryEnemyField()
    {
        _seed = 0;
        var src = new EnemyAggregate();
        Populate(src, new HashSet<string>());

        var clone = RunTracker.CloneEnemyAggregate(src);

        Assert.Equal(ScalarJson(src), ScalarJson(clone));
    }

    [Fact]
    public void MergeEnemyAggregateInto_Empty_ReproducesEveryEnemyField()
    {
        _seed = 0;
        var src = new EnemyAggregate();
        Populate(src, new HashSet<string>());

        var target = new EnemyAggregate();
        RunTracker.MergeEnemyAggregateInto(target, src);

        Assert.Equal(ScalarJson(src), ScalarJson(target));
    }

    [Fact]
    public void RunStorageOptions_IncludeFields_SoPublicFieldsRoundTrip()
    {
        // Guards the RemovedSnapshot data-loss fix: the game's SerializableCard
        // stores data in public fields, which only serialize with IncludeFields.
        var probe = new FieldBackedProbe { Value = 7, Name = "keep" };
        var json = JsonSerializer.Serialize(probe, RunStorage.Options);
        Assert.Contains("\"value\"", json);
        Assert.Contains("\"name\"", json);
        var back = JsonSerializer.Deserialize<FieldBackedProbe>(json, RunStorage.Options)!;
        Assert.Equal(7, back.Value);
        Assert.Equal("keep", back.Name);
    }

    private sealed class FieldBackedProbe
    {
        public int Value;
        public string? Name;
    }
}
