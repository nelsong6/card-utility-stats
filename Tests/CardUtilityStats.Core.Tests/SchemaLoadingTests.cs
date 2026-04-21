using CardUtilityStats.Core;
using Xunit;

namespace CardUtilityStats.Core.Tests;

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
        Assert.Equal(1, loaded!.SourceSchemaVersion);
        Assert.True(loaded.IsLegacy);
        Assert.False(loaded.SupportsResume);
        Assert.False(loaded.HasPerInstanceIdentity);
        Assert.Contains("historical data", loaded.CompatibilityNote!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CARD.STRIKE_KIN", loaded.Data.Aggregates.Keys);
    }

    [Fact]
    public void HistoricalLoad_AcceptsLegacyResumableV2Fixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v2-per-instance-run.json"));

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.SourceSchemaVersion);
        Assert.True(loaded.IsLegacy);
        Assert.True(loaded.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Contains("resumable", loaded.CompatibilityNote!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CARD.STRIKE_KIN#1", loaded.Data.Aggregates.Keys);
        Assert.Equal(1, loaded.Data.DefCounters["CARD.STRIKE_KIN"]);
    }

    [Fact]
    public void HistoricalLoad_AcceptsLegacyResumableV3Fixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v3-per-instance-effects-run.json"));

        Assert.NotNull(loaded);
        Assert.Equal(3, loaded!.SourceSchemaVersion);
        Assert.True(loaded.IsLegacy);
        Assert.True(loaded.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Contains("resumable", loaded.CompatibilityNote!, StringComparison.OrdinalIgnoreCase);
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
        Assert.Equal(4, loaded!.SourceSchemaVersion);
        Assert.True(loaded.IsLegacy);
        Assert.True(loaded.SupportsResume);
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
        Assert.Equal(5, loaded!.SourceSchemaVersion);
        Assert.True(loaded.IsLegacy);
        Assert.True(loaded.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        var agg = loaded.Data.Aggregates["CARD.DEFEND_KIN#1"];
        Assert.Equal(6, agg.TotalBlockEffective);
        Assert.Equal(4, agg.TotalBlockWasted);
        Assert.Equal(0, agg.TotalTargetsHit);
    }

    [Fact]
    public void HistoricalLoad_AcceptsCurrentV6Fixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v6-target-coverage-run.json"));

        Assert.NotNull(loaded);
        Assert.Equal(RunData.CurrentSchemaVersion, loaded!.SourceSchemaVersion);
        Assert.False(loaded.IsLegacy);
        Assert.True(loaded.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        var agg = loaded.Data.Aggregates["CARD.CLEAVE_KIN#1"];
        Assert.Equal(7, agg.TotalTargetsHit);
        Assert.Equal(25, agg.TotalEffective);
    }

    [Fact]
    public void HistoricalLoad_RejectsUnknownSchemaFixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v999-unknown-run.json"));

        Assert.Null(loaded);
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
        Assert.Equal(2, resumed!.SchemaVersion);
        Assert.Contains("CARD.ENERGY_SURGE#1", resumed.Aggregates.Keys);
    }

    [Fact]
    public void ResumableLoad_AcceptsLegacyResumableV3Fixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v3-per-instance-effects-run.json"));

        Assert.NotNull(resumed);
        Assert.Equal(3, resumed!.SchemaVersion);
        var effect = resumed.Aggregates["CARD.NECROBINDER_POWER#1"].AppliedEffects["POWER.NECROBINDER_TRIGGER"];
        Assert.Equal(3m, effect.TotalAmountApplied);
    }

    [Fact]
    public void ResumableLoad_AcceptsLegacyResumableV4Fixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v4-per-instance-effects-exhaust-run.json"));

        Assert.NotNull(resumed);
        Assert.Equal(4, resumed!.SchemaVersion);
        Assert.Equal(1, resumed.Aggregates["CARD.NECROBINDER_POWER#1"].TimesExhausted);
    }

    [Fact]
    public void ResumableLoad_AcceptsLegacyResumableV5Fixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v5-per-instance-block-ledger-run.json"));

        Assert.NotNull(resumed);
        Assert.Equal(5, resumed!.SchemaVersion);
        Assert.Equal(6, resumed.Aggregates["CARD.DEFEND_KIN#1"].TotalBlockEffective);
        Assert.Equal(4, resumed.Aggregates["CARD.DEFEND_KIN#1"].TotalBlockWasted);
        Assert.Equal(0, resumed.Aggregates["CARD.DEFEND_KIN#1"].TotalTargetsHit);
    }

    [Fact]
    public void ResumableLoad_AcceptsCurrentV6Fixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v6-target-coverage-run.json"));

        Assert.NotNull(resumed);
        Assert.Equal(RunData.CurrentSchemaVersion, resumed!.SchemaVersion);
        Assert.Equal(7, resumed.Aggregates["CARD.CLEAVE_KIN#1"].TotalTargetsHit);
        Assert.Equal(25, resumed.Aggregates["CARD.CLEAVE_KIN#1"].TotalEffective);
    }

    [Fact]
    public void ResumableLoad_RejectsUnknownSchemaFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v999-unknown-run.json"));

        Assert.Null(resumed);
    }
}
