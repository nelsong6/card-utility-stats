using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

public class SchemaLoadingTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string FixturePath(string fileName) =>
        Path.Combine(RepoRoot, "Fixtures", "RunSchema", fileName);

    [Fact]
    public void HistoricalLoad_AcceptsLegacyV1Fixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v1-pooled-run.json"));

        Assert.NotNull(loaded);
        Assert.False(loaded!.SupportsResume);
        Assert.False(loaded.HasPerInstanceIdentity);
        Assert.Contains("historical data", loaded.CompatibilityNote!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CARD.STRIKE_KIN", loaded.Data.Aggregates.Keys);
    }

    [Fact]
    public void HistoricalLoad_AcceptsLegacyResumableV2Fixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v2-per-instance-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Contains("CARD.STRIKE_KIN#1", loaded.Data.Aggregates.Keys);
        Assert.Equal(1, loaded.Data.DefCounters["CARD.STRIKE_KIN"]);
    }

    [Fact]
    public void HistoricalLoad_AcceptsLegacyResumableV3Fixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v3-per-instance-effects-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        var agg = loaded.Data.Aggregates["CARD.NECROBINDER_POWER#1"];
        var effect = agg.AppliedEffects["POWER.NECROBINDER_TRIGGER"];
        Assert.Equal("Necrobinder Trigger", effect.DisplayName);
        Assert.Equal(3, effect.TimesApplied);
    }

    [Fact]
    public void HistoricalLoad_AcceptsLegacyResumableV4Fixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v4-per-instance-effects-exhaust-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        var agg = loaded.Data.Aggregates["CARD.NECROBINDER_POWER#1"];
        Assert.Equal(1, agg.TimesExhausted);
        Assert.Equal(9m, agg.AppliedEffects["POWER.NECROBINDER_TRIGGER"].TotalAmountApplied);
    }

    [Fact]
    public void HistoricalLoad_AcceptsLegacyResumableV5Fixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v5-per-instance-block-ledger-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        var agg = loaded.Data.Aggregates["CARD.DEFEND_KIN#1"];
        Assert.Equal(6, agg.TotalBlockEffective);
        Assert.Equal(4, agg.TotalBlockWasted);
    }

    [Fact]
    public void HistoricalLoad_AcceptsLegacyResumableV6Fixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v6-per-instance-artifact-block-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Equal(6, loaded.Data.Aggregates["CARD.DEFEND_KIN#1"].TotalBlockEffective);
        var effect = loaded.Data.Aggregates["CARD.BASH_KIN#1"].AppliedEffects["POWER.WEAK"];
        Assert.Equal(1, effect.TimesBlockedByArtifact);
        Assert.Equal(2m, effect.TotalAmountBlockedByArtifact);
    }

    [Fact]
    public void HistoricalLoad_AcceptsLegacyResumableV7Fixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v7-per-instance-poison-damage-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        var effect = loaded.Data.Aggregates["CARD.DEADLY_POISON#1"].AppliedEffects["POWER.POISON"];
        Assert.Equal(9m, effect.TotalTriggeredEffectiveDamage);
        Assert.Equal(3m, effect.TotalTriggeredOverkill);
    }

    [Fact]
    public void HistoricalLoad_AcceptsLegacyResumableV8Fixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v8-per-instance-regent-stars-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Equal(2, loaded.Data.Aggregates["CARD.VENERATE#1"].TotalStarsGenerated);
        Assert.Equal(2, loaded.Data.Aggregates["CARD.STARDUST#1"].TotalStarsSpent);
        Assert.Equal(2, loaded.Data.Events[1].StarsSpent);
    }

    [Fact]
    public void HistoricalLoad_AcceptsLegacyResumableV9BlockedDrawFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v9-per-instance-blocked-draw-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Equal(2, loaded.Data.Aggregates["CARD.POMMEL_STRIKE#1"].TimesCardsDrawBlocked);
        Assert.Equal(1, loaded.Data.Aggregates["CARD.POMMEL_STRIKE#1"].TimesCardsDrawn);
        Assert.Equal(0, loaded.Data.Aggregates["CARD.POMMEL_STRIKE#1"].TotalStarsGenerated);
    }

    [Fact]
    public void HistoricalLoad_AcceptsLegacyResumableV9ForgeFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v9-per-instance-forge-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Equal(9m, loaded.Data.Aggregates["CARD.REFINE_BLADE#1"].TotalForgeGenerated);
        Assert.Equal(5m, loaded.Data.Events[0].ForgeGained);
    }

    [Fact]
    public void HistoricalLoad_AcceptsLegacyResumableV10ForgeFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v10-per-instance-forge-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Equal(9m, loaded.Data.Aggregates["CARD.REFINE_BLADE#1"].TotalForgeGenerated);
        Assert.Equal(0, loaded.Data.Aggregates["CARD.REFINE_BLADE#1"].TimesCardsDrawBlocked);
        Assert.Equal(4m, loaded.Data.Events[2].ForgeGained);
    }

    [Fact]
    public void HistoricalLoad_AcceptsLegacyResumableV11Fixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v11-per-instance-no-draw-blocked-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        var effect = loaded.Data.Aggregates["CARD.BATTLE_TRANCE#1"].AppliedEffects["POWER.NO_DRAW"];
        Assert.Equal(2, effect.TotalTriggeredCardsDrawBlocked);
        Assert.Equal(2, loaded.Data.Aggregates["CARD.POMMEL_STRIKE#1"].TimesCardsDrawBlocked);
    }

    [Fact]
    public void HistoricalLoad_AcceptsLegacyResumableV12Fixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v12-per-instance-draw-attempt-gap-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        var blocker = loaded.Data.Aggregates["CARD.BATTLE_TRANCE#1"].AppliedEffects["POWER.NO_DRAW"];
        Assert.Equal(3, blocker.TotalTriggeredCardsDrawBlocked);
        Assert.Equal(3, loaded.Data.Aggregates["CARD.BATTLE_TRANCE#2"].TimesCardsDrawAttempted);
        Assert.Equal(0, loaded.Data.Aggregates["CARD.BATTLE_TRANCE#2"].TimesCardsDrawn);
    }

    [Fact]
    public void HistoricalLoad_AcceptsLegacyResumableV13Fixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v13-per-instance-blocked-draw-reasons-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        var blocker = loaded.Data.Aggregates["CARD.BATTLE_TRANCE#1"].AppliedEffects["POWER.NO_DRAW"];
        Assert.Equal(3, blocker.TotalTriggeredCardsDrawBlocked);
        var reason = loaded.Data.Aggregates["CARD.BATTLE_TRANCE#2"].BlockedDrawReasons["effect:POWER.NO_DRAW"];
        Assert.Equal("No Draw", reason.DisplayName);
        Assert.Equal(3, reason.Count);
    }

    [Fact]
    public void HistoricalLoad_AcceptsLegacyResumableV14Fixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v14-per-instance-make-it-so-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Equal(9m, loaded.Data.Aggregates["CARD.REFINE_BLADE#1"].TotalForgeGenerated);
        Assert.Equal(2, loaded.Data.Aggregates["CARD.MAKE_IT_SO#1"].TimesSummonedToHand);
        Assert.Equal(4m, loaded.Data.Events[2].ForgeGained);
    }

    [Fact]
    public void HistoricalLoad_AcceptsLegacyResumableV15Fixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v15-bag-of-marbles-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.BAG_OF_MARBLES"];
        Assert.Equal(5, relicAgg.EnemiesAffected);
        Assert.Equal(5, relicAgg.VulnerableApplied);
    }

    [Fact]
    public void HistoricalLoad_AcceptsLegacyResumableV16Fixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v16-red-mask-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.RED_MASK"];
        Assert.Equal(3, relicAgg.EnemiesAffected);
        Assert.Equal(3, relicAgg.WeakApplied);
    }

    [Fact]
    public void HistoricalLoad_AcceptsLegacyResumableV17Fixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v17-orichalcum-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.ORICHALCUM"];
        Assert.Equal(12, relicAgg.AdditionalBlockGained);
    }

    [Fact]
    public void HistoricalLoad_AcceptsV18Fixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v18-pocketwatch-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Null(loaded.CompatibilityNote);
        Assert.Equal(12, loaded.Data.RelicAggregates["RELIC.ORICHALCUM"].AdditionalBlockGained);
        Assert.Equal(6, loaded.Data.RelicAggregates["RELIC.POCKETWATCH"].AdditionalCardsDrawn);
    }

    [Fact]
    public void HistoricalLoad_AcceptsV19BookRepairKnifeFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v19-book-repair-knife-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Null(loaded.CompatibilityNote);
        Assert.Equal(3, loaded.Data.RelicAggregates["RELIC.BOOK_REPAIR_KNIFE"].DoomDeathTriggers);
        Assert.Equal(3, loaded.Data.RelicAggregates["RELIC.BOOK_REPAIR_KNIFE"].DoomKills);
        Assert.Equal(18m, loaded.Data.RelicAggregates["RELIC.BOOK_REPAIR_KNIFE"].TotalHealingAttempted);
        Assert.Equal(10m, loaded.Data.RelicAggregates["RELIC.BOOK_REPAIR_KNIFE"].TotalHealingRestored);
        Assert.Equal(8m, loaded.Data.RelicAggregates["RELIC.BOOK_REPAIR_KNIFE"].TotalHealingLost);
        Assert.Equal(6m, loaded.Data.RelicAggregates["RELIC.BOOK_REPAIR_KNIFE"].HealingLostReasons["full_hp"].Amount);
        Assert.Equal(2m, loaded.Data.RelicAggregates["RELIC.BOOK_REPAIR_KNIFE"].HealingLostReasons["other"].Amount);
    }

    [Fact]
    public void HistoricalLoad_AcceptsV20BoneFluteFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v20-bone-flute-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Null(loaded.CompatibilityNote);
        Assert.Equal(2, loaded.Data.RelicAggregates["RELIC.BONE_FLUTE"].BoneFluteTriggers);
        Assert.Equal(14, loaded.Data.RelicAggregates["RELIC.BONE_FLUTE"].AdditionalBlockGained);
    }

    [Fact]
    public void HistoricalLoad_AcceptsV21UnleashOstyHpFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v21-unleash-osty-hp-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Null(loaded.CompatibilityNote);
        var agg = loaded.Data.Aggregates["CARD.UNLEASH#1"];
        Assert.Equal(30, agg.TotalOstyHpAttackBonus);
        Assert.Equal(3, agg.TimesOstyHpAttackBonusApplied);
    }

    [Fact]
    public void HistoricalLoad_AcceptsV22OstySummonBodyFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v22-osty-summon-body-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Null(loaded.CompatibilityNote);
        var summonAgg = loaded.Data.Aggregates["CARD.SUMMON_FORTH#1"];
        Assert.Equal(2, summonAgg.TimesOstySummoned);
        Assert.Equal(18m, summonAgg.TotalOstyHpSummoned);
        Assert.Equal(18m, loaded.Data.MetaStats.TotalOstyHpSummoned);
        Assert.Equal(11m, loaded.Data.MetaStats.TotalOstyDamageAbsorbed);
    }

    [Fact]
    public void HistoricalLoad_AcceptsV23ReplayExtraPlaysFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v23-replay-extra-plays-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Null(loaded.CompatibilityNote);
        var agg = loaded.Data.Aggregates["CARD.STRIKE_KIN#1"];
        Assert.Equal(5, agg.Plays);
        Assert.Equal(2, agg.TimesReplayExtraPlayed);
    }

    [Fact]
    public void HistoricalLoad_AcceptsV24ReplaySourceBreakdownFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v24-replay-source-breakdown-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Null(loaded.CompatibilityNote);
        var agg = loaded.Data.Aggregates["CARD.STRIKE_KIN#1"];
        Assert.Equal(6, agg.Plays);
        Assert.Equal(3, agg.TimesReplayExtraPlayed);
        Assert.Equal(1, agg.ReplayExtraPlayReasons["replay"].Count);
        Assert.Equal("Replay", agg.ReplayExtraPlayReasons["replay"].DisplayName);
        Assert.Equal(2, agg.ReplayExtraPlayReasons["power:POWER.BURST"].Count);
        Assert.Equal("Burst", agg.ReplayExtraPlayReasons["power:POWER.BURST"].DisplayName);
    }

    [Fact]
    public void HistoricalLoad_AcceptsV25ReplayOutcomesFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v25-replay-outcomes-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Null(loaded.CompatibilityNote);
        var agg = loaded.Data.Aggregates["CARD.STRIKE_KIN#1"];
        Assert.Equal(6, agg.Plays);
        Assert.Equal(4, agg.TimesReplayExtraPlanned);
        Assert.Equal(3, agg.TimesReplayExtraPlayed);
        Assert.Equal(1, agg.TimesReplayAttackNoDamage);
        Assert.Equal(1, agg.ReplayExtraPlayPlannedReasons["replay"].Count);
        Assert.Equal(3, agg.ReplayExtraPlayPlannedReasons["power:POWER.BURST"].Count);
        Assert.Equal(2, agg.ReplayExtraPlayReasons["power:POWER.BURST"].Count);
        Assert.Equal(1, agg.ReplayAttackNoDamageReasons["power:POWER.BURST"].Count);
    }

    [Fact]
    public void HistoricalLoad_AcceptsMealTicketRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("meal-ticket-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Null(loaded.CompatibilityNote);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.MEAL_TICKET"];
        Assert.Equal(2, relicAgg.Activations);
        Assert.Equal(30m, relicAgg.TotalHealingAttempted);
        Assert.Equal(18m, relicAgg.TotalHealingRestored);
        Assert.Equal(12m, relicAgg.TotalHealingLost);
        Assert.Equal(12m, relicAgg.HealingLostReasons["full_hp"].Amount);
    }

    [Fact]
    public void HistoricalLoad_AcceptsBurningBloodRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("burning-blood-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Null(loaded.CompatibilityNote);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.BURNING_BLOOD"];
        Assert.Equal(2, relicAgg.Activations);
        Assert.Equal(12m, relicAgg.TotalHealingAttempted);
        Assert.Equal(9m, relicAgg.TotalHealingRestored);
        Assert.Equal(3m, relicAgg.TotalHealingLost);
        Assert.Equal(3m, relicAgg.HealingLostReasons["full_hp"].Amount);
    }

    [Fact]
    public void HistoricalLoad_AcceptsWhiteBeastStatueRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("white-beast-statue-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Null(loaded.CompatibilityNote);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.WHITE_BEAST_STATUE"];
        Assert.Equal(5, relicAgg.PotionsGained);
        Assert.Equal(2, relicAgg.CommonPotionsGained);
        Assert.Equal(2, relicAgg.UncommonPotionsGained);
        Assert.Equal(1, relicAgg.RarePotionsGained);
        Assert.Equal(3, relicAgg.PotionsSkipped);
    }

    [Fact]
    public void HistoricalLoad_AcceptsPhylacteryRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("phylactery-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Null(loaded.CompatibilityNote);
        var boundAgg = loaded.Data.RelicAggregates["RELIC.BOUND_PHYLACTERY"];
        Assert.Equal(3, boundAgg.Activations);
        Assert.Equal(12m, boundAgg.TotalOstyHpSummoned);
        var unboundAgg = loaded.Data.RelicAggregates["RELIC.PHYLACTERY_UNBOUND"];
        Assert.Equal(4, unboundAgg.Activations);
        Assert.Equal(20m, unboundAgg.TotalOstyHpSummoned);
    }

    [Fact]
    public void HistoricalLoad_AcceptsEnemyStatusPollutionFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("enemy-status-pollution-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Null(loaded.CompatibilityNote);
        var hauntedShipAgg = loaded.Data.EnemyAggregates["MONSTER.HAUNTED_SHIP"];
        Assert.Equal(2, hauntedShipAgg.DamageInstances);
        Assert.Equal(16, hauntedShipAgg.DamageAttempted);
        Assert.Equal(6, hauntedShipAgg.DamageBlocked);
        Assert.Equal(10, hauntedShipAgg.DamageDealt);
        Assert.Equal(3, hauntedShipAgg.StatusCardsAdded);
        Assert.Equal(1, hauntedShipAgg.StatusCardsAddedToHand);
        Assert.Equal(2, hauntedShipAgg.StatusCardsAddedToDraw);
        Assert.Equal(3, hauntedShipAgg.StatusCardsById["CARD.DAZED"].Count);
    }

    [Fact]
    public void ResumableLoad_RejectsLegacyV1Fixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v1-pooled-run.json"));

        Assert.Null(resumed);
    }

    [Fact]
    public void ResumableLoad_AcceptsLegacyResumableV2Fixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v2-per-instance-run.json"));

        Assert.NotNull(resumed);
        Assert.Contains("CARD.ENERGY_SURGE#1", resumed!.Aggregates.Keys);
    }

    [Fact]
    public void ResumableLoad_AcceptsLegacyResumableV3Fixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v3-per-instance-effects-run.json"));

        Assert.NotNull(resumed);
        var effect = resumed!.Aggregates["CARD.NECROBINDER_POWER#1"].AppliedEffects["POWER.NECROBINDER_TRIGGER"];
        Assert.Equal(3m, effect.TotalAmountApplied);
    }

    [Fact]
    public void ResumableLoad_AcceptsLegacyResumableV4Fixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v4-per-instance-effects-exhaust-run.json"));

        Assert.NotNull(resumed);
        Assert.Equal(1, resumed!.Aggregates["CARD.NECROBINDER_POWER#1"].TimesExhausted);
    }

    [Fact]
    public void ResumableLoad_AcceptsLegacyResumableV5Fixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v5-per-instance-block-ledger-run.json"));

        Assert.NotNull(resumed);
        Assert.Equal(6, resumed!.Aggregates["CARD.DEFEND_KIN#1"].TotalBlockEffective);
        Assert.Equal(4, resumed.Aggregates["CARD.DEFEND_KIN#1"].TotalBlockWasted);
    }

    [Fact]
    public void ResumableLoad_AcceptsLegacyResumableV6Fixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v6-per-instance-artifact-block-run.json"));

        Assert.NotNull(resumed);
        Assert.Equal(6, resumed!.Aggregates["CARD.DEFEND_KIN#1"].TotalBlockEffective);
        var effect = resumed.Aggregates["CARD.BASH_KIN#1"].AppliedEffects["POWER.WEAK"];
        Assert.Equal(1, effect.TimesBlockedByArtifact);
        Assert.Equal(2m, effect.TotalAmountBlockedByArtifact);
    }

    [Fact]
    public void ResumableLoad_AcceptsLegacyResumableV7Fixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v7-per-instance-poison-damage-run.json"));

        Assert.NotNull(resumed);
        var effect = resumed!.Aggregates["CARD.DEADLY_POISON#1"].AppliedEffects["POWER.POISON"];
        Assert.Equal(9m, effect.TotalTriggeredEffectiveDamage);
        Assert.Equal(3m, effect.TotalTriggeredOverkill);
    }

    [Fact]
    public void ResumableLoad_AcceptsLegacyResumableV8Fixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v8-per-instance-regent-stars-run.json"));

        Assert.NotNull(resumed);
        Assert.Equal(2, resumed!.Aggregates["CARD.VENERATE#1"].TotalStarsGenerated);
        Assert.Equal(2, resumed.Aggregates["CARD.STARDUST#1"].TotalStarsSpent);
        Assert.Equal(1, resumed.Aggregates["CARD.VENERATE#1"].TimesDrawn);
    }

    [Fact]
    public void ResumableLoad_AcceptsLegacyResumableV9BlockedDrawFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v9-per-instance-blocked-draw-run.json"));

        Assert.NotNull(resumed);
        Assert.Equal(2, resumed!.Aggregates["CARD.POMMEL_STRIKE#1"].TimesCardsDrawBlocked);
        Assert.Equal(1, resumed.Aggregates["CARD.POMMEL_STRIKE#1"].TimesCardsDrawn);
        Assert.Equal(0, resumed.Aggregates["CARD.POMMEL_STRIKE#1"].TotalStarsSpent);
    }

    [Fact]
    public void ResumableLoad_AcceptsLegacyResumableV9ForgeFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v9-per-instance-forge-run.json"));

        Assert.NotNull(resumed);
        Assert.Equal(9m, resumed!.Aggregates["CARD.REFINE_BLADE#1"].TotalForgeGenerated);
        Assert.Equal(0, resumed.Aggregates["CARD.REFINE_BLADE#1"].TimesCardsDrawBlocked);
    }

    [Fact]
    public void ResumableLoad_AcceptsLegacyResumableV10ForgeFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v10-per-instance-forge-run.json"));

        Assert.NotNull(resumed);
        Assert.Equal(9m, resumed!.Aggregates["CARD.REFINE_BLADE#1"].TotalForgeGenerated);
        Assert.Equal(0, resumed.Aggregates["CARD.REFINE_BLADE#1"].TimesCardsDrawBlocked);
    }

    [Fact]
    public void ResumableLoad_AcceptsLegacyResumableV11Fixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v11-per-instance-no-draw-blocked-run.json"));

        Assert.NotNull(resumed);
        var effect = resumed!.Aggregates["CARD.BATTLE_TRANCE#1"].AppliedEffects["POWER.NO_DRAW"];
        Assert.Equal(2, effect.TotalTriggeredCardsDrawBlocked);
        Assert.Equal(2, resumed.Aggregates["CARD.POMMEL_STRIKE#1"].TimesCardsDrawBlocked);
    }

    [Fact]
    public void ResumableLoad_AcceptsLegacyResumableV12Fixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v12-per-instance-draw-attempt-gap-run.json"));

        Assert.NotNull(resumed);
        var blocker = resumed!.Aggregates["CARD.BATTLE_TRANCE#1"].AppliedEffects["POWER.NO_DRAW"];
        Assert.Equal(3, blocker.TotalTriggeredCardsDrawBlocked);
        Assert.Equal(3, resumed.Aggregates["CARD.BATTLE_TRANCE#2"].TimesCardsDrawAttempted);
        Assert.Equal(0, resumed.Aggregates["CARD.BATTLE_TRANCE#2"].TimesCardsDrawn);
    }

    [Fact]
    public void ResumableLoad_AcceptsLegacyResumableV13Fixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v13-per-instance-blocked-draw-reasons-run.json"));

        Assert.NotNull(resumed);
        var blocker = resumed!.Aggregates["CARD.BATTLE_TRANCE#1"].AppliedEffects["POWER.NO_DRAW"];
        Assert.Equal(3, blocker.TotalTriggeredCardsDrawBlocked);
        var reason = resumed.Aggregates["CARD.BATTLE_TRANCE#2"].BlockedDrawReasons["effect:POWER.NO_DRAW"];
        Assert.Equal("No Draw", reason.DisplayName);
        Assert.Equal(3, reason.Count);
    }

    [Fact]
    public void ResumableLoad_AcceptsLegacyResumableV14Fixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v14-per-instance-make-it-so-run.json"));

        Assert.NotNull(resumed);
        Assert.Equal(9m, resumed!.Aggregates["CARD.REFINE_BLADE#1"].TotalForgeGenerated);
        Assert.Equal(2, resumed.Aggregates["CARD.MAKE_IT_SO#1"].TimesSummonedToHand);
        Assert.Equal(3, resumed.Aggregates["CARD.MAKE_IT_SO#1"].TimesDrawn);
    }

    [Fact]
    public void ResumableLoad_AcceptsLegacyResumableV15Fixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v15-bag-of-marbles-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.BAG_OF_MARBLES"];
        Assert.Equal(5, relicAgg.EnemiesAffected);
        Assert.Equal(5, relicAgg.VulnerableApplied);
    }

    [Fact]
    public void ResumableLoad_AcceptsLegacyResumableV16Fixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v16-red-mask-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.RED_MASK"];
        Assert.Equal(3, relicAgg.EnemiesAffected);
        Assert.Equal(3, relicAgg.WeakApplied);
    }

    [Fact]
    public void ResumableLoad_AcceptsLegacyResumableV17Fixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v17-orichalcum-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.ORICHALCUM"];
        Assert.Equal(12, relicAgg.AdditionalBlockGained);
    }

    [Fact]
    public void ResumableLoad_AcceptsV18Fixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v18-pocketwatch-run.json"));

        Assert.NotNull(resumed);
        Assert.Equal(12, resumed!.RelicAggregates["RELIC.ORICHALCUM"].AdditionalBlockGained);
        Assert.Equal(6, resumed.RelicAggregates["RELIC.POCKETWATCH"].AdditionalCardsDrawn);
    }

    [Fact]
    public void ResumableLoad_AcceptsV19BookRepairKnifeFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v19-book-repair-knife-run.json"));

        Assert.NotNull(resumed);
        Assert.Equal(3, resumed!.RelicAggregates["RELIC.BOOK_REPAIR_KNIFE"].DoomDeathTriggers);
        Assert.Equal(3, resumed!.RelicAggregates["RELIC.BOOK_REPAIR_KNIFE"].DoomKills);
        Assert.Equal(18m, resumed!.RelicAggregates["RELIC.BOOK_REPAIR_KNIFE"].TotalHealingAttempted);
        Assert.Equal(10m, resumed!.RelicAggregates["RELIC.BOOK_REPAIR_KNIFE"].TotalHealingRestored);
        Assert.Equal(8m, resumed!.RelicAggregates["RELIC.BOOK_REPAIR_KNIFE"].TotalHealingLost);
        Assert.Equal(6m, resumed!.RelicAggregates["RELIC.BOOK_REPAIR_KNIFE"].HealingLostReasons["full_hp"].Amount);
        Assert.Equal(2m, resumed!.RelicAggregates["RELIC.BOOK_REPAIR_KNIFE"].HealingLostReasons["other"].Amount);
    }

    [Fact]
    public void HistoricalLoad_AcceptsV19HappyFlowerFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v19-happy-flower-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.HAPPY_FLOWER"];
        Assert.Equal(3, relicAgg.EnergyGenerated);
    }

    [Fact]
    public void ResumableLoad_AcceptsV19HappyFlowerFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v19-happy-flower-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.HAPPY_FLOWER"];
        Assert.Equal(3, relicAgg.EnergyGenerated);
    }

    [Fact]
    public void HistoricalLoad_AcceptsV20CloakClaspFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v20-cloak-clasp-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.CLOAK_CLASP"];
        Assert.Equal(21, relicAgg.AdditionalBlockGained);
    }

    [Fact]
    public void ResumableLoad_AcceptsV20BoneFluteFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v20-bone-flute-run.json"));

        Assert.NotNull(resumed);
        Assert.Equal(2, resumed!.RelicAggregates["RELIC.BONE_FLUTE"].BoneFluteTriggers);
        Assert.Equal(14, resumed!.RelicAggregates["RELIC.BONE_FLUTE"].AdditionalBlockGained);
    }

    [Fact]
    public void ResumableLoad_AcceptsV20CloakClaspFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v20-cloak-clasp-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.CLOAK_CLASP"];
        Assert.Equal(21, relicAgg.AdditionalBlockGained);
    }

    [Fact]
    public void ResumableLoad_AcceptsV21UnleashOstyHpFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v21-unleash-osty-hp-run.json"));

        Assert.NotNull(resumed);
        var agg = resumed!.Aggregates["CARD.UNLEASH#1"];
        Assert.Equal(30, agg.TotalOstyHpAttackBonus);
        Assert.Equal(3, agg.TimesOstyHpAttackBonusApplied);
    }

    [Fact]
    public void ResumableLoad_AcceptsV22OstySummonBodyFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v22-osty-summon-body-run.json"));

        Assert.NotNull(resumed);
        var summonAgg = resumed!.Aggregates["CARD.SUMMON_FORTH#1"];
        Assert.Equal(2, summonAgg.TimesOstySummoned);
        Assert.Equal(18m, summonAgg.TotalOstyHpSummoned);
        Assert.Equal(18m, resumed.MetaStats.TotalOstyHpSummoned);
        Assert.Equal(11m, resumed.MetaStats.TotalOstyDamageAbsorbed);
    }

    [Fact]
    public void ResumableLoad_AcceptsV23ReplayExtraPlaysFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v23-replay-extra-plays-run.json"));

        Assert.NotNull(resumed);
        var agg = resumed!.Aggregates["CARD.STRIKE_KIN#1"];
        Assert.Equal(5, agg.Plays);
        Assert.Equal(2, agg.TimesReplayExtraPlayed);
    }

    [Fact]
    public void ResumableLoad_AcceptsV24ReplaySourceBreakdownFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v24-replay-source-breakdown-run.json"));

        Assert.NotNull(resumed);
        var agg = resumed!.Aggregates["CARD.STRIKE_KIN#1"];
        Assert.Equal(6, agg.Plays);
        Assert.Equal(3, agg.TimesReplayExtraPlayed);
        Assert.Equal(1, agg.ReplayExtraPlayReasons["replay"].Count);
        Assert.Equal(2, agg.ReplayExtraPlayReasons["power:POWER.BURST"].Count);
    }

    [Fact]
    public void ResumableLoad_AcceptsV25ReplayOutcomesFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v25-replay-outcomes-run.json"));

        Assert.NotNull(resumed);
        var agg = resumed!.Aggregates["CARD.STRIKE_KIN#1"];
        Assert.Equal(6, agg.Plays);
        Assert.Equal(4, agg.TimesReplayExtraPlanned);
        Assert.Equal(3, agg.TimesReplayExtraPlayed);
        Assert.Equal(1, agg.TimesReplayAttackNoDamage);
        Assert.Equal(3, agg.ReplayExtraPlayPlannedReasons["power:POWER.BURST"].Count);
        Assert.Equal(2, agg.ReplayExtraPlayReasons["power:POWER.BURST"].Count);
        Assert.Equal(1, agg.ReplayAttackNoDamageReasons["power:POWER.BURST"].Count);
    }

    [Fact]
    public void ResumableLoad_AcceptsMealTicketRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("meal-ticket-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.MEAL_TICKET"];
        Assert.Equal(2, relicAgg.Activations);
        Assert.Equal(30m, relicAgg.TotalHealingAttempted);
        Assert.Equal(18m, relicAgg.TotalHealingRestored);
        Assert.Equal(12m, relicAgg.TotalHealingLost);
        Assert.Equal(12m, relicAgg.HealingLostReasons["full_hp"].Amount);
    }

    [Fact]
    public void ResumableLoad_AcceptsBurningBloodRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("burning-blood-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.BURNING_BLOOD"];
        Assert.Equal(2, relicAgg.Activations);
        Assert.Equal(12m, relicAgg.TotalHealingAttempted);
        Assert.Equal(9m, relicAgg.TotalHealingRestored);
        Assert.Equal(3m, relicAgg.TotalHealingLost);
        Assert.Equal(3m, relicAgg.HealingLostReasons["full_hp"].Amount);
    }

    [Fact]
    public void ResumableLoad_AcceptsWhiteBeastStatueRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("white-beast-statue-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.WHITE_BEAST_STATUE"];
        Assert.Equal(5, relicAgg.PotionsGained);
        Assert.Equal(2, relicAgg.CommonPotionsGained);
        Assert.Equal(2, relicAgg.UncommonPotionsGained);
        Assert.Equal(1, relicAgg.RarePotionsGained);
        Assert.Equal(3, relicAgg.PotionsSkipped);
    }

    [Fact]
    public void ResumableLoad_AcceptsPhylacteryRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("phylactery-relic-run.json"));

        Assert.NotNull(resumed);
        var boundAgg = resumed!.RelicAggregates["RELIC.BOUND_PHYLACTERY"];
        Assert.Equal(3, boundAgg.Activations);
        Assert.Equal(12m, boundAgg.TotalOstyHpSummoned);
        var unboundAgg = resumed.RelicAggregates["RELIC.PHYLACTERY_UNBOUND"];
        Assert.Equal(4, unboundAgg.Activations);
        Assert.Equal(20m, unboundAgg.TotalOstyHpSummoned);
    }

    [Fact]
    public void ResumableLoad_AcceptsV26PrismaticGemFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v26-prismatic-gem-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.PRISMATIC_GEM"];
        Assert.Equal(4, relicAgg.EnergyGenerated);
        Assert.Equal(2, relicAgg.CardRewardsAffected);
    }

    [Fact]
    public void ResumableLoad_AcceptsV27OrichalcumBlockedAndCombatsInDeckFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v27-orichalcum-blocked-and-combats-in-deck-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.ORICHALCUM"];
        Assert.Equal(12, relicAgg.AdditionalBlockGained);
        Assert.Equal(4, relicAgg.BlockedTriggers);
        Assert.Equal(3, resumed.Aggregates["CARD.SPOILS_MAP#1"].CombatsInDeck);
        Assert.Equal(3, resumed.Aggregates["CARD.STRIKE_IRONCLAD#1"].CombatsInDeck);
    }

    [Fact]
    public void ResumableLoad_AcceptsV28ReptileTrinketFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v28-reptile-trinket-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.REPTILE_TRINKET"];
        Assert.Equal(3, relicAgg.Activations);
        Assert.Equal(6m, relicAgg.StrengthAdded);
    }

    [Fact]
    public void ResumableLoad_AcceptsV29GorgetFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v29-gorget-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.GORGET"];
        Assert.Equal(4, relicAgg.Activations);
        Assert.Equal(12m, relicAgg.PlatingAdded);
    }

    [Fact]
    public void ResumableLoad_AcceptsV30StoneCrackerFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v30-stone-cracker-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.STONE_CRACKER"];
        Assert.Equal(3, relicAgg.Activations);
        Assert.Equal(6, relicAgg.CardsUpgraded);
    }

    [Fact]
    public void ResumableLoad_AcceptsV31PrismaticGemRewardCategoriesFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v31-prismatic-gem-reward-categories-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.PRISMATIC_GEM"];
        Assert.Equal(4, relicAgg.EnergyGenerated);
        Assert.Equal(2, relicAgg.CardRewardsAffected);
        Assert.Equal(3, relicAgg.CardRewardCategories["defect"].Count);
        Assert.Equal("Defect", relicAgg.CardRewardCategories["defect"].DisplayName);
        Assert.Equal(1, relicAgg.CardRewardCategories["colorless"].Count);
        Assert.Equal("Colorless", relicAgg.CardRewardCategories["colorless"].DisplayName);
    }

    [Fact]
    public void ResumableLoad_AcceptsV32EnemyDamageFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v32-enemy-damage-run.json"));

        Assert.NotNull(resumed);
        var enemyAgg = resumed!.EnemyAggregates["MONSTER.JAW_WORM"];
        Assert.Equal("MONSTER.JAW_WORM", enemyAgg.EnemyId);
        Assert.Equal(20, enemyAgg.DamageAttempted);
        Assert.Equal(12, enemyAgg.DamageDealt);
        Assert.Equal(8, enemyAgg.DamageBlocked);
    }

    [Fact]
    public void ResumableLoad_AcceptsEnemyStatusPollutionFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("enemy-status-pollution-run.json"));

        Assert.NotNull(resumed);
        var hauntedShipAgg = resumed!.EnemyAggregates["MONSTER.HAUNTED_SHIP"];
        Assert.Equal(2, hauntedShipAgg.DamageInstances);
        Assert.Equal(16, hauntedShipAgg.DamageAttempted);
        Assert.Equal(6, hauntedShipAgg.DamageBlocked);
        Assert.Equal(10, hauntedShipAgg.DamageDealt);
        Assert.Equal(3, hauntedShipAgg.StatusCardsAdded);
        Assert.Equal(1, hauntedShipAgg.StatusCardsAddedToHand);
        Assert.Equal(2, hauntedShipAgg.StatusCardsAddedToDraw);
        Assert.Equal(3, hauntedShipAgg.StatusCardsById["CARD.DAZED"].Count);
        var entomancerAgg = resumed.EnemyAggregates["MONSTER.ENTOMANCER"];
        Assert.Equal(2, entomancerAgg.StatusCardsAdded);
        Assert.Equal(2, entomancerAgg.StatusCardsById["CARD.DAZED"].Count);
    }

    [Fact]
    public void HistoricalLoad_AcceptsOpenBranchRelicStatsFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("open-branch-relic-stats-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        var anchorAgg = loaded.Data.RelicAggregates["RELIC.ANCHOR"];
        Assert.Equal(2, anchorAgg.Activations);
        Assert.Equal(20, anchorAgg.AdditionalBlockGained);
        var letterOpenerAgg = loaded.Data.RelicAggregates["RELIC.LETTER_OPENER"];
        Assert.Equal(3, letterOpenerAgg.Activations);
        Assert.Equal(45, letterOpenerAgg.TotalDamageAttempted);
        Assert.Equal(9, letterOpenerAgg.TotalTargets);
        var bloodVialAgg = loaded.Data.RelicAggregates["RELIC.BLOOD_VIAL"];
        Assert.Equal(2, bloodVialAgg.Activations);
        Assert.Equal(3m, bloodVialAgg.TotalHealingRestored);
        Assert.Equal(1m, bloodVialAgg.TotalHealingLost);
        Assert.Equal(1m, bloodVialAgg.HealingLostReasons["full_hp"].Amount);
        Assert.Equal(16, loaded.Data.RelicAggregates["RELIC.AKABEKO"].VigorGained);
        var boomingConchAgg = loaded.Data.RelicAggregates["RELIC.BOOMING_CONCH"];
        Assert.Equal(2, boomingConchAgg.EnergyGenerated);
        Assert.Equal(4, boomingConchAgg.AdditionalCardsDrawn);
        var toolboxAgg = loaded.Data.RelicAggregates["RELIC.TOOLBOX"];
        Assert.Equal(2, toolboxAgg.Activations);
        Assert.Equal(4, toolboxAgg.UncommonCardsOffered);
        Assert.Equal(1, toolboxAgg.RareCardsOffered);
    }

    [Fact]
    public void ResumableLoad_AcceptsOpenBranchRelicStatsFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("open-branch-relic-stats-run.json"));

        Assert.NotNull(resumed);
        var letterOpenerAgg = resumed!.RelicAggregates["RELIC.LETTER_OPENER"];
        Assert.Equal(45, letterOpenerAgg.TotalDamageAttempted);
        Assert.Equal(9, letterOpenerAgg.TotalTargets);
        Assert.Equal(16, resumed.RelicAggregates["RELIC.AKABEKO"].VigorGained);
        Assert.Equal(4, resumed.RelicAggregates["RELIC.BOOMING_CONCH"].AdditionalCardsDrawn);
        Assert.Equal(4, resumed.RelicAggregates["RELIC.TOOLBOX"].UncommonCardsOffered);
        Assert.Equal(1, resumed.RelicAggregates["RELIC.TOOLBOX"].RareCardsOffered);
    }
}
