using System.Reflection;
using CardUtilityStats.Core;
using Xunit;

namespace CardUtilityStats.Core.Tests;

public class RunTrackerAggregateTests
{
    private static readonly MethodInfo CloneAggregateMethod =
        typeof(RunTracker).GetMethod("CloneAggregate", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("CloneAggregate not found.");

    private static readonly MethodInfo MergeAggregateIntoMethod =
        typeof(RunTracker).GetMethod("MergeAggregateInto", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("MergeAggregateInto not found.");

    [Fact]
    public void CloneAggregate_CopiesForgeGenerated()
    {
        var source = new CardAggregate
        {
            TotalForgeGenerated = 9m,
        };

        var clone = (CardAggregate)(CloneAggregateMethod.Invoke(null, new object?[] { source })
            ?? throw new InvalidOperationException("CloneAggregate returned null."));

        Assert.Equal(9m, clone.TotalForgeGenerated);
    }

    [Fact]
    public void MergeAggregateInto_AddsForgeGenerated()
    {
        var target = new CardAggregate
        {
            TotalForgeGenerated = 5m,
        };
        var source = new CardAggregate
        {
            TotalForgeGenerated = 4m,
        };

        _ = MergeAggregateIntoMethod.Invoke(null, new object?[] { target, source });

        Assert.Equal(9m, target.TotalForgeGenerated);
    }
}
