namespace BGIguard.Tests;

public sealed class ConfigServiceTests
{
    [Fact]
    public void Normalize_ReplacesInvalidValuesWithDefaults()
    {
        var config = new RuntimeConfig("BetterGI.exe", 0, -1, 42, true, -2);

        RuntimeConfig normalized = ConfigService.Normalize(config);

        Assert.Equal(85, normalized.MemoryPercent);
        Assert.Equal(5, normalized.MonitorIntervalSeconds);
        Assert.Equal(6, normalized.MissingCountThreshold);
        Assert.True(normalized.SkipSetup);
        Assert.Equal(4096, normalized.BetterGiMemoryLimitMB);
    }

    [Fact]
    public void CalculateMemoryLimitMB_UsesNormalizedPercent()
    {
        long limit = ConfigService.CalculateMemoryLimitMB(10_000, 80);

        Assert.Equal(8_000, limit);
    }
}
