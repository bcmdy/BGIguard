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

    [Fact]
    public void Load_ReturnsSavedSettingsAndPreservesPath()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "BGIguard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string configPath = Path.Combine(tempDir, "BGIguard_config.json");
        string exePath = Path.Combine(tempDir, "BetterGI.exe");
        File.WriteAllText(exePath, "");

        try
        {
            var service = new ConfigService(configPath, (_, _) => { });

            Assert.True(service.SavePath(exePath, out string normalizedPath));
            service.SaveSettings(new RuntimeConfig("", 72, 9, 4, true, 2048));

            RuntimeConfig config = service.Load();

            Assert.Equal(Path.GetFullPath(exePath), normalizedPath);
            Assert.Equal(normalizedPath, config.BetterGiPath);
            Assert.Equal(72, config.MemoryPercent);
            Assert.Equal(9, config.MonitorIntervalSeconds);
            Assert.Equal(4, config.MissingCountThreshold);
            Assert.True(config.SkipSetup);
            Assert.Equal(2048, config.BetterGiMemoryLimitMB);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Load_NormalizesInvalidFileValues()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "BGIguard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string configPath = Path.Combine(tempDir, "BGIguard_config.json");
        File.WriteAllText(configPath, """
            {
              "MemoryPercent": 101,
              "MonitorInterval": 0,
              "MissingCount": 99,
              "BetterGiMemoryLimitMB": -1
            }
            """);

        try
        {
            var service = new ConfigService(configPath, (_, _) => { });

            RuntimeConfig config = service.Load();

            Assert.Equal(85, config.MemoryPercent);
            Assert.Equal(5, config.MonitorIntervalSeconds);
            Assert.Equal(6, config.MissingCountThreshold);
            Assert.Equal(4096, config.BetterGiMemoryLimitMB);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
