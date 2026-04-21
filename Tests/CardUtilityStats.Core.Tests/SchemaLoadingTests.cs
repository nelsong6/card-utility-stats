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
    public void HistoricalLoad_AcceptsCurrentV2Fixture()
    {
        var loaded = RunStorage.LoadHistorical(FixturePath("v2-per-instance-run.json"));

        Assert.NotNull(loaded);
        Assert.Equal(RunData.CurrentSchemaVersion, loaded!.SourceSchemaVersion);
        Assert.False(loaded.IsLegacy);
        Assert.True(loaded.SupportsResume);
        Assert.True(loaded.HasPerInstanceIdentity);
        Assert.Contains("CARD.STRIKE_KIN#1", loaded.Data.Aggregates.Keys);
        Assert.Equal(1, loaded.Data.DefCounters["CARD.STRIKE_KIN"]);
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
    public void ResumableLoad_AcceptsCurrentV2Fixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v2-per-instance-run.json"));

        Assert.NotNull(resumed);
        Assert.Equal(RunData.CurrentSchemaVersion, resumed!.SchemaVersion);
        Assert.Contains("CARD.ENERGY_SURGE#1", resumed.Aggregates.Keys);
    }

    [Fact]
    public void ResumableLoad_RejectsUnknownSchemaFixture()
    {
        var resumed = RunStorage.LoadResumable(FixturePath("v999-unknown-run.json"));

        Assert.Null(resumed);
    }
}
