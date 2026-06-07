namespace BGIguard.Tests;

public sealed class RuntimeConfigProviderTests
{
    [Fact]
    public void ReloadGuardRunnerConfig_UsesLatestSavedSettings()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "BGIguard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string configPath = Path.Combine(tempDir, "BGIguard_config.json");
        string betterGiPath = Path.Combine(tempDir, "BetterGI.exe");
        File.WriteAllText(betterGiPath, "");

        try
        {
            var configService = new ConfigService(configPath, (_, _) => { });
            var consoleUi = new ConsoleUiService(configService, configPath, configService.ClearCache);
            Assert.True(configService.SavePath(betterGiPath, out _));
            configService.SaveSettings(new RuntimeConfig("", 70, 8, 3, true, 1024));
            var provider = new RuntimeConfigProvider(configService, tempDir, "BetterGI.exe", consoleUi);

            GuardRunnerConfig first = provider.ReloadGuardRunnerConfig(3000, 1000, 60);
            configService.SaveSettings(new RuntimeConfig("", 65, 4, 2, true, 2048));
            GuardRunnerConfig second = provider.ReloadGuardRunnerConfig(3000, 1000, 60);

            Assert.Equal(70, first.MemoryPercent);
            Assert.Equal(8000, first.MonitorIntervalMs);
            Assert.Equal(3, first.MissingCountThreshold);
            Assert.Equal(1024, first.BetterGiMemoryLimitMB);
            Assert.Equal(65, second.MemoryPercent);
            Assert.Equal(4000, second.MonitorIntervalMs);
            Assert.Equal(2, second.MissingCountThreshold);
            Assert.Equal(2048, second.BetterGiMemoryLimitMB);
            Assert.Equal(60, second.RestartCooldownSeconds);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
