using System;
using System.Linq;
using System.Reflection;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class HookPatchTargetTests
{
    [Theory]
    [Trait("Category", "RequiresLiveGame")]
    [InlineData(typeof(HookAfterSideTurnEndCloakClaspCleanupPatch))]
    [InlineData(typeof(HookAfterSideTurnEndOrichalcumCleanupPatch))]
    public void EndTurnCleanupPatches_ResolveHookWithSideParameter(Type patchType)
    {
        var target = InvokeTargetMethod(patchType);

        Assert.NotNull(target);
        Assert.Equal("MegaCrit.Sts2.Core.Hooks.Hook", target!.DeclaringType?.FullName);
        Assert.Contains(target.Name, new[] { "AfterSideTurnEnd", "AfterTurnEnd" });

        var sideParameter = target.GetParameters().SingleOrDefault(p => p.Name == "side");
        Assert.NotNull(sideParameter);
        Assert.Equal("MegaCrit.Sts2.Core.Combat.CombatSide", sideParameter!.ParameterType.FullName);
    }

    [Theory]
    [Trait("Category", "RequiresLiveGame")]
    [InlineData(typeof(AnchorBeforeCombatStartPatch), "MegaCrit.Sts2.Core.Models.Relics.Anchor")]
    [InlineData(typeof(FakeAnchorBeforeCombatStartPatch), "MegaCrit.Sts2.Core.Models.Relics.FakeAnchor")]
    public void AnchorCombatStartPatches_ResolveBeforeCombatStart(Type patchType, string declaringTypeName)
    {
        var target = InvokeTargetMethod(patchType);

        Assert.NotNull(target);
        Assert.Equal(declaringTypeName, target!.DeclaringType?.FullName);
        Assert.Equal("BeforeCombatStart", target.Name);
        Assert.Empty(target.GetParameters());
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void GamblingChipPatch_ResolvesAfterPlayerTurnStart()
    {
        var target = InvokeTargetMethod(typeof(GamblingChipAfterPlayerTurnStartPatch));

        Assert.NotNull(target);
        Assert.Equal("MegaCrit.Sts2.Core.Models.Relics.GamblingChip", target!.DeclaringType?.FullName);
        Assert.Equal("AfterPlayerTurnStart", target.Name);
        Assert.Equal(
            new[]
            {
                "MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext",
                "MegaCrit.Sts2.Core.Entities.Players.Player",
            },
            target.GetParameters().Select(p => p.ParameterType.FullName).ToArray());
    }

    private static MethodBase? InvokeTargetMethod(Type patchType)
    {
        _ = Assembly.Load("sts2");
        var targetMethod = patchType.GetMethod("TargetMethod", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(targetMethod);
        return (MethodBase?)targetMethod!.Invoke(null, Array.Empty<object>());
    }
}
