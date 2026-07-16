using System;
using System.Collections.Generic;

namespace SpireLens.Core;

internal static class CardAggregatePooler
{
    public static bool IsAggregateForDefinition(string aggregateKey, string definitionId)
    {
        return aggregateKey.StartsWith(definitionId, StringComparison.Ordinal)
            && aggregateKey.Length > definitionId.Length
            && aggregateKey[definitionId.Length] == '#';
    }

    public static CardAggregate? PoolByDefinition(
        IEnumerable<KeyValuePair<string, CardAggregate>> aggregates,
        string definitionId)
    {
        CardAggregate? pooled = null;

        foreach (var (aggregateKey, aggregate) in aggregates)
        {
            if (!IsAggregateForDefinition(aggregateKey, definitionId)) continue;
            pooled ??= new CardAggregate();
            MergeInto(pooled, aggregate);
        }

        return pooled;
    }

    // Pooling and pending/committed projections must accumulate exactly the
    // same fields as normal combat promotion. Keep one authoritative field
    // list so newly added card stats cannot silently disappear from pooled UI.
    public static void MergeInto(CardAggregate target, CardAggregate source) =>
        RunTracker.MergeAggregateInto(target, source);
}
