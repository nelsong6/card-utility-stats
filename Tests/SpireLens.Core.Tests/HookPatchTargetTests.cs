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

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void CentennialPuzzlePatch_ResolvesAfterDamageReceived()
    {
        var target = InvokeTargetMethod(typeof(CentennialPuzzleAfterDamageReceivedPatch));

        Assert.NotNull(target);
        Assert.Equal("MegaCrit.Sts2.Core.Models.Relics.CentennialPuzzle", target!.DeclaringType?.FullName);
        Assert.Equal("AfterDamageReceived", target.Name);
        Assert.Equal(
            new[]
            {
                "MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext",
                "MegaCrit.Sts2.Core.Entities.Creatures.Creature",
                "MegaCrit.Sts2.Core.Entities.Creatures.DamageResult",
                "MegaCrit.Sts2.Core.ValueProps.ValueProp",
                "MegaCrit.Sts2.Core.Entities.Creatures.Creature",
                "MegaCrit.Sts2.Core.Models.CardModel",
            },
            target.GetParameters().Select(p => p.ParameterType.FullName).ToArray());
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void CentennialPuzzleDrawPatch_ResolvesSingleCardDraw()
    {
        var target = InvokeTargetMethod(typeof(CentennialPuzzleCardPileDrawPatch));

        Assert.NotNull(target);
        Assert.Equal("MegaCrit.Sts2.Core.Commands.CardPileCmd", target!.DeclaringType?.FullName);
        Assert.Equal("Draw", target.Name);
        Assert.Equal(
            new[]
            {
                "MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext",
                "MegaCrit.Sts2.Core.Entities.Players.Player",
            },
            target.GetParameters().Select(p => p.ParameterType.FullName).ToArray());
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void PenNibTurnEndPatch_ResolvesBeforeSideTurnEnd()
    {
        var target = InvokeTargetMethod(typeof(HookBeforeSideTurnEndPenNibPatch));

        Assert.NotNull(target);
        Assert.Equal("MegaCrit.Sts2.Core.Hooks.Hook", target!.DeclaringType?.FullName);
        Assert.Contains(target.Name, new[] { "BeforeSideTurnEnd", "BeforeTurnEnd" });

        var sideParameter = target.GetParameters().SingleOrDefault(p => p.Name == "side");
        Assert.NotNull(sideParameter);
        Assert.Equal("MegaCrit.Sts2.Core.Combat.CombatSide", sideParameter!.ParameterType.FullName);
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void LetterOpenerTurnEndPatch_ResolvesBeforeSideTurnEnd()
    {
        var target = InvokeTargetMethod(typeof(HookBeforeSideTurnEndLetterOpenerPatch));

        Assert.NotNull(target);
        Assert.Equal("MegaCrit.Sts2.Core.Hooks.Hook", target!.DeclaringType?.FullName);
        Assert.Contains(target.Name, new[] { "BeforeSideTurnEnd", "BeforeTurnEnd" });
        Assert.Equal(
            new[]
            {
                "combatState",
                "side",
                "participants",
            },
            target.GetParameters().Select(p => p.Name).ToArray());

        var sideParameter = target.GetParameters().SingleOrDefault(p => p.Name == "side");
        Assert.NotNull(sideParameter);
        Assert.Equal("MegaCrit.Sts2.Core.Combat.CombatSide", sideParameter!.ParameterType.FullName);
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void LetterOpenerTurnStartFallbackPatch_HasExactPrefixShape()
    {
        var prefix = typeof(LetterOpenerAfterSideTurnStartPatch).GetMethod("Prefix");

        Assert.NotNull(prefix);
        var parameters = prefix!.GetParameters();
        Assert.Equal(
            new[]
            {
                "MegaCrit.Sts2.Core.Models.Relics.LetterOpener",
                "MegaCrit.Sts2.Core.Combat.CombatSide",
                "System.Collections.Generic.IReadOnlyList`1",
                "MegaCrit.Sts2.Core.Combat.ICombatState",
            },
            parameters.Select(p => p.ParameterType.IsGenericType
                ? p.ParameterType.GetGenericTypeDefinition().FullName
                : p.ParameterType.FullName).ToArray());
        Assert.Equal(
            "MegaCrit.Sts2.Core.Entities.Creatures.Creature",
            parameters[2].ParameterType.GenericTypeArguments.Single().FullName);
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void TurnEnergyRelicsTurnEndPatch_ResolvesBeforeSideTurnEnd()
    {
        var target = InvokeTargetMethod(typeof(HookBeforeSideTurnEndTurnEnergyRelicsPatch));

        Assert.NotNull(target);
        Assert.Equal("MegaCrit.Sts2.Core.Hooks.Hook", target!.DeclaringType?.FullName);
        Assert.Contains(target.Name, new[] { "BeforeSideTurnEnd", "BeforeTurnEnd" });

        var sideParameter = target.GetParameters().SingleOrDefault(p => p.Name == "side");
        Assert.NotNull(sideParameter);
        Assert.Equal("MegaCrit.Sts2.Core.Combat.CombatSide", sideParameter!.ParameterType.FullName);
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void RegalPillowModifyHealPatch_ResolvesRestSiteHealModifier()
    {
        var target = InvokeTargetMethod(typeof(RegalPillowModifyRestSiteHealAmountPatch));

        Assert.NotNull(target);
        Assert.Equal("MegaCrit.Sts2.Core.Models.Relics.RegalPillow", target!.DeclaringType?.FullName);
        Assert.Equal("ModifyRestSiteHealAmount", target.Name);
        Assert.Equal(
            new[]
            {
                "MegaCrit.Sts2.Core.Entities.Creatures.Creature",
                "System.Decimal",
            },
            target.GetParameters().Select(p => p.ParameterType.FullName).ToArray());
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void RegalPillowAfterRestHealPatch_ResolvesAfterRestSiteHeal()
    {
        var target = InvokeTargetMethod(typeof(RegalPillowAfterRestSiteHealPatch));

        Assert.NotNull(target);
        Assert.Equal("MegaCrit.Sts2.Core.Models.Relics.RegalPillow", target!.DeclaringType?.FullName);
        Assert.Equal("AfterRestSiteHeal", target.Name);
        Assert.Equal(
            new[]
            {
                "MegaCrit.Sts2.Core.Entities.Players.Player",
                "System.Boolean",
            },
            target.GetParameters().Select(p => p.ParameterType.FullName).ToArray());
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void UnsettlingLampPatch_ResolvesModifyPowerAmountGivenMultiplicative()
    {
        var target = InvokeTargetMethod(typeof(UnsettlingLampModifyPowerAmountGivenPatch));

        Assert.NotNull(target);
        Assert.Equal("MegaCrit.Sts2.Core.Models.Relics.UnsettlingLamp", target!.DeclaringType?.FullName);
        Assert.Equal("ModifyPowerAmountGivenMultiplicative", target.Name);
        Assert.Equal(
            new[]
            {
                "MegaCrit.Sts2.Core.Models.PowerModel",
                "MegaCrit.Sts2.Core.Entities.Creatures.Creature",
                "System.Decimal",
                "MegaCrit.Sts2.Core.Entities.Creatures.Creature",
                "MegaCrit.Sts2.Core.Models.CardModel",
            },
            target.GetParameters().Select(p => p.ParameterType.FullName).ToArray());
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void FragrantMushroomPatch_ResolvesAfterObtained()
    {
        var target = InvokeTargetMethod(typeof(FragrantMushroomAfterObtainedPatch));

        Assert.NotNull(target);
        Assert.Equal("MegaCrit.Sts2.Core.Models.Relics.FragrantMushroom", target!.DeclaringType?.FullName);
        Assert.Equal("AfterObtained", target.Name);
        Assert.Empty(target.GetParameters());
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void ArcaneScrollPatch_ResolvesAfterObtained()
    {
        var target = InvokeTargetMethod(typeof(ArcaneScrollAfterObtainedPatch));

        Assert.NotNull(target);
        Assert.Equal("MegaCrit.Sts2.Core.Models.Relics.ArcaneScroll", target!.DeclaringType?.FullName);
        Assert.Equal("AfterObtained", target.Name);
        Assert.Empty(target.GetParameters());
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void LargeCapsulePatch_ResolvesAfterObtained()
    {
        var target = InvokeTargetMethod(typeof(LargeCapsuleAfterObtainedPatch));

        Assert.NotNull(target);
        Assert.Equal("MegaCrit.Sts2.Core.Models.Relics.LargeCapsule", target!.DeclaringType?.FullName);
        Assert.Equal("AfterObtained", target.Name);
        Assert.Empty(target.GetParameters());
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void NeowsBonesPatch_ResolvesAfterObtained()
    {
        var target = InvokeTargetMethod(typeof(NeowsBonesAfterObtainedPatch));

        Assert.NotNull(target);
        Assert.Equal("MegaCrit.Sts2.Core.Models.Relics.NeowsBones", target!.DeclaringType?.FullName);
        Assert.Equal("AfterObtained", target.Name);
        Assert.Empty(target.GetParameters());
    }

    private static MethodBase? InvokeTargetMethod(Type patchType)
    {
        _ = Assembly.Load("sts2");
        var targetMethod = patchType.GetMethod("TargetMethod", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(targetMethod);
        return (MethodBase?)targetMethod!.Invoke(null, Array.Empty<object>());
    }
}
