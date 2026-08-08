using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

public class CardAggregatePoolerTests
{
    [Fact]
    public void PoolByDefinition_MergesOnlyMatchingShivInstances()
    {
        var first = new CardAggregate
        {
            CombatsInDeck = 2,
            Plays = 1,
            TotalEffective = 4,
            TotalBlocked = 1,
            TimesExhausted = 1,
            TotalOrbsCreated = 2,
            TimesCardsDrawAttempted = 3,
            TimesCardsDrawBlocked = 2,
            TotalOstyHpAttackBonus = 7,
            TimesOstyHpAttackBonusApplied = 1,
            TimesOstySummoned = 1,
            TotalOstyHpSummoned = 8m,
            TimesReplayExtraPlanned = 2,
            TimesReplayExtraPlayed = 1,
            TimesReplayAttackNoDamage = 1,
        };
        first.OrbOutcomes["ORB.LIGHTNING_ORB"] = new CardOrbAggregate
        {
            OrbId = "ORB.LIGHTNING_ORB",
            Created = 2,
            PassiveActivations = 4,
            Evokes = 1,
            DamageAttempted = 10,
            DamageDealt = 8,
            DamageBlocked = 1,
            DamageOverkill = 1,
            Kills = 1,
            TargetsHit = 2,
        };
        first.AppliedEffects["POWER.ARTIFACT"] = new AppliedEffectAggregate
        {
            EffectId = "POWER.ARTIFACT",
            DisplayName = "Artifact",
            TimesBlockedByArtifact = 1,
            TotalAmountBlockedByArtifact = 1m,
            TotalTriggeredCardsDrawBlocked = 2,
        };
        first.BlockedDrawReasons["effect:POWER.NO_DRAW"] = new BlockedDrawReasonAggregate
        {
            ReasonId = "effect:POWER.NO_DRAW",
            DisplayName = "No Draw",
            Count = 2,
        };
        first.ReplayExtraPlayReasons["replay"] = new ReplayExtraPlayReasonAggregate
        {
            ReasonId = "replay",
            DisplayName = "Replay",
            Count = 1,
        };
        first.ReplayExtraPlayPlannedReasons["replay"] = new ReplayExtraPlayReasonAggregate
        {
            ReasonId = "replay",
            DisplayName = "Replay",
            Count = 2,
        };
        first.ReplayAttackNoDamageReasons["replay"] = new ReplayExtraPlayReasonAggregate
        {
            ReasonId = "replay",
            DisplayName = "Replay",
            Count = 1,
        };

        var second = new CardAggregate
        {
            CombatsInDeck = 3,
            Plays = 2,
            TotalEffective = 11,
            TotalBlocked = 2,
            TimesExhausted = 2,
            TotalOrbsCreated = 3,
            TimesCardsDrawAttempted = 1,
            TimesCardsDrawBlocked = 1,
            TotalOstyHpAttackBonus = 11,
            TimesOstyHpAttackBonusApplied = 2,
            TimesOstySummoned = 2,
            TotalOstyHpSummoned = 12m,
            TimesReplayExtraPlanned = 3,
            TimesReplayExtraPlayed = 3,
            TimesReplayAttackNoDamage = 2,
        };
        second.OrbOutcomes["ORB.LIGHTNING_ORB"] = new CardOrbAggregate
        {
            OrbId = "ORB.LIGHTNING_ORB",
            Created = 3,
            PassiveActivations = 6,
            Evokes = 2,
            Fizzles = 1,
            DamageAttempted = 20,
            DamageDealt = 15,
            DamageBlocked = 3,
            DamageOverkill = 2,
            Kills = 2,
            TargetsHit = 4,
        };
        second.AppliedEffects["POWER.VULNERABLE"] = new AppliedEffectAggregate
        {
            EffectId = "POWER.VULNERABLE",
            DisplayName = "Vulnerable",
            TimesApplied = 2,
            TotalAmountApplied = 4m,
        };
        second.BlockedDrawReasons["full_hand"] = new BlockedDrawReasonAggregate
        {
            ReasonId = "full_hand",
            DisplayName = "Hand full",
            Count = 1,
        };
        second.ReplayExtraPlayReasons["power:POWER.BURST"] = new ReplayExtraPlayReasonAggregate
        {
            ReasonId = "power:POWER.BURST",
            DisplayName = "Burst",
            Count = 3,
        };
        second.ReplayExtraPlayPlannedReasons["power:POWER.BURST"] = new ReplayExtraPlayReasonAggregate
        {
            ReasonId = "power:POWER.BURST",
            DisplayName = "Burst",
            Count = 3,
        };
        second.ReplayAttackNoDamageReasons["power:POWER.BURST"] = new ReplayExtraPlayReasonAggregate
        {
            ReasonId = "power:POWER.BURST",
            DisplayName = "Burst",
            Count = 2,
        };

        var otherDefinition = new CardAggregate
        {
            CombatsInDeck = 99,
            Plays = 99,
            TotalEffective = 999,
            TimesExhausted = 99,
        };

        var pooled = CardAggregatePooler.PoolByDefinition(
            new[]
            {
                new KeyValuePair<string, CardAggregate>("CARD.SHIV#1", first),
                new KeyValuePair<string, CardAggregate>("CARD.SHIV#2", second),
                new KeyValuePair<string, CardAggregate>("CARD.STRIKE_SILENT#1", otherDefinition),
            },
            "CARD.SHIV");

        Assert.NotNull(pooled);
        Assert.Equal(5, pooled!.CombatsInDeck);
        Assert.Equal(3, pooled.Plays);
        Assert.Equal(15, pooled.TotalEffective);
        Assert.Equal(3, pooled.TotalBlocked);
        Assert.Equal(3, pooled.TimesExhausted);
        Assert.Equal(5, pooled.TotalOrbsCreated);
        Assert.Equal(5, pooled.OrbOutcomes["ORB.LIGHTNING_ORB"].Created);
        Assert.Equal(10, pooled.OrbOutcomes["ORB.LIGHTNING_ORB"].PassiveActivations);
        Assert.Equal(3, pooled.OrbOutcomes["ORB.LIGHTNING_ORB"].Evokes);
        Assert.Equal(1, pooled.OrbOutcomes["ORB.LIGHTNING_ORB"].Fizzles);
        Assert.Equal(30, pooled.OrbOutcomes["ORB.LIGHTNING_ORB"].DamageAttempted);
        Assert.Equal(23, pooled.OrbOutcomes["ORB.LIGHTNING_ORB"].DamageDealt);
        Assert.Equal(4, pooled.OrbOutcomes["ORB.LIGHTNING_ORB"].DamageBlocked);
        Assert.Equal(3, pooled.OrbOutcomes["ORB.LIGHTNING_ORB"].DamageOverkill);
        Assert.Equal(3, pooled.OrbOutcomes["ORB.LIGHTNING_ORB"].Kills);
        Assert.Equal(6, pooled.OrbOutcomes["ORB.LIGHTNING_ORB"].TargetsHit);
        Assert.Equal(4, pooled.TimesCardsDrawAttempted);
        Assert.Equal(3, pooled.TimesCardsDrawBlocked);
        Assert.Equal(18, pooled.TotalOstyHpAttackBonus);
        Assert.Equal(3, pooled.TimesOstyHpAttackBonusApplied);
        Assert.Equal(3, pooled.TimesOstySummoned);
        Assert.Equal(20m, pooled.TotalOstyHpSummoned);
        Assert.Equal(5, pooled.TimesReplayExtraPlanned);
        Assert.Equal(4, pooled.TimesReplayExtraPlayed);
        Assert.Equal(3, pooled.TimesReplayAttackNoDamage);
        Assert.Equal(2, pooled.ReplayExtraPlayPlannedReasons["replay"].Count);
        Assert.Equal(3, pooled.ReplayExtraPlayPlannedReasons["power:POWER.BURST"].Count);
        Assert.Equal(1, pooled.ReplayExtraPlayReasons["replay"].Count);
        Assert.Equal(3, pooled.ReplayExtraPlayReasons["power:POWER.BURST"].Count);
        Assert.Equal(1, pooled.ReplayAttackNoDamageReasons["replay"].Count);
        Assert.Equal(2, pooled.ReplayAttackNoDamageReasons["power:POWER.BURST"].Count);
        Assert.Equal(2, pooled.BlockedDrawReasons["effect:POWER.NO_DRAW"].Count);
        Assert.Equal(1, pooled.BlockedDrawReasons["full_hand"].Count);

        var artifact = pooled.AppliedEffects["POWER.ARTIFACT"];
        Assert.Equal(1, artifact.TimesBlockedByArtifact);
        Assert.Equal(1m, artifact.TotalAmountBlockedByArtifact);
        Assert.Equal(2, artifact.TotalTriggeredCardsDrawBlocked);

        var vulnerable = pooled.AppliedEffects["POWER.VULNERABLE"];
        Assert.Equal(2, vulnerable.TimesApplied);
        Assert.Equal(4m, vulnerable.TotalAmountApplied);
    }

    [Fact]
    public void PoolByDefinition_MergesOnlyMatchingSovereignBladeInstances()
    {
        var first = new CardAggregate
        {
            Plays = 1,
            TotalEffective = 6,
            TotalBlocked = 2,
            TimesCardsDrawn = 1,
            TotalForgeGenerated = 2m,
        };
        first.AppliedEffects["POWER.FORGE"] = new AppliedEffectAggregate
        {
            EffectId = "POWER.FORGE",
            DisplayName = "Forge",
            TimesApplied = 1,
            TotalAmountApplied = 2m,
        };

        var second = new CardAggregate
        {
            Plays = 2,
            TotalEffective = 9,
            TotalBlocked = 1,
            TimesCardsDrawn = 3,
            TotalForgeGenerated = 5m,
        };
        second.AppliedEffects["POWER.FORGE"] = new AppliedEffectAggregate
        {
            EffectId = "POWER.FORGE",
            DisplayName = "Forge",
            TimesApplied = 2,
            TotalAmountApplied = 5m,
        };
        second.AppliedEffects["POWER.BLADE"] = new AppliedEffectAggregate
        {
            EffectId = "POWER.BLADE",
            DisplayName = "Blade",
            TimesApplied = 1,
            TotalAmountApplied = 1m,
        };

        var otherDefinition = new CardAggregate
        {
            Plays = 99,
            TotalEffective = 999,
            TimesCardsDrawn = 99,
        };

        var pooled = CardAggregatePooler.PoolByDefinition(
            new[]
            {
                new KeyValuePair<string, CardAggregate>("CARD.SOVEREIGN_BLADE#1", first),
                new KeyValuePair<string, CardAggregate>("CARD.SOVEREIGN_BLADE#2", second),
                new KeyValuePair<string, CardAggregate>("CARD.STRIKE_KIN#1", otherDefinition),
            },
            "CARD.SOVEREIGN_BLADE");

        Assert.NotNull(pooled);
        Assert.Equal(3, pooled!.Plays);
        Assert.Equal(15, pooled.TotalEffective);
        Assert.Equal(3, pooled.TotalBlocked);
        Assert.Equal(4, pooled.TimesCardsDrawn);
        Assert.Equal(7m, pooled.TotalForgeGenerated);

        var forge = pooled.AppliedEffects["POWER.FORGE"];
        Assert.Equal(3, forge.TimesApplied);
        Assert.Equal(7m, forge.TotalAmountApplied);

        var blade = pooled.AppliedEffects["POWER.BLADE"];
        Assert.Equal(1, blade.TimesApplied);
        Assert.Equal(1m, blade.TotalAmountApplied);
    }

    [Fact]
    public void PoolByDefinition_ReturnsNullWhenDefinitionIsAbsent()
    {
        var pooled = CardAggregatePooler.PoolByDefinition(
            new[]
            {
                new KeyValuePair<string, CardAggregate>("CARD.STRIKE_SILENT#1", new CardAggregate()),
            },
            "CARD.SHIV");

        Assert.Null(pooled);
    }
}
