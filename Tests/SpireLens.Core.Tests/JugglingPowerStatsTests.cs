using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class JugglingPowerStatsTests
{
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    public void NormalizeAttackCount_ShowsRawTurnProgressWithoutCappingAtTrigger(
        int attacksPlayedThisTurn,
        int expected)
    {
        Assert.Equal(
            expected,
            JugglingPowerDisplayAmountPatch.NormalizeAttackCount(attacksPlayedThisTurn));
    }
}
