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
    public void HistoricalLoad_AcceptsChosenCheeseRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("chosen-cheese-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Null(loaded.CompatibilityNote);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.CHOSEN_CHEESE"];
        Assert.Equal(3m, relicAgg.MaxHpGained);
        Assert.Equal(70m, relicAgg.OriginalMaxHp);
        Assert.Null(relicAgg.NewMaxHp);
    }

    [Fact]
    public void HistoricalLoad_AcceptsStrawberryRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("strawberry-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.STRAWBERRY"];
        Assert.Equal(1, relicAgg.Activations);
        Assert.Equal(7m, relicAgg.MaxHpGained);
        Assert.Equal(70m, relicAgg.OriginalMaxHp);
        Assert.Equal(77m, relicAgg.NewMaxHp);
    }

    [Fact]
    public void HistoricalLoad_AcceptsPearRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("pear-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.PEAR"];
        Assert.Equal(1, relicAgg.Activations);
        Assert.Equal(10m, relicAgg.MaxHpGained);
        Assert.Equal(70m, relicAgg.OriginalMaxHp);
        Assert.Equal(80m, relicAgg.NewMaxHp);
    }

    [Fact]
    public void HistoricalLoad_AcceptsMangoRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("mango-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.MANGO"];
        Assert.Equal(1, relicAgg.Activations);
        Assert.Equal(14m, relicAgg.MaxHpGained);
        Assert.Equal(70m, relicAgg.OriginalMaxHp);
        Assert.Equal(84m, relicAgg.NewMaxHp);
    }

    [Fact]
    public void HistoricalLoad_AcceptsNutritiousOysterRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("nutritious-oyster-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.NUTRITIOUS_OYSTER"];
        Assert.Equal(1, relicAgg.Activations);
        Assert.Equal(11m, relicAgg.MaxHpGained);
        Assert.Equal(70m, relicAgg.OriginalMaxHp);
        Assert.Equal(81m, relicAgg.NewMaxHp);
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
    public void HistoricalLoad_AcceptsAlchemizeCardFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("alchemize-card-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Null(loaded.CompatibilityNote);
        var cardAgg = loaded.Data.Aggregates["CARD.ALCHEMIZE#1"];
        Assert.Equal(5, cardAgg.PotionsGained);
        Assert.Equal(2, cardAgg.CommonPotionsGained);
        Assert.Equal(2, cardAgg.UncommonPotionsGained);
        Assert.Equal(1, cardAgg.RarePotionsGained);
        Assert.Equal(3, cardAgg.PotionsSkipped);
    }

    [Fact]
    public void HistoricalLoad_AcceptsDebtCardFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("debt-card-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Null(loaded.CompatibilityNote);
        var cardAgg = loaded.Data.Aggregates["CARD.DEBT#1"];
        Assert.Equal(4, cardAgg.DebtTriggers);
        Assert.Equal(13, cardAgg.DebtGoldLost);
        Assert.Equal(7, cardAgg.DebtGoldLossBlocked);
    }

    [Fact]
    public void HistoricalLoad_AcceptsSealOfGoldRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("seal-of-gold-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Null(loaded.CompatibilityNote);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.SEAL_OF_GOLD"];
        Assert.Equal(4, relicAgg.Activations);
        Assert.Equal(17, relicAgg.GoldLost);
        Assert.Equal(3, relicAgg.GoldLossBlocked);
        Assert.Equal(3, relicAgg.EnergyGenerated);
        Assert.Equal(2, relicAgg.EnergyGeneratedCombats);
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
    public void HistoricalLoad_AcceptsHappyFlowerEnergyAverageFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("happy-flower-energy-average-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.HAPPY_FLOWER"];
        Assert.Equal(5, relicAgg.EnergyGenerated);
        Assert.Equal(3, relicAgg.EnergyGeneratedCombats);
    }

    [Fact]
    public void ResumableLoad_AcceptsHappyFlowerEnergyAverageFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("happy-flower-energy-average-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.HAPPY_FLOWER"];
        Assert.Equal(5, relicAgg.EnergyGenerated);
        Assert.Equal(3, relicAgg.EnergyGeneratedCombats);
    }

    [Fact]
    public void HistoricalLoad_AcceptsNunchakuRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("nunchaku-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.NUNCHAKU"];
        Assert.Equal(18, relicAgg.NunchakuAttacksPlayed);
        Assert.Equal(3, relicAgg.EnergyGenerated);
        Assert.Equal(4, relicAgg.EnergyGeneratedCombats);
        Assert.Equal(2, relicAgg.NunchakuCombatsEndedOn8Charges);
        Assert.Equal(1, relicAgg.NunchakuCombatsEndedOn9Charges);
        Assert.Equal(34, relicAgg.NunchakuCombatEndChargeTotal);
    }

    [Fact]
    public void ResumableLoad_AcceptsNunchakuRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("nunchaku-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.NUNCHAKU"];
        Assert.Equal(18, relicAgg.NunchakuAttacksPlayed);
        Assert.Equal(3, relicAgg.EnergyGenerated);
        Assert.Equal(4, relicAgg.EnergyGeneratedCombats);
        Assert.Equal(2, relicAgg.NunchakuCombatsEndedOn8Charges);
        Assert.Equal(1, relicAgg.NunchakuCombatsEndedOn9Charges);
        Assert.Equal(34, relicAgg.NunchakuCombatEndChargeTotal);
    }

    [Fact]
    public void HistoricalLoad_AcceptsPenNibRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("pen-nib-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.PEN_NIB"];
        Assert.Equal(27, relicAgg.TotalDamageAttempted);
        Assert.Equal(9, relicAgg.PenNibAttacksPlayed);
        Assert.Equal(2, relicAgg.PenNibTurnsEndedOn8Charges);
        Assert.Equal(1, relicAgg.PenNibTurnsEndedOn9Charges);
        Assert.Equal(34, relicAgg.PenNibTurnEndChargeTotal);
        Assert.Equal(5, relicAgg.PenNibTurnEndChargeCount);
    }

    [Fact]
    public void ResumableLoad_AcceptsPenNibRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("pen-nib-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.PEN_NIB"];
        Assert.Equal(27, relicAgg.TotalDamageAttempted);
        Assert.Equal(9, relicAgg.PenNibAttacksPlayed);
        Assert.Equal(2, relicAgg.PenNibTurnsEndedOn8Charges);
        Assert.Equal(1, relicAgg.PenNibTurnsEndedOn9Charges);
        Assert.Equal(34, relicAgg.PenNibTurnEndChargeTotal);
        Assert.Equal(5, relicAgg.PenNibTurnEndChargeCount);
    }

    [Fact]
    public void HistoricalLoad_AcceptsIronClubRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("iron-club-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.IRON_CLUB"];
        Assert.Equal(7, relicAgg.AdditionalCardsDrawn);
        Assert.Equal(4, relicAgg.IronClubCombats);
        Assert.Equal(1, relicAgg.IronClubCombatsEndedOn0Charges);
        Assert.Equal(1, relicAgg.IronClubCombatsEndedOn1Charges);
        Assert.Equal(0, relicAgg.IronClubCombatsEndedOn2Charges);
        Assert.Equal(2, relicAgg.IronClubCombatsEndedOn3Charges);
        Assert.Equal(7, relicAgg.IronClubCombatEndChargeTotal);
        Assert.Equal(4, relicAgg.IronClubCombatEndChargeCount);
    }

    [Fact]
    public void ResumableLoad_AcceptsIronClubRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("iron-club-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.IRON_CLUB"];
        Assert.Equal(7, relicAgg.AdditionalCardsDrawn);
        Assert.Equal(4, relicAgg.IronClubCombats);
        Assert.Equal(1, relicAgg.IronClubCombatsEndedOn0Charges);
        Assert.Equal(1, relicAgg.IronClubCombatsEndedOn1Charges);
        Assert.Equal(0, relicAgg.IronClubCombatsEndedOn2Charges);
        Assert.Equal(2, relicAgg.IronClubCombatsEndedOn3Charges);
        Assert.Equal(7, relicAgg.IronClubCombatEndChargeTotal);
        Assert.Equal(4, relicAgg.IronClubCombatEndChargeCount);
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
    public void ResumableLoad_AcceptsChosenCheeseRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("chosen-cheese-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.CHOSEN_CHEESE"];
        Assert.Equal(3m, relicAgg.MaxHpGained);
        Assert.Equal(70m, relicAgg.OriginalMaxHp);
        Assert.Null(relicAgg.NewMaxHp);
    }

    [Fact]
    public void ResumableLoad_AcceptsStrawberryRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("strawberry-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.STRAWBERRY"];
        Assert.Equal(1, relicAgg.Activations);
        Assert.Equal(7m, relicAgg.MaxHpGained);
        Assert.Equal(70m, relicAgg.OriginalMaxHp);
        Assert.Equal(77m, relicAgg.NewMaxHp);
    }

    [Fact]
    public void ResumableLoad_AcceptsPearRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("pear-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.PEAR"];
        Assert.Equal(1, relicAgg.Activations);
        Assert.Equal(10m, relicAgg.MaxHpGained);
        Assert.Equal(70m, relicAgg.OriginalMaxHp);
        Assert.Equal(80m, relicAgg.NewMaxHp);
    }

    [Fact]
    public void ResumableLoad_AcceptsMangoRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("mango-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.MANGO"];
        Assert.Equal(1, relicAgg.Activations);
        Assert.Equal(14m, relicAgg.MaxHpGained);
        Assert.Equal(70m, relicAgg.OriginalMaxHp);
        Assert.Equal(84m, relicAgg.NewMaxHp);
    }

    [Fact]
    public void ResumableLoad_AcceptsNutritiousOysterRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("nutritious-oyster-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.NUTRITIOUS_OYSTER"];
        Assert.Equal(1, relicAgg.Activations);
        Assert.Equal(11m, relicAgg.MaxHpGained);
        Assert.Equal(70m, relicAgg.OriginalMaxHp);
        Assert.Equal(81m, relicAgg.NewMaxHp);
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
    public void ResumableLoad_AcceptsAlchemizeCardFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("alchemize-card-run.json"));

        Assert.NotNull(resumed);
        var cardAgg = resumed!.Aggregates["CARD.ALCHEMIZE#1"];
        Assert.Equal(5, cardAgg.PotionsGained);
        Assert.Equal(2, cardAgg.CommonPotionsGained);
        Assert.Equal(2, cardAgg.UncommonPotionsGained);
        Assert.Equal(1, cardAgg.RarePotionsGained);
        Assert.Equal(3, cardAgg.PotionsSkipped);
    }

    [Fact]
    public void ResumableLoad_AcceptsDebtCardFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("debt-card-run.json"));

        Assert.NotNull(resumed);
        var cardAgg = resumed!.Aggregates["CARD.DEBT#1"];
        Assert.Equal(4, cardAgg.DebtTriggers);
        Assert.Equal(13, cardAgg.DebtGoldLost);
        Assert.Equal(7, cardAgg.DebtGoldLossBlocked);
    }

    [Fact]
    public void ResumableLoad_AcceptsSealOfGoldRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("seal-of-gold-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.SEAL_OF_GOLD"];
        Assert.Equal(4, relicAgg.Activations);
        Assert.Equal(17, relicAgg.GoldLost);
        Assert.Equal(3, relicAgg.GoldLossBlocked);
        Assert.Equal(3, relicAgg.EnergyGenerated);
        Assert.Equal(2, relicAgg.EnergyGeneratedCombats);
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
        var pendulumAgg = loaded.Data.RelicAggregates["RELIC.PENDULUM"];
        Assert.Equal(3, pendulumAgg.Activations);
        Assert.Equal(6, pendulumAgg.AdditionalCardsDrawn);
        var parryingShieldAgg = loaded.Data.RelicAggregates["RELIC.PARRYING_SHIELD"];
        Assert.Equal(2, parryingShieldAgg.Activations);
        Assert.Equal(17, parryingShieldAgg.TotalDamageAttempted);
        Assert.Equal(11, parryingShieldAgg.TotalDamageDealt);
        Assert.Equal(4, parryingShieldAgg.TotalDamageBlocked);
        Assert.Equal(2, parryingShieldAgg.TotalDamageOverkill);
        Assert.Equal(1, parryingShieldAgg.Kills);
        Assert.Equal(2, parryingShieldAgg.TotalTargets);
        var hornCleatAgg = loaded.Data.RelicAggregates["RELIC.HORN_CLEAT"];
        Assert.Equal(2, hornCleatAgg.Activations);
        Assert.Equal(24, hornCleatAgg.AdditionalBlockGained);
        var toolboxAgg = loaded.Data.RelicAggregates["RELIC.TOOLBOX"];
        Assert.Equal(2, toolboxAgg.Activations);
        Assert.Equal(4, toolboxAgg.UncommonCardsOffered);
        Assert.Equal(1, toolboxAgg.RareCardsOffered);
        Assert.Equal(2, toolboxAgg.UncommonCardsTaken);
        Assert.Equal(1, toolboxAgg.RareCardsTaken);
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
        Assert.Equal(6, resumed.RelicAggregates["RELIC.PENDULUM"].AdditionalCardsDrawn);
        Assert.Equal(11, resumed.RelicAggregates["RELIC.PARRYING_SHIELD"].TotalDamageDealt);
        Assert.Equal(4, resumed.RelicAggregates["RELIC.PARRYING_SHIELD"].TotalDamageBlocked);
        Assert.Equal(24, resumed.RelicAggregates["RELIC.HORN_CLEAT"].AdditionalBlockGained);
        Assert.Equal(4, resumed.RelicAggregates["RELIC.TOOLBOX"].UncommonCardsOffered);
        Assert.Equal(1, resumed.RelicAggregates["RELIC.TOOLBOX"].RareCardsOffered);
        Assert.Equal(2, resumed.RelicAggregates["RELIC.TOOLBOX"].UncommonCardsTaken);
        Assert.Equal(1, resumed.RelicAggregates["RELIC.TOOLBOX"].RareCardsTaken);
    }

    [Fact]
    public void HistoricalLoad_AcceptsLetterOpenerRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("letter-opener-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.LETTER_OPENER"];
        Assert.Equal(3, relicAgg.Activations);
        Assert.Equal(45, relicAgg.TotalDamageAttempted);
        Assert.Equal(9, relicAgg.TotalTargets);
        Assert.Equal(9, relicAgg.LetterOpenerSkillsPlayed);
        Assert.Equal(3, relicAgg.LetterOpenerCombats);
        Assert.Equal(6, relicAgg.LetterOpenerTurns);
        Assert.Equal(2, relicAgg.LetterOpenerTurnsEndedAt1Charge);
        Assert.Equal(3, relicAgg.LetterOpenerTurnsEndedAt2Charges);
    }

    [Fact]
    public void ResumableLoad_AcceptsLetterOpenerRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("letter-opener-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.LETTER_OPENER"];
        Assert.Equal(3, relicAgg.Activations);
        Assert.Equal(45, relicAgg.TotalDamageAttempted);
        Assert.Equal(9, relicAgg.TotalTargets);
        Assert.Equal(9, relicAgg.LetterOpenerSkillsPlayed);
        Assert.Equal(3, relicAgg.LetterOpenerCombats);
        Assert.Equal(6, relicAgg.LetterOpenerTurns);
        Assert.Equal(2, relicAgg.LetterOpenerTurnsEndedAt1Charge);
        Assert.Equal(3, relicAgg.LetterOpenerTurnsEndedAt2Charges);
    }

    [Fact]
    public void HistoricalLoad_AcceptsBronzeScalesRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("bronze-scales-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.BRONZE_SCALES"];
        Assert.Equal(4, relicAgg.Activations);
        Assert.Equal(13, relicAgg.TotalDamageAttempted);
        Assert.Equal(10, relicAgg.TotalDamageDealt);
        Assert.Equal(2, relicAgg.TotalDamageBlocked);
        Assert.Equal(1, relicAgg.TotalDamageOverkill);
        Assert.Equal(1, relicAgg.Kills);
        Assert.Equal(4, relicAgg.TotalTargets);
    }

    [Fact]
    public void ResumableLoad_AcceptsBronzeScalesRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("bronze-scales-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.BRONZE_SCALES"];
        Assert.Equal(4, relicAgg.Activations);
        Assert.Equal(10, relicAgg.TotalDamageDealt);
        Assert.Equal(2, relicAgg.TotalDamageBlocked);
        Assert.Equal(1, relicAgg.TotalDamageOverkill);
    }

    [Fact]
    public void HistoricalLoad_AcceptsCandelabraRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("candelabra-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.CANDELABRA"];
        Assert.Equal(4, relicAgg.Activations);
        Assert.Equal(8, relicAgg.EnergyGenerated);
        Assert.Equal(2, relicAgg.SecondTurnsEndedWithExcessEnergy);
    }

    [Fact]
    public void ResumableLoad_AcceptsCandelabraRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("candelabra-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.CANDELABRA"];
        Assert.Equal(4, relicAgg.Activations);
        Assert.Equal(8, relicAgg.EnergyGenerated);
        Assert.Equal(2, relicAgg.SecondTurnsEndedWithExcessEnergy);
    }

    [Fact]
    public void HistoricalLoad_AcceptsTurnEnergyRelicsFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("turn-energy-relics-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);

        var lanternAgg = loaded.Data.RelicAggregates["RELIC.LANTERN"];
        Assert.Equal(2, lanternAgg.Activations);
        Assert.Equal(2, lanternAgg.EnergyGenerated);
        Assert.Equal(1, lanternAgg.FirstTurnsEndedWithExcessEnergy);

        var veryHotCocoaAgg = loaded.Data.RelicAggregates["RELIC.VERY_HOT_COCOA"];
        Assert.Equal(3, veryHotCocoaAgg.Activations);
        Assert.Equal(3, veryHotCocoaAgg.EnergyGenerated);
        Assert.Equal(2, veryHotCocoaAgg.FirstTurnsEndedWithExcessEnergy);

        var candelabraAgg = loaded.Data.RelicAggregates["RELIC.CANDELABRA"];
        Assert.Equal(4, candelabraAgg.Activations);
        Assert.Equal(8, candelabraAgg.EnergyGenerated);
        Assert.Equal(2, candelabraAgg.SecondTurnsEndedWithExcessEnergy);
        Assert.Equal(1, candelabraAgg.CombatsWithoutActivation);

        var chandelierAgg = loaded.Data.RelicAggregates["RELIC.CHANDELIER"];
        Assert.Equal(3, chandelierAgg.Activations);
        Assert.Equal(9, chandelierAgg.EnergyGenerated);
        Assert.Equal(1, chandelierAgg.ThirdTurnsEndedWithExcessEnergy);
        Assert.Equal(2, chandelierAgg.CombatsWithoutActivation);
    }

    [Fact]
    public void ResumableLoad_AcceptsTurnEnergyRelicsFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("turn-energy-relics-run.json"));

        Assert.NotNull(resumed);

        var lanternAgg = resumed!.RelicAggregates["RELIC.LANTERN"];
        Assert.Equal(2, lanternAgg.Activations);
        Assert.Equal(2, lanternAgg.EnergyGenerated);
        Assert.Equal(1, lanternAgg.FirstTurnsEndedWithExcessEnergy);

        var veryHotCocoaAgg = resumed.RelicAggregates["RELIC.VERY_HOT_COCOA"];
        Assert.Equal(3, veryHotCocoaAgg.Activations);
        Assert.Equal(3, veryHotCocoaAgg.EnergyGenerated);
        Assert.Equal(2, veryHotCocoaAgg.FirstTurnsEndedWithExcessEnergy);

        var candelabraAgg = resumed.RelicAggregates["RELIC.CANDELABRA"];
        Assert.Equal(4, candelabraAgg.Activations);
        Assert.Equal(8, candelabraAgg.EnergyGenerated);
        Assert.Equal(2, candelabraAgg.SecondTurnsEndedWithExcessEnergy);
        Assert.Equal(1, candelabraAgg.CombatsWithoutActivation);

        var chandelierAgg = resumed.RelicAggregates["RELIC.CHANDELIER"];
        Assert.Equal(3, chandelierAgg.Activations);
        Assert.Equal(9, chandelierAgg.EnergyGenerated);
        Assert.Equal(1, chandelierAgg.ThirdTurnsEndedWithExcessEnergy);
        Assert.Equal(2, chandelierAgg.CombatsWithoutActivation);
    }

    [Fact]
    public void HistoricalLoad_AcceptsPaelsWingSacrificeRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("paels-wing-sacrifice-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.PAELS_WING"];
        Assert.Equal(5, relicAgg.CommonCardsConsumed);
        Assert.Equal(3, relicAgg.UncommonCardsConsumed);
        Assert.Equal(1, relicAgg.RareCardsConsumed);
        Assert.Equal(3, relicAgg.SacrificesMade);
        Assert.Equal(2, relicAgg.SacrificesSkipped);
        Assert.Equal(1, relicAgg.RelicsGranted["RELIC.KUNAI"].Count);
        Assert.Equal("Kunai", relicAgg.RelicsGranted["RELIC.KUNAI"].DisplayName);
    }

    [Fact]
    public void ResumableLoad_AcceptsPaelsWingSacrificeRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("paels-wing-sacrifice-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.PAELS_WING"];
        Assert.Equal(5, relicAgg.CommonCardsConsumed);
        Assert.Equal(3, relicAgg.UncommonCardsConsumed);
        Assert.Equal(1, relicAgg.RareCardsConsumed);
        Assert.Equal(3, relicAgg.SacrificesMade);
        Assert.Equal(2, relicAgg.SacrificesSkipped);
        Assert.Equal(1, relicAgg.RelicsGranted["RELIC.KUNAI"].Count);
        Assert.Equal("Kunai", relicAgg.RelicsGranted["RELIC.KUNAI"].DisplayName);
    }

    [Fact]
    public void HistoricalLoad_AcceptsPaelsToothRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("paels-tooth-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        AssertPaelsToothFixture(loaded.Data.RelicAggregates["RELIC.PAELS_TOOTH"]);
    }

    [Fact]
    public void ResumableLoad_AcceptsPaelsToothRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("paels-tooth-relic-run.json"));

        Assert.NotNull(resumed);
        AssertPaelsToothFixture(resumed!.RelicAggregates["RELIC.PAELS_TOOTH"]);
    }

    [Fact]
    public void HistoricalLoad_AcceptsStrikeDummyRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("strike-dummy-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.STRIKE_DUMMY"];
        Assert.Equal(8, relicAgg.StrikeDummyStrikesPlayed);
        Assert.Equal(4, relicAgg.StrikeDummyBaseStrikesInDeck);
        Assert.Equal(3, relicAgg.StrikeDummyNonBaseStrikeCardsInDeck);
    }

    [Fact]
    public void ResumableLoad_AcceptsStrikeDummyRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("strike-dummy-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.STRIKE_DUMMY"];
        Assert.Equal(8, relicAgg.StrikeDummyStrikesPlayed);
        Assert.Equal(4, relicAgg.StrikeDummyBaseStrikesInDeck);
        Assert.Equal(3, relicAgg.StrikeDummyNonBaseStrikeCardsInDeck);
    }

    [Fact]
    public void HistoricalLoad_AcceptsUnsettlingLampRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("unsettling-lamp-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.UNSETTLING_LAMP"];
        Assert.Equal(3, relicAgg.Activations);
        Assert.Equal(4, relicAgg.VulnerableApplied);
        Assert.Equal(2, relicAgg.WeakApplied);
        Assert.Equal(6m, relicAgg.AppliedEffects["POWER.POISON"].TotalAmountApplied);
    }

    [Fact]
    public void ResumableLoad_AcceptsUnsettlingLampRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("unsettling-lamp-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.UNSETTLING_LAMP"];
        Assert.Equal(3, relicAgg.Activations);
        Assert.Equal(4, relicAgg.VulnerableApplied);
        Assert.Equal(2, relicAgg.WeakApplied);
        Assert.Equal(6m, relicAgg.AppliedEffects["POWER.POISON"].TotalAmountApplied);
    }

    [Fact]
    public void HistoricalLoad_AcceptsNutritiousSoupRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("nutritious-soup-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.NUTRITIOUS_SOUP"];
        Assert.Equal(2, relicAgg.Activations);
        Assert.Equal(4, relicAgg.NutritiousSoupEnchantedStrikesPlayed);
    }

    [Fact]
    public void ResumableLoad_AcceptsNutritiousSoupRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("nutritious-soup-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.NUTRITIOUS_SOUP"];
        Assert.Equal(2, relicAgg.Activations);
        Assert.Equal(4, relicAgg.NutritiousSoupEnchantedStrikesPlayed);
    }

    [Fact]
    public void HistoricalLoad_AcceptsBrilliantScarfRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("brilliant-scarf-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.BRILLIANT_SCARF"];
        Assert.Equal(2, relicAgg.DiscountCombats);
        Assert.Equal(5, relicAgg.DiscountsOffered);
        Assert.Equal(3, relicAgg.DiscountsTaken);
        Assert.Equal(7, relicAgg.EnergySavedByDiscount);
        Assert.Equal(2, relicAgg.DiscountedCardCosts["energy:2|stars:0"].Count);
        Assert.Equal(1, relicAgg.DiscountedCardCosts["energy:1|stars:2"].Count);
        Assert.Equal(1, relicAgg.DiscountedCardCosts["energy:4|stars:0"].Count);
    }

    [Fact]
    public void HistoricalLoad_AcceptsDarkstonePeriaptRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("darkstone-periapt-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.DARKSTONE_PERIAPT"];
        Assert.Equal(3, relicAgg.Activations);
        Assert.Equal(3, relicAgg.CursesAcquired);
        Assert.Equal(18, relicAgg.TotalMaxHpGained);
        Assert.Equal(70m, relicAgg.OriginalMaxHp);
        Assert.Equal(88m, relicAgg.NewMaxHp);
    }

    [Fact]
    public void HistoricalLoad_AcceptsLuckyFyshRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("lucky-fysh-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.LUCKY_FYSH"];
        Assert.Equal(45, relicAgg.GoldGained);
        Assert.Equal(3, relicAgg.CardsAddedToDeck);
    }

    [Fact]
    public void HistoricalLoad_AcceptsSignetRingRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("signet-ring-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.SIGNET_RING"];
        Assert.Equal(4, relicAgg.FloorsTraveledUntilNextShop);
    }

    [Fact]
    public void HistoricalLoad_AcceptsShovelRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("shovel-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.SHOVEL"];
        Assert.Equal(4, relicAgg.RelicsAcquired);
        Assert.Equal(1, relicAgg.CommonRelicsAcquired);
        Assert.Equal(2, relicAgg.UncommonRelicsAcquired);
        Assert.Equal(1, relicAgg.RareRelicsAcquired);
        Assert.Equal(2, relicAgg.CampfiresNotDug);
    }

    [Fact]
    public void HistoricalLoad_AcceptsJuzuBraceletRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("juzu-bracelet-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.JUZU_BRACELET"];
        Assert.Equal(3, relicAgg.QuestionMarkSitesEntered);
    }

    [Fact]
    public void ResumableLoad_AcceptsBrilliantScarfRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("brilliant-scarf-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.BRILLIANT_SCARF"];
        Assert.Equal(2, relicAgg.DiscountCombats);
        Assert.Equal(5, relicAgg.DiscountsOffered);
        Assert.Equal(3, relicAgg.DiscountsTaken);
        Assert.Equal(7, relicAgg.EnergySavedByDiscount);
        Assert.Equal(2, relicAgg.DiscountedCardCosts["energy:2|stars:0"].Count);
        Assert.Equal(1, relicAgg.DiscountedCardCosts["energy:1|stars:2"].Count);
        Assert.Equal(1, relicAgg.DiscountedCardCosts["energy:4|stars:0"].Count);
    }

    [Fact]
    public void ResumableLoad_AcceptsDarkstonePeriaptRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("darkstone-periapt-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.DARKSTONE_PERIAPT"];
        Assert.Equal(3, relicAgg.Activations);
        Assert.Equal(3, relicAgg.CursesAcquired);
        Assert.Equal(18, relicAgg.TotalMaxHpGained);
        Assert.Equal(70m, relicAgg.OriginalMaxHp);
        Assert.Equal(88m, relicAgg.NewMaxHp);
    }

    [Fact]
    public void ResumableLoad_AcceptsLuckyFyshRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("lucky-fysh-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.LUCKY_FYSH"];
        Assert.Equal(45, relicAgg.GoldGained);
        Assert.Equal(3, relicAgg.CardsAddedToDeck);
    }

    [Fact]
    public void ResumableLoad_AcceptsSignetRingRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("signet-ring-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.SIGNET_RING"];
        Assert.Equal(4, relicAgg.FloorsTraveledUntilNextShop);
    }

    [Fact]
    public void ResumableLoad_AcceptsShovelRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("shovel-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.SHOVEL"];
        Assert.Equal(4, relicAgg.RelicsAcquired);
        Assert.Equal(1, relicAgg.CommonRelicsAcquired);
        Assert.Equal(2, relicAgg.UncommonRelicsAcquired);
        Assert.Equal(1, relicAgg.RareRelicsAcquired);
        Assert.Equal(2, relicAgg.CampfiresNotDug);
    }

    [Fact]
    public void ResumableLoad_AcceptsJuzuBraceletRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("juzu-bracelet-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.JUZU_BRACELET"];
        Assert.Equal(3, relicAgg.QuestionMarkSitesEntered);
    }

    [Fact]
    public void HistoricalLoad_AcceptsCursedPearlRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("cursed-pearl-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.CURSED_PEARL"];
        var curseAgg = loaded.Data.Aggregates["CARD.GREED#1"];
        Assert.Equal(6, relicAgg.FloorsAscendedBeforeFirstShop);
        Assert.Equal(4, curseAgg.CombatsInDeck);
        Assert.Equal(8, curseAgg.TimesDrawn);
        Assert.Equal(3, curseAgg.TimesDiscarded);
        Assert.Equal(1, curseAgg.Plays);
        Assert.Equal(2, curseAgg.TimesExhausted);
    }

    [Fact]
    public void ResumableLoad_AcceptsCursedPearlRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("cursed-pearl-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.CURSED_PEARL"];
        var curseAgg = resumed.Aggregates["CARD.GREED#1"];
        Assert.Equal(6, relicAgg.FloorsAscendedBeforeFirstShop);
        Assert.Equal(4, curseAgg.CombatsInDeck);
        Assert.Equal(8, curseAgg.TimesDrawn);
        Assert.Equal(3, curseAgg.TimesDiscarded);
        Assert.Equal(1, curseAgg.Plays);
        Assert.Equal(2, curseAgg.TimesExhausted);
    }

    [Fact]
    public void HistoricalLoad_AcceptsLeafyPoulticeRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("leafy-poultice-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.LEAFY_POULTICE"];
        Assert.Equal(1, relicAgg.Activations);
        Assert.Equal(70m, relicAgg.OriginalMaxHp);
        Assert.Equal(58m, relicAgg.NewMaxHp);
        AssertLeafyPoulticeTransformations(relicAgg);
    }

    [Fact]
    public void ResumableLoad_AcceptsLeafyPoulticeRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("leafy-poultice-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.LEAFY_POULTICE"];
        Assert.Equal(1, relicAgg.Activations);
        Assert.Equal(70m, relicAgg.OriginalMaxHp);
        Assert.Equal(58m, relicAgg.NewMaxHp);
        AssertLeafyPoulticeTransformations(relicAgg);
    }

    [Fact]
    public void HistoricalLoad_AcceptsGamblingChipRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("gambling-chip-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.GAMBLING_CHIP"];
        Assert.Equal(3, relicAgg.Activations);
        Assert.Equal(7, relicAgg.CardsDiscarded);
    }

    [Fact]
    public void ResumableLoad_AcceptsGamblingChipRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("gambling-chip-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.GAMBLING_CHIP"];
        Assert.Equal(3, relicAgg.Activations);
        Assert.Equal(7, relicAgg.CardsDiscarded);
    }

    [Fact]
    public void HistoricalLoad_AcceptsCentennialPuzzleRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("centennial-puzzle-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.CENTENNIAL_PUZZLE"];
        Assert.Equal(4, relicAgg.Activations);
        Assert.Equal(11, relicAgg.AdditionalCardsDrawn);
    }

    [Fact]
    public void ResumableLoad_AcceptsCentennialPuzzleRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("centennial-puzzle-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.CENTENNIAL_PUZZLE"];
        Assert.Equal(4, relicAgg.Activations);
        Assert.Equal(11, relicAgg.AdditionalCardsDrawn);
    }

    [Fact]
    public void HistoricalLoad_AcceptsRegalPillowRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("regal-pillow-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.REGAL_PILLOW"];
        Assert.Equal(2, relicAgg.Activations);
        Assert.Equal(12m, relicAgg.TotalHealingAttempted);
        Assert.Equal(9m, relicAgg.TotalHealingRestored);
        Assert.Equal(3m, relicAgg.TotalHealingLost);
        Assert.Equal(3m, relicAgg.HealingLostReasons["full_hp"].Amount);
    }

    [Fact]
    public void ResumableLoad_AcceptsRegalPillowRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("regal-pillow-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.REGAL_PILLOW"];
        Assert.Equal(2, relicAgg.Activations);
        Assert.Equal(12m, relicAgg.TotalHealingAttempted);
        Assert.Equal(9m, relicAgg.TotalHealingRestored);
        Assert.Equal(3m, relicAgg.TotalHealingLost);
        Assert.Equal(3m, relicAgg.HealingLostReasons["full_hp"].Amount);
    }

    [Fact]
    public void HistoricalLoad_AcceptsPrecariousShearsRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("precarious-shears-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.PRECARIOUS_SHEARS"];
        Assert.Equal(new[] { "Strike", "Defend+" }, relicAgg.CardsRemoved);
        Assert.Equal(70m, relicAgg.OriginalMaxHp);
        Assert.Equal(63m, relicAgg.NewMaxHp);
        Assert.Equal(70m, relicAgg.StartingMaxHp);
        Assert.Equal(63m, relicAgg.ResultingMaxHp);
    }

    [Fact]
    public void ResumableLoad_AcceptsPrecariousShearsRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("precarious-shears-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.PRECARIOUS_SHEARS"];
        Assert.Equal(new[] { "Strike", "Defend+" }, relicAgg.CardsRemoved);
        Assert.Equal(70m, relicAgg.OriginalMaxHp);
        Assert.Equal(63m, relicAgg.NewMaxHp);
        Assert.Equal(70m, relicAgg.StartingMaxHp);
        Assert.Equal(63m, relicAgg.ResultingMaxHp);
    }

    [Fact]
    public void HistoricalLoad_AcceptsSandCastleRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("sand-castle-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.SAND_CASTLE"];
        Assert.Equal(2, relicAgg.CardsUpgraded);
        Assert.Equal(new[] { "Strike+", "Defend+" }, relicAgg.UpgradedCards);
    }

    [Fact]
    public void ResumableLoad_AcceptsSandCastleRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("sand-castle-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.SAND_CASTLE"];
        Assert.Equal(2, relicAgg.CardsUpgraded);
        Assert.Equal(new[] { "Strike+", "Defend+" }, relicAgg.UpgradedCards);
    }

    [Fact]
    public void HistoricalLoad_AcceptsFragrantMushroomRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("fragrant-mushroom-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.FRAGRANT_MUSHROOM"];
        Assert.Equal(2, relicAgg.CardsUpgraded);
        Assert.Equal(new[] { "Strike+", "Defend+" }, relicAgg.UpgradedCards);
    }

    [Fact]
    public void ResumableLoad_AcceptsFragrantMushroomRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("fragrant-mushroom-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.FRAGRANT_MUSHROOM"];
        Assert.Equal(2, relicAgg.CardsUpgraded);
        Assert.Equal(new[] { "Strike+", "Defend+" }, relicAgg.UpgradedCards);
    }

    [Fact]
    public void HistoricalLoad_AcceptsWhetstoneRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("whetstone-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.WHETSTONE"];
        Assert.Equal(2, relicAgg.CardsUpgraded);
        Assert.Equal(new[] { "Strike+", "Pommel Strike+" }, relicAgg.UpgradedCards);
    }

    [Fact]
    public void ResumableLoad_AcceptsWhetstoneRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("whetstone-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.WHETSTONE"];
        Assert.Equal(2, relicAgg.CardsUpgraded);
        Assert.Equal(new[] { "Strike+", "Pommel Strike+" }, relicAgg.UpgradedCards);
    }

    [Fact]
    public void HistoricalLoad_AcceptsFishingRodRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("fishing-rod-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.FISHING_ROD"];
        Assert.Equal(2, relicAgg.CardsUpgraded);
        Assert.Equal(new[] { "Grave Warden+", "Reap+" }, relicAgg.UpgradedCards);
    }

    [Fact]
    public void ResumableLoad_AcceptsFishingRodRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("fishing-rod-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.FISHING_ROD"];
        Assert.Equal(2, relicAgg.CardsUpgraded);
        Assert.Equal(new[] { "Grave Warden+", "Reap+" }, relicAgg.UpgradedCards);
    }

    [Fact]
    public void HistoricalLoad_AcceptsEggRelicOffersFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("egg-relic-offers-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.Equal(7, loaded.Data.RelicAggregates["RELIC.MOLTEN_EGG"].UpgradedCardsOffered);
        Assert.Equal(5, loaded.Data.RelicAggregates["RELIC.TOXIC_EGG"].UpgradedCardsOffered);
        Assert.Equal(3, loaded.Data.RelicAggregates["RELIC.FROZEN_EGG"].UpgradedCardsOffered);
    }

    [Fact]
    public void ResumableLoad_AcceptsEggRelicOffersFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("egg-relic-offers-run.json"));

        Assert.NotNull(resumed);
        Assert.Equal(7, resumed!.RelicAggregates["RELIC.MOLTEN_EGG"].UpgradedCardsOffered);
        Assert.Equal(5, resumed.RelicAggregates["RELIC.TOXIC_EGG"].UpgradedCardsOffered);
        Assert.Equal(3, resumed.RelicAggregates["RELIC.FROZEN_EGG"].UpgradedCardsOffered);
    }

    [Fact]
    public void HistoricalLoad_AcceptsBloodSoakedRoseRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("blood-soaked-rose-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.BLOOD_SOAKED_ROSE"];
        var curseAgg = loaded.Data.Aggregates["CARD.ENTHRALLED#1"];
        Assert.Equal(3, relicAgg.Activations);
        Assert.Equal(9, relicAgg.EnergyGenerated);
        Assert.Equal(3, curseAgg.CombatsInDeck);
        Assert.Equal(5, curseAgg.TimesDrawn);
        Assert.Equal(2, curseAgg.TimesDiscarded);
        Assert.Equal(1, curseAgg.Plays);
        Assert.Equal(1, curseAgg.TimesExhausted);
    }

    [Fact]
    public void ResumableLoad_AcceptsBloodSoakedRoseRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("blood-soaked-rose-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.BLOOD_SOAKED_ROSE"];
        var curseAgg = resumed.Aggregates["CARD.ENTHRALLED#1"];
        Assert.Equal(3, relicAgg.Activations);
        Assert.Equal(9, relicAgg.EnergyGenerated);
        Assert.Equal(3, curseAgg.CombatsInDeck);
        Assert.Equal(5, curseAgg.TimesDrawn);
        Assert.Equal(2, curseAgg.TimesDiscarded);
        Assert.Equal(1, curseAgg.Plays);
        Assert.Equal(1, curseAgg.TimesExhausted);
    }

    [Fact]
    public void HistoricalLoad_AcceptsPlanisphereRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("planisphere-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.PLANISPHERE"];
        Assert.Equal(3, relicAgg.Activations);
        Assert.Equal(15m, relicAgg.TotalHealingAttempted);
        Assert.Equal(11m, relicAgg.TotalHealingRestored);
        Assert.Equal(4m, relicAgg.TotalHealingLost);
        Assert.Equal(4m, relicAgg.HealingLostReasons["full_hp"].Amount);
    }

    [Fact]
    public void ResumableLoad_AcceptsPlanisphereRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("planisphere-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.PLANISPHERE"];
        Assert.Equal(3, relicAgg.Activations);
        Assert.Equal(15m, relicAgg.TotalHealingAttempted);
        Assert.Equal(11m, relicAgg.TotalHealingRestored);
        Assert.Equal(4m, relicAgg.TotalHealingLost);
        Assert.Equal(4m, relicAgg.HealingLostReasons["full_hp"].Amount);
    }

    [Fact]
    public void HistoricalLoad_AcceptsLizardTailRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("lizard-tail-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.LIZARD_TAIL"];
        Assert.Equal(1, relicAgg.Activations);
        Assert.Equal(7, relicAgg.FloorAcquired);
        Assert.Equal(19, relicAgg.FloorActivated);
        Assert.Equal(36m, relicAgg.TotalHealingAttempted);
        Assert.Equal(36m, relicAgg.TotalHealingRestored);
        Assert.Equal(0m, relicAgg.TotalHealingLost);
    }

    [Fact]
    public void ResumableLoad_AcceptsLizardTailRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("lizard-tail-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.LIZARD_TAIL"];
        Assert.Equal(1, relicAgg.Activations);
        Assert.Equal(7, relicAgg.FloorAcquired);
        Assert.Equal(19, relicAgg.FloorActivated);
        Assert.Equal(36m, relicAgg.TotalHealingAttempted);
        Assert.Equal(36m, relicAgg.TotalHealingRestored);
        Assert.Equal(0m, relicAgg.TotalHealingLost);
    }

    [Fact]
    public void HistoricalLoad_AcceptsPantographRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("pantograph-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Null(loaded.CompatibilityNote);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.PANTOGRAPH"];
        Assert.Equal(2, relicAgg.Activations);
        Assert.Equal(50m, relicAgg.TotalHealingAttempted);
        Assert.Equal(31m, relicAgg.TotalHealingRestored);
        Assert.Equal(19m, relicAgg.TotalHealingLost);
        Assert.Equal(19m, relicAgg.HealingLostReasons["full_hp"].Amount);
    }

    [Fact]
    public void ResumableLoad_AcceptsPantographRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("pantograph-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.PANTOGRAPH"];
        Assert.Equal(2, relicAgg.Activations);
        Assert.Equal(50m, relicAgg.TotalHealingAttempted);
        Assert.Equal(31m, relicAgg.TotalHealingRestored);
        Assert.Equal(19m, relicAgg.TotalHealingLost);
        Assert.Equal(19m, relicAgg.HealingLostReasons["full_hp"].Amount);
    }

    [Fact]
    public void HistoricalLoad_AcceptsHeftyTabletRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("hefty-tablet-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.HEFTY_TABLET"];
        Assert.Equal(1, relicAgg.CardsGranted["CARD.ADRENALINE"].Count);
        Assert.Equal("Adrenaline", relicAgg.CardsGranted["CARD.ADRENALINE"].DisplayName);
        Assert.Equal(1, relicAgg.CardChoicesSkipped);
    }

    [Fact]
    public void ResumableLoad_AcceptsHeftyTabletRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("hefty-tablet-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.HEFTY_TABLET"];
        Assert.Equal(1, relicAgg.CardsGranted["CARD.ADRENALINE"].Count);
        Assert.Equal("Adrenaline", relicAgg.CardsGranted["CARD.ADRENALINE"].DisplayName);
        Assert.Equal(1, relicAgg.CardChoicesSkipped);
    }

    [Fact]
    public void HistoricalLoad_AcceptsArcaneScrollRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("arcane-scroll-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.ARCANE_SCROLL"];
        Assert.Equal(1, relicAgg.CardsGranted["CARD.ADRENALINE"].Count);
        Assert.Equal("Adrenaline", relicAgg.CardsGranted["CARD.ADRENALINE"].DisplayName);
    }

    [Fact]
    public void ResumableLoad_AcceptsArcaneScrollRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("arcane-scroll-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.ARCANE_SCROLL"];
        Assert.Equal(1, relicAgg.CardsGranted["CARD.ADRENALINE"].Count);
        Assert.Equal("Adrenaline", relicAgg.CardsGranted["CARD.ADRENALINE"].DisplayName);
    }

    [Fact]
    public void HistoricalLoad_AcceptsLargeCapsuleRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("large-capsule-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.LARGE_CAPSULE"];
        Assert.Equal(1, relicAgg.RelicsGranted["RELIC.DATA_DISK"].Count);
        Assert.Equal("Data Disk", relicAgg.RelicsGranted["RELIC.DATA_DISK"].DisplayName);
        Assert.Equal(1, relicAgg.RelicsGranted["RELIC.BAG_OF_PREPARATION"].Count);
        Assert.Equal("Bag of Preparation", relicAgg.RelicsGranted["RELIC.BAG_OF_PREPARATION"].DisplayName);
    }

    [Fact]
    public void ResumableLoad_AcceptsLargeCapsuleRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("large-capsule-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.LARGE_CAPSULE"];
        Assert.Equal(1, relicAgg.RelicsGranted["RELIC.DATA_DISK"].Count);
        Assert.Equal("Data Disk", relicAgg.RelicsGranted["RELIC.DATA_DISK"].DisplayName);
        Assert.Equal(1, relicAgg.RelicsGranted["RELIC.BAG_OF_PREPARATION"].Count);
        Assert.Equal("Bag of Preparation", relicAgg.RelicsGranted["RELIC.BAG_OF_PREPARATION"].DisplayName);
    }

    [Fact]
    public void HistoricalLoad_AcceptsNeowsBonesRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("neows-bones-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.NEOWS_BONES"];
        Assert.Equal(1, relicAgg.RelicsGranted["RELIC.NEOWS_TALISMAN"].Count);
        Assert.Equal("Neow's Talisman", relicAgg.RelicsGranted["RELIC.NEOWS_TALISMAN"].DisplayName);
        Assert.Equal(1, relicAgg.RelicsGranted["RELIC.NEOWS_TORMENT"].Count);
        Assert.Equal("Neow's Torment", relicAgg.RelicsGranted["RELIC.NEOWS_TORMENT"].DisplayName);
        Assert.Equal(1, relicAgg.CardsGranted["CARD.INJURY"].Count);
        Assert.Equal("Injury", relicAgg.CardsGranted["CARD.INJURY"].DisplayName);

        var curseAgg = loaded.Data.Aggregates["CARD.INJURY#1"];
        Assert.Equal(3, curseAgg.CombatsInDeck);
        Assert.Equal(5, curseAgg.TimesDrawn);
        Assert.Equal(2, curseAgg.TimesDiscarded);
        Assert.Equal(1, curseAgg.Plays);
        Assert.Equal(2, curseAgg.TimesExhausted);
    }

    [Fact]
    public void ResumableLoad_AcceptsNeowsBonesRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("neows-bones-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.NEOWS_BONES"];
        Assert.Equal(1, relicAgg.RelicsGranted["RELIC.NEOWS_TALISMAN"].Count);
        Assert.Equal("Neow's Talisman", relicAgg.RelicsGranted["RELIC.NEOWS_TALISMAN"].DisplayName);
        Assert.Equal(1, relicAgg.RelicsGranted["RELIC.NEOWS_TORMENT"].Count);
        Assert.Equal("Neow's Torment", relicAgg.RelicsGranted["RELIC.NEOWS_TORMENT"].DisplayName);
        Assert.Equal(1, relicAgg.CardsGranted["CARD.INJURY"].Count);
        Assert.Equal("Injury", relicAgg.CardsGranted["CARD.INJURY"].DisplayName);

        var curseAgg = resumed.Aggregates["CARD.INJURY#1"];
        Assert.Equal(3, curseAgg.CombatsInDeck);
        Assert.Equal(5, curseAgg.TimesDrawn);
        Assert.Equal(2, curseAgg.TimesDiscarded);
        Assert.Equal(1, curseAgg.Plays);
        Assert.Equal(2, curseAgg.TimesExhausted);
    }

    [Fact]
    public void HistoricalLoad_AcceptsVambraceRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("vambrace-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.VAMBRACE"];
        Assert.Equal(2, relicAgg.Activations);
        Assert.Equal(13, relicAgg.AdditionalBlockGained);
    }

    [Fact]
    public void ResumableLoad_AcceptsVambraceRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("vambrace-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.VAMBRACE"];
        Assert.Equal(2, relicAgg.Activations);
        Assert.Equal(13, relicAgg.AdditionalBlockGained);
    }

    [Fact]
    public void HistoricalLoad_AcceptsRegaliteRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("regalite-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.REGALITE"];
        Assert.Equal(6, relicAgg.RegaliteCardsCreated);
        Assert.Equal(12, relicAgg.AdditionalBlockGained);
        Assert.Equal(4, relicAgg.RegaliteTurns);
        Assert.Equal(2, relicAgg.RegaliteCombats);
    }

    [Fact]
    public void ResumableLoad_AcceptsRegaliteRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("regalite-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.REGALITE"];
        Assert.Equal(6, relicAgg.RegaliteCardsCreated);
        Assert.Equal(12, relicAgg.AdditionalBlockGained);
        Assert.Equal(4, relicAgg.RegaliteTurns);
        Assert.Equal(2, relicAgg.RegaliteCombats);
    }

    [Fact]
    public void HistoricalLoad_AcceptsIntimidatingHelmetRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("intimidating-helmet-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.INTIMIDATING_HELMET"];
        Assert.Equal(6, relicAgg.Activations);
        Assert.Equal(30, relicAgg.AdditionalBlockGained);
        Assert.Equal(8, relicAgg.IntimidatingHelmetTurns);
        Assert.Equal(3, relicAgg.IntimidatingHelmetCombats);
    }

    [Fact]
    public void ResumableLoad_AcceptsIntimidatingHelmetRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("intimidating-helmet-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.INTIMIDATING_HELMET"];
        Assert.Equal(6, relicAgg.Activations);
        Assert.Equal(30, relicAgg.AdditionalBlockGained);
        Assert.Equal(8, relicAgg.IntimidatingHelmetTurns);
        Assert.Equal(3, relicAgg.IntimidatingHelmetCombats);
    }

    [Fact]
    public void HistoricalLoad_AcceptsTuningForkRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("tuning-fork-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.TUNING_FORK"];
        Assert.Equal(27, relicAgg.TuningForkSkillsPlayed);
        Assert.Equal(3, relicAgg.Activations);
        Assert.Equal(18, relicAgg.AdditionalBlockGained);
        Assert.Equal(2, relicAgg.TuningForkCombats);
        Assert.Equal(7, relicAgg.TuningForkTurns);
        Assert.Equal(2, relicAgg.TuningForkTurnsEndedOn8Charges);
        Assert.Equal(1, relicAgg.TuningForkTurnsEndedOn9Charges);
        Assert.Equal(31, relicAgg.TuningForkTurnEndChargeTotal);
        Assert.Equal(5, relicAgg.TuningForkTurnEndChargeCount);
    }

    [Fact]
    public void HistoricalLoad_AcceptsRippleBasinRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("ripple-basin-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.RIPPLE_BASIN"];
        Assert.Equal(3, relicAgg.Activations);
        Assert.Equal(12, relicAgg.AdditionalBlockGained);
    }

    [Fact]
    public void HistoricalLoad_AcceptsPaelsEyeRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("paels-eye-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.PAELS_EYE"];
        Assert.Equal(3, relicAgg.Activations);
        Assert.Equal(4, relicAgg.StatusCardsExhausted);
        Assert.Equal(2, relicAgg.CurseCardsExhausted);
        Assert.Equal(5, relicAgg.CombatsWithoutActivation);
    }

    [Fact]
    public void ResumableLoad_AcceptsPaelsEyeRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("paels-eye-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.PAELS_EYE"];
        Assert.Equal(3, relicAgg.Activations);
        Assert.Equal(4, relicAgg.StatusCardsExhausted);
        Assert.Equal(2, relicAgg.CurseCardsExhausted);
        Assert.Equal(5, relicAgg.CombatsWithoutActivation);
    }

    [Fact]
    public void ResumableLoad_AcceptsTuningForkRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("tuning-fork-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.TUNING_FORK"];
        Assert.Equal(27, relicAgg.TuningForkSkillsPlayed);
        Assert.Equal(3, relicAgg.Activations);
        Assert.Equal(18, relicAgg.AdditionalBlockGained);
        Assert.Equal(2, relicAgg.TuningForkCombats);
        Assert.Equal(7, relicAgg.TuningForkTurns);
        Assert.Equal(2, relicAgg.TuningForkTurnsEndedOn8Charges);
        Assert.Equal(1, relicAgg.TuningForkTurnsEndedOn9Charges);
        Assert.Equal(31, relicAgg.TuningForkTurnEndChargeTotal);
        Assert.Equal(5, relicAgg.TuningForkTurnEndChargeCount);
    }

    [Fact]
    public void ResumableLoad_AcceptsRippleBasinRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("ripple-basin-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.RIPPLE_BASIN"];
        Assert.Equal(3, relicAgg.Activations);
        Assert.Equal(12, relicAgg.AdditionalBlockGained);
    }

    [Fact]
    public void HistoricalLoad_AcceptsWarPaintRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("war-paint-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.WAR_PAINT"];
        Assert.Equal(2, relicAgg.CardsUpgraded);
        Assert.Equal(new[] { "Defend+", "Battle Trance+" }, relicAgg.UpgradedCards);
    }

    [Fact]
    public void ResumableLoad_AcceptsWarPaintRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("war-paint-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.WAR_PAINT"];
        Assert.Equal(2, relicAgg.CardsUpgraded);
        Assert.Equal(new[] { "Defend+", "Battle Trance+" }, relicAgg.UpgradedCards);
    }

    [Fact]
    public void HistoricalLoad_AcceptsMiniatureCannonRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("miniature-cannon-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.MINIATURE_CANNON"];
        Assert.Equal(4, relicAgg.Activations);
        Assert.Equal(3, relicAgg.MiniatureCannonUpgradedAttacksInDeck);
        Assert.Equal(10, relicAgg.MiniatureCannonUpgradedAttackPlays);
        Assert.Equal(17, relicAgg.MiniatureCannonUpgradedAttackHits);
    }

    [Fact]
    public void ResumableLoad_AcceptsMiniatureCannonRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("miniature-cannon-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.MINIATURE_CANNON"];
        Assert.Equal(4, relicAgg.Activations);
        Assert.Equal(3, relicAgg.MiniatureCannonUpgradedAttacksInDeck);
        Assert.Equal(10, relicAgg.MiniatureCannonUpgradedAttackPlays);
        Assert.Equal(17, relicAgg.MiniatureCannonUpgradedAttackHits);
    }

    [Fact]
    public void HistoricalLoad_AcceptsVajraRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("vajra-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.VAJRA"];
        Assert.Equal(6, relicAgg.VajraAttacksPlayed);
        Assert.Equal(11, relicAgg.VajraAttackHits);
    }

    [Fact]
    public void ResumableLoad_AcceptsVajraRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("vajra-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.VAJRA"];
        Assert.Equal(6, relicAgg.VajraAttacksPlayed);
        Assert.Equal(11, relicAgg.VajraAttackHits);
    }

    [Fact]
    public void HistoricalLoad_AcceptsKunaiRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("kunai-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.KUNAI"];
        Assert.Equal(14, relicAgg.KunaiAttacksPlayed);
        Assert.Equal(4, relicAgg.Activations);
        Assert.Equal(4, relicAgg.KunaiDexterityGained);
        Assert.Equal(2, relicAgg.KunaiTurnsEndedAt1Charge);
        Assert.Equal(3, relicAgg.KunaiTurnsEndedAt2Charges);
        Assert.Equal(11, relicAgg.KunaiTurnEndChargeTotal);
        Assert.Equal(7, relicAgg.KunaiTurnEndChargeCount);
    }

    [Fact]
    public void ResumableLoad_AcceptsKunaiRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("kunai-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.KUNAI"];
        Assert.Equal(14, relicAgg.KunaiAttacksPlayed);
        Assert.Equal(4, relicAgg.Activations);
        Assert.Equal(4, relicAgg.KunaiDexterityGained);
        Assert.Equal(2, relicAgg.KunaiTurnsEndedAt1Charge);
        Assert.Equal(3, relicAgg.KunaiTurnsEndedAt2Charges);
        Assert.Equal(11, relicAgg.KunaiTurnEndChargeTotal);
        Assert.Equal(7, relicAgg.KunaiTurnEndChargeCount);
    }

    [Fact]
    public void HistoricalLoad_AcceptsUnlimitedAttackChargeRelicsFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("unlimited-attack-charge-relics-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);

        var kusarigama = loaded.Data.RelicAggregates["RELIC.KUSARIGAMA"];
        Assert.Equal(14, kusarigama.KusarigamaAttacksPlayed);
        Assert.Equal(4, kusarigama.Activations);
        Assert.Equal(24, kusarigama.TotalDamageAttempted);
        Assert.Equal(17, kusarigama.TotalDamageDealt);
        Assert.Equal(3, kusarigama.TotalDamageBlocked);
        Assert.Equal(4, kusarigama.TotalDamageOverkill);
        Assert.Equal(2, kusarigama.Kills);
        Assert.Equal(4, kusarigama.TotalTargets);
        Assert.Equal(2, kusarigama.KusarigamaTurnsEndedAt1Charge);
        Assert.Equal(3, kusarigama.KusarigamaTurnsEndedAt2Charges);
        Assert.Equal(8, kusarigama.KusarigamaTurnEndChargeTotal);
        Assert.Equal(7, kusarigama.KusarigamaTurnEndChargeCount);

        var ornamentalFan = loaded.Data.RelicAggregates["RELIC.ORNAMENTAL_FAN"];
        Assert.Equal(11, ornamentalFan.OrnamentalFanAttacksPlayed);
        Assert.Equal(3, ornamentalFan.Activations);
        Assert.Equal(13, ornamentalFan.AdditionalBlockGained);
        Assert.Equal(1, ornamentalFan.OrnamentalFanTurnsEndedAt1Charge);
        Assert.Equal(3, ornamentalFan.OrnamentalFanTurnsEndedAt2Charges);
        Assert.Equal(7, ornamentalFan.OrnamentalFanTurnEndChargeTotal);
        Assert.Equal(5, ornamentalFan.OrnamentalFanTurnEndChargeCount);

        var shuriken = loaded.Data.RelicAggregates["RELIC.SHURIKEN"];
        Assert.Equal(17, shuriken.ShurikenAttacksPlayed);
        Assert.Equal(5, shuriken.Activations);
        Assert.Equal(5m, shuriken.StrengthAdded);
        Assert.Equal(2, shuriken.ShurikenTurnsEndedAt1Charge);
        Assert.Equal(2, shuriken.ShurikenTurnsEndedAt2Charges);
        Assert.Equal(6, shuriken.ShurikenTurnEndChargeTotal);
        Assert.Equal(6, shuriken.ShurikenTurnEndChargeCount);
    }

    [Fact]
    public void ResumableLoad_AcceptsUnlimitedAttackChargeRelicsFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("unlimited-attack-charge-relics-run.json"));

        Assert.NotNull(resumed);

        var kusarigama = resumed!.RelicAggregates["RELIC.KUSARIGAMA"];
        Assert.Equal(14, kusarigama.KusarigamaAttacksPlayed);
        Assert.Equal(4, kusarigama.Activations);
        Assert.Equal(24, kusarigama.TotalDamageAttempted);
        Assert.Equal(17, kusarigama.TotalDamageDealt);
        Assert.Equal(3, kusarigama.TotalDamageBlocked);
        Assert.Equal(4, kusarigama.TotalDamageOverkill);
        Assert.Equal(2, kusarigama.Kills);
        Assert.Equal(4, kusarigama.TotalTargets);
        Assert.Equal(2, kusarigama.KusarigamaTurnsEndedAt1Charge);
        Assert.Equal(3, kusarigama.KusarigamaTurnsEndedAt2Charges);
        Assert.Equal(8, kusarigama.KusarigamaTurnEndChargeTotal);
        Assert.Equal(7, kusarigama.KusarigamaTurnEndChargeCount);

        var ornamentalFan = resumed.RelicAggregates["RELIC.ORNAMENTAL_FAN"];
        Assert.Equal(11, ornamentalFan.OrnamentalFanAttacksPlayed);
        Assert.Equal(3, ornamentalFan.Activations);
        Assert.Equal(13, ornamentalFan.AdditionalBlockGained);
        Assert.Equal(1, ornamentalFan.OrnamentalFanTurnsEndedAt1Charge);
        Assert.Equal(3, ornamentalFan.OrnamentalFanTurnsEndedAt2Charges);
        Assert.Equal(7, ornamentalFan.OrnamentalFanTurnEndChargeTotal);
        Assert.Equal(5, ornamentalFan.OrnamentalFanTurnEndChargeCount);

        var shuriken = resumed.RelicAggregates["RELIC.SHURIKEN"];
        Assert.Equal(17, shuriken.ShurikenAttacksPlayed);
        Assert.Equal(5, shuriken.Activations);
        Assert.Equal(5m, shuriken.StrengthAdded);
        Assert.Equal(2, shuriken.ShurikenTurnsEndedAt1Charge);
        Assert.Equal(2, shuriken.ShurikenTurnsEndedAt2Charges);
        Assert.Equal(6, shuriken.ShurikenTurnEndChargeTotal);
        Assert.Equal(6, shuriken.ShurikenTurnEndChargeCount);
    }

    [Fact]
    public void HistoricalLoad_AcceptsPaperPhrogRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("paper-phrog-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.PAPER_PHROG"];
        Assert.Equal(18.75m, relicAgg.PaperPhrogDamageAdded);
        Assert.Equal(6, relicAgg.PaperPhrogEnhancedAttacks);
        Assert.Equal(3, relicAgg.PaperPhrogCombats);
        Assert.Equal(5, relicAgg.PaperPhrogTurns);
    }

    [Fact]
    public void ResumableLoad_AcceptsPaperPhrogRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("paper-phrog-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.PAPER_PHROG"];
        Assert.Equal(18.75m, relicAgg.PaperPhrogDamageAdded);
        Assert.Equal(6, relicAgg.PaperPhrogEnhancedAttacks);
        Assert.Equal(3, relicAgg.PaperPhrogCombats);
        Assert.Equal(5, relicAgg.PaperPhrogTurns);
    }

    [Fact]
    public void HistoricalLoad_AcceptsRazorToothRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("razor-tooth-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.RAZOR_TOOTH"];
        Assert.Equal(6, relicAgg.CardsUpgraded);
        Assert.Equal(2, relicAgg.RazorToothCombats);
        Assert.Equal(8, relicAgg.RazorToothTurns);
        Assert.Equal(4, relicAgg.RazorToothUpgradedCardPlays);
        Assert.Equal(2, relicAgg.RazorToothUpgradedCardDraws);
    }

    [Fact]
    public void ResumableLoad_AcceptsRazorToothRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("razor-tooth-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.RAZOR_TOOTH"];
        Assert.Equal(6, relicAgg.CardsUpgraded);
        Assert.Equal(2, relicAgg.RazorToothCombats);
        Assert.Equal(8, relicAgg.RazorToothTurns);
        Assert.Equal(4, relicAgg.RazorToothUpgradedCardPlays);
        Assert.Equal(2, relicAgg.RazorToothUpgradedCardDraws);
    }

    [Fact]
    public void HistoricalLoad_AcceptsStorybookRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("storybook-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.Data.RelicAggregates.ContainsKey("RELIC.STORYBOOK"));
        var cardAgg = loaded.Data.Aggregates["CARD.BRIGHTEST_FLAME#1"];
        Assert.Equal(4, cardAgg.Plays);
        Assert.Equal(6, cardAgg.TimesDrawn);
        Assert.Equal(8, cardAgg.TotalEnergyGenerated);
        Assert.Equal(8, cardAgg.TimesCardsDrawn);
        Assert.Equal(4, cardAgg.TotalMaxHpLost);
    }

    [Fact]
    public void ResumableLoad_AcceptsStorybookRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("storybook-relic-run.json"));

        Assert.NotNull(resumed);
        Assert.True(resumed!.RelicAggregates.ContainsKey("RELIC.STORYBOOK"));
        var cardAgg = resumed.Aggregates["CARD.BRIGHTEST_FLAME#1"];
        Assert.Equal(4, cardAgg.Plays);
        Assert.Equal(6, cardAgg.TimesDrawn);
        Assert.Equal(8, cardAgg.TotalEnergyGenerated);
        Assert.Equal(8, cardAgg.TimesCardsDrawn);
        Assert.Equal(4, cardAgg.TotalMaxHpLost);
        Assert.Equal(new[] { 1 }, resumed.InstanceNumbersByDef["CARD.BRIGHTEST_FLAME"]);
        Assert.Equal(1, resumed.DefCounters["CARD.BRIGHTEST_FLAME"]);
    }

    [Fact]
    public void HistoricalLoad_AcceptsBookmarkRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("bookmark-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        var relicAgg = loaded.Data.RelicAggregates["RELIC.BOOKMARK"];
        Assert.Equal(7, relicAgg.Activations);
        Assert.Equal(4, relicAgg.BookmarkCombats);
        Assert.Equal(2, relicAgg.BookmarkCommonActivations);
        Assert.Equal(3, relicAgg.BookmarkUncommonActivations);
        Assert.Equal(2, relicAgg.BookmarkRareActivations);
    }

    [Fact]
    public void ResumableLoad_AcceptsBookmarkRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("bookmark-relic-run.json"));

        Assert.NotNull(resumed);
        var relicAgg = resumed!.RelicAggregates["RELIC.BOOKMARK"];
        Assert.Equal(7, relicAgg.Activations);
        Assert.Equal(4, relicAgg.BookmarkCombats);
        Assert.Equal(2, relicAgg.BookmarkCommonActivations);
        Assert.Equal(3, relicAgg.BookmarkUncommonActivations);
        Assert.Equal(2, relicAgg.BookmarkRareActivations);
    }

    [Fact]
    public void HistoricalLoad_AcceptsFresnelLensRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("fresnel-lens-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        AssertFresnelLensFixture(loaded.Data.RelicAggregates["RELIC.FRESNEL_LENS"]);
    }

    [Fact]
    public void ResumableLoad_AcceptsFresnelLensRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("fresnel-lens-relic-run.json"));

        Assert.NotNull(resumed);
        AssertFresnelLensFixture(resumed!.RelicAggregates["RELIC.FRESNEL_LENS"]);
    }

    [Fact]
    public void HistoricalLoad_AcceptsSilverCrucibleRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("silver-crucible-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        AssertSilverCrucibleFixture(loaded.Data.RelicAggregates["RELIC.SILVER_CRUCIBLE"]);
    }

    [Fact]
    public void ResumableLoad_AcceptsSilverCrucibleRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("silver-crucible-relic-run.json"));

        Assert.NotNull(resumed);
        AssertSilverCrucibleFixture(resumed!.RelicAggregates["RELIC.SILVER_CRUCIBLE"]);
    }

    [Fact]
    public void HistoricalLoad_AcceptsOrreryRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("orrery-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        AssertOrreryFixture(loaded.Data.RelicAggregates["RELIC.ORRERY"]);
    }

    [Fact]
    public void ResumableLoad_AcceptsOrreryRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("orrery-relic-run.json"));

        Assert.NotNull(resumed);
        AssertOrreryFixture(resumed!.RelicAggregates["RELIC.ORRERY"]);
    }

    [Fact]
    public void HistoricalLoad_AcceptsUnmovablePowerMetaFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("unmovable-power-meta-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Null(loaded.CompatibilityNote);
        Assert.Equal(24m, loaded.Data.MetaStats.ExtraBlockGainedFromUnmovablePower);
    }

    [Fact]
    public void ResumableLoad_AcceptsUnmovablePowerMetaFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("unmovable-power-meta-run.json"));

        Assert.NotNull(resumed);
        Assert.Equal(24m, resumed!.MetaStats.ExtraBlockGainedFromUnmovablePower);
    }

    [Fact]
    public void HistoricalLoad_AcceptsDowsingRodRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("dowsing-rod-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        Assert.Equal(
            2,
            loaded.Data.RelicAggregates["RELIC.DOWSING_ROD"].DowsingQuestionRoomsRemaining);
    }

    [Fact]
    public void ResumableLoad_AcceptsDowsingRodRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("dowsing-rod-relic-run.json"));

        Assert.NotNull(resumed);
        Assert.Equal(
            2,
            resumed!.RelicAggregates["RELIC.DOWSING_ROD"].DowsingQuestionRoomsRemaining);
    }

    private static void AssertLeafyPoulticeTransformations(RelicAggregate relicAgg)
    {
        Assert.Equal(2, relicAgg.CardTransformations.Count);
        Assert.Equal("CARD.STRIKE_IRONCLAD", relicAgg.CardTransformations[0].SourceCardId);
        Assert.Equal("Strike", relicAgg.CardTransformations[0].SourceDisplayName);
        Assert.Equal("CARD.BASH", relicAgg.CardTransformations[0].ResultCardId);
        Assert.Equal("Bash", relicAgg.CardTransformations[0].ResultDisplayName);
        Assert.Equal("CARD.DEFEND_IRONCLAD", relicAgg.CardTransformations[1].SourceCardId);
        Assert.Equal("Defend", relicAgg.CardTransformations[1].SourceDisplayName);
        Assert.Equal("CARD.SHRUG_IT_OFF", relicAgg.CardTransformations[1].ResultCardId);
        Assert.Equal("Shrug It Off", relicAgg.CardTransformations[1].ResultDisplayName);
    }

    [Fact]
    public void HistoricalLoad_AcceptsMummifiedHandRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("mummified-hand-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        AssertMummifiedHandFixture(loaded.Data.RelicAggregates["RELIC.MUMMIFIED_HAND"]);
    }

    [Fact]
    public void ResumableLoad_AcceptsMummifiedHandRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("mummified-hand-relic-run.json"));

        Assert.NotNull(resumed);
        AssertMummifiedHandFixture(resumed!.RelicAggregates["RELIC.MUMMIFIED_HAND"]);
    }

    [Fact]
    public void HistoricalLoad_AcceptsStoneHumidifierRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("stone-humidifier-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        AssertStoneHumidifierFixture(loaded.Data.RelicAggregates["RELIC.STONE_HUMIDIFIER"]);
    }

    [Fact]
    public void ResumableLoad_AcceptsStoneHumidifierRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("stone-humidifier-relic-run.json"));

        Assert.NotNull(resumed);
        AssertStoneHumidifierFixture(resumed!.RelicAggregates["RELIC.STONE_HUMIDIFIER"]);
    }

    [Fact]
    public void HistoricalLoad_AcceptsSturdyClampRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("sturdy-clamp-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        AssertSturdyClampFixture(loaded.Data.RelicAggregates["RELIC.STURDY_CLAMP"]);
    }

    [Fact]
    public void ResumableLoad_AcceptsSturdyClampRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("sturdy-clamp-relic-run.json"));

        Assert.NotNull(resumed);
        AssertSturdyClampFixture(resumed!.RelicAggregates["RELIC.STURDY_CLAMP"]);
    }

    [Fact]
    public void HistoricalLoad_AcceptsRuinedHelmetRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("ruined-helmet-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        AssertRuinedHelmetFixture(loaded.Data.RelicAggregates["RELIC.RUINED_HELMET"]);
    }

    [Fact]
    public void ResumableLoad_AcceptsRuinedHelmetRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("ruined-helmet-relic-run.json"));

        Assert.NotNull(resumed);
        AssertRuinedHelmetFixture(resumed!.RelicAggregates["RELIC.RUINED_HELMET"]);
    }

    [Fact]
    public void HistoricalLoad_AcceptsDaughterOfTheWindRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("daughter-of-the-wind-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        AssertDaughterOfTheWindFixture(
            loaded.Data.RelicAggregates["RELIC.DAUGHTER_OF_THE_WIND"]);
    }

    [Fact]
    public void ResumableLoad_AcceptsDaughterOfTheWindRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("daughter-of-the-wind-relic-run.json"));

        Assert.NotNull(resumed);
        AssertDaughterOfTheWindFixture(
            resumed!.RelicAggregates["RELIC.DAUGHTER_OF_THE_WIND"]);
    }

    [Fact]
    public void HistoricalLoad_AcceptsArtOfWarRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("art-of-war-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        AssertArtOfWarFixture(loaded.Data.RelicAggregates["RELIC.ART_OF_WAR"]);
    }

    [Fact]
    public void ResumableLoad_AcceptsArtOfWarRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("art-of-war-relic-run.json"));

        Assert.NotNull(resumed);
        AssertArtOfWarFixture(resumed!.RelicAggregates["RELIC.ART_OF_WAR"]);
    }

    [Fact]
    public void HistoricalLoad_AcceptsPaelsClawRelicFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("paels-claw-relic-run.json"));

        Assert.NotNull(loaded);
        Assert.True(loaded!.SupportsResume);
        AssertPaelsClawFixture(loaded.Data.RelicAggregates["RELIC.PAELS_CLAW"]);
    }

    [Fact]
    public void ResumableLoad_AcceptsPaelsClawRelicFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("paels-claw-relic-run.json"));

        Assert.NotNull(resumed);
        AssertPaelsClawFixture(resumed!.RelicAggregates["RELIC.PAELS_CLAW"]);
    }

    private static void AssertPaelsClawFixture(RelicAggregate relicAgg)
    {
        Assert.Equal(7, relicAgg.PaelsClawGoopyCardsPlayed);
        Assert.Equal(5, relicAgg.PaelsClawGoopyEnhancements);
        Assert.Equal(4, relicAgg.PaelsClawGoopyCards);
        Assert.Equal(4, relicAgg.PaelsClawTurns);
        Assert.Equal(2, relicAgg.PaelsClawCombats);
    }

    private static void AssertSturdyClampFixture(RelicAggregate relicAgg)
    {
        Assert.Equal(17, relicAgg.SturdyClampBlockRetained);
        Assert.Equal(3, relicAgg.SturdyClampExcessBlockOverTen);
        Assert.Equal(3, relicAgg.SturdyClampTurns);
        Assert.Equal(2, relicAgg.SturdyClampCombats);
    }

    private static void AssertRuinedHelmetFixture(RelicAggregate relicAgg)
    {
        Assert.Equal(7.5m, relicAgg.StrengthAdded);
        Assert.Equal(3, relicAgg.RuinedHelmetCombats);
    }

    private static void AssertDaughterOfTheWindFixture(RelicAggregate relicAgg)
    {
        Assert.Equal(24, relicAgg.AdditionalBlockGained);
        Assert.Equal(6, relicAgg.DaughterOfTheWindTurns);
        Assert.Equal(3, relicAgg.DaughterOfTheWindCombats);
    }

    private static void AssertArtOfWarFixture(RelicAggregate relicAgg)
    {
        Assert.Equal(4, relicAgg.EnergyGenerated);
        Assert.Equal(8, relicAgg.ArtOfWarTurns);
        Assert.Equal(2, relicAgg.EnergyGeneratedCombats);
    }

    private static void AssertStoneHumidifierFixture(RelicAggregate relicAgg)
    {
        Assert.Equal(2, relicAgg.Activations);
        Assert.Equal(9m, relicAgg.MaxHpGained);
        Assert.Collection(
            relicAgg.MaxHpActivations,
            activation =>
            {
                Assert.Equal(70m, activation.StartingHp);
                Assert.Equal(75m, activation.ResultingHp);
            },
            activation =>
            {
                Assert.Equal(80m, activation.StartingHp);
                Assert.Equal(84m, activation.ResultingHp);
            });
    }

    private static void AssertMummifiedHandFixture(RelicAggregate relicAgg)
    {
        Assert.Equal(4, relicAgg.Activations);
        Assert.Equal(6m, relicAgg.MummifiedHandTriggeringPowerCostTotal);
        Assert.Equal(6m, relicAgg.MummifiedHandDiscountGivenTotal);
        Assert.Equal(1.25m, relicAgg.MummifiedHandEnergySpentToDiscountedCostRatioTotal);
        Assert.Equal(2, relicAgg.MummifiedHandEnergySpentToDiscountedCostRatioCount);
        Assert.Equal(2, relicAgg.MummifiedHandCombats);
        Assert.Equal(5, relicAgg.MummifiedHandTurns);
        Assert.Equal(1, relicAgg.MummifiedHandDiscountedPowers);
        Assert.Equal(1, relicAgg.MummifiedHandDiscountedAttacks);
        Assert.Equal(1, relicAgg.MummifiedHandDiscountedSkills);
    }

    private static void AssertFresnelLensFixture(RelicAggregate relicAgg)
    {
        Assert.Equal(70m, relicAgg.OriginalMaxHp);
        Assert.Equal(57m, relicAgg.NewMaxHp);
        Assert.Equal(9, relicAgg.NimbleCardsTaken);
        Assert.Equal(8, relicAgg.RewardScreensWithNimbleCards);
        Assert.Equal(3, relicAgg.RewardScreensWithTwoNimbleCards);
        Assert.Equal(2, relicAgg.RewardScreensWithThreeOrMoreNimbleCards);
        Assert.Equal(5, relicAgg.RewardScreensWithoutNimbleCards);
        Assert.Equal(4, relicAgg.RewardScreensWithNimbleCardsButNoneTaken);
    }

    private static void AssertPaelsToothFixture(RelicAggregate relicAgg)
    {
        Assert.Collection(
            relicAgg.CardsReturned,
            card => AssertPaelsToothCard(card, "CARD.STRIKE_KIN", "Strike+", 1),
            card => AssertPaelsToothCard(card, "CARD.POMMEL_STRIKE", "Pommel Strike++", 2),
            card => AssertPaelsToothCard(card, "CARD.STRIKE_KIN", "Strike++", 2));
    }

    private static void AssertPaelsToothCard(
        RelicCardReturnAggregate card,
        string cardId,
        string displayName,
        int upgradeLevel)
    {
        Assert.Equal(cardId, card.CardId);
        Assert.Equal(displayName, card.DisplayName);
        Assert.Equal(upgradeLevel, card.UpgradeLevel);
    }

    private static void AssertSilverCrucibleFixture(RelicAggregate relicAgg)
    {
        Assert.Equal(3, relicAgg.CardRewardScreens.Count);

        var first = relicAgg.CardRewardScreens[0];
        Assert.Equal(1, first.ScreenNumber);
        Assert.Equal(12, first.Floor);
        Assert.True(first.Resolved);
        Assert.Collection(
            first.Cards,
            card => AssertSilverCrucibleCard(card, "CARD.BASH", "Bash+", 1, taken: true),
            card => AssertSilverCrucibleCard(card, "CARD.SHRUG_IT_OFF", "Shrug It Off+", 1, taken: false),
            card => AssertSilverCrucibleCard(card, "CARD.INFLAME", "Inflame+", 1, taken: false));

        var second = relicAgg.CardRewardScreens[1];
        Assert.Equal(2, second.ScreenNumber);
        Assert.Equal(12, second.Floor);
        Assert.True(second.Resolved);
        Assert.Collection(
            second.Cards,
            card => AssertSilverCrucibleCard(card, "CARD.POMMEL_STRIKE", "Pommel Strike+", 1, taken: false),
            card => AssertSilverCrucibleCard(card, "CARD.TRUE_GRIT", "True Grit+", 1, taken: true),
            card => AssertSilverCrucibleCard(card, "CARD.SPOT_WEAKNESS", "Spot Weakness+", 1, taken: false));

        var third = relicAgg.CardRewardScreens[2];
        Assert.Equal(3, third.ScreenNumber);
        Assert.Equal(14, third.Floor);
        Assert.True(third.Resolved);
        Assert.Equal(
            new[] { "CARD.HEADBUTT", "CARD.IRON_WAVE", "CARD.BATTLE_TRANCE" },
            third.Cards.Select(card => card.CardId));
        Assert.All(third.Cards, card =>
        {
            Assert.Equal(1, card.UpgradeLevel);
            Assert.False(card.Taken);
        });
    }

    private static void AssertSilverCrucibleCard(
        RelicCardRewardOptionAggregate card,
        string cardId,
        string displayName,
        int upgradeLevel,
        bool taken)
    {
        Assert.Equal(cardId, card.CardId);
        Assert.Equal(displayName, card.DisplayName);
        Assert.Equal(upgradeLevel, card.UpgradeLevel);
        Assert.Equal(taken, card.Taken);
    }

    private static void AssertOrreryFixture(RelicAggregate relicAgg)
    {
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, relicAgg.OrreryRewards.Select(reward => reward.RewardNumber));

        var skipped = relicAgg.OrreryRewards[0];
        Assert.Equal(12, skipped.Floor);
        Assert.Equal("skipped", skipped.Outcome);
        Assert.Empty(skipped.CardsObtained);
        Assert.Equal(
            new[] { "CARD.BASH", "CARD.SHRUG_IT_OFF", "CARD.INFLAME" },
            skipped.OfferedCardIds);

        var obtained = relicAgg.OrreryRewards[1];
        Assert.Equal("obtained", obtained.Outcome);
        Assert.Collection(
            obtained.CardsObtained,
            card =>
            {
                Assert.Equal("CARD.POMMEL_STRIKE", card.CardId);
                Assert.Equal("Pommel Strike", card.DisplayName);
                Assert.Equal(0, card.UpgradeLevel);
            });

        var sacrificed = relicAgg.OrreryRewards[2];
        Assert.Equal("alternative", sacrificed.Outcome);
        Assert.Equal("SACRIFICE", sacrificed.AlternativeId);
        Assert.Empty(sacrificed.CardsObtained);

        Assert.Equal("pending", relicAgg.OrreryRewards[3].Outcome);

        var upgraded = relicAgg.OrreryRewards[4];
        Assert.Equal("obtained", upgraded.Outcome);
        Assert.Collection(
            upgraded.CardsObtained,
            card =>
            {
                Assert.Equal("CARD.UPPERCUT", card.CardId);
                Assert.Equal("Uppercut+", card.DisplayName);
                Assert.Equal(1, card.UpgradeLevel);
            });
    }
}
