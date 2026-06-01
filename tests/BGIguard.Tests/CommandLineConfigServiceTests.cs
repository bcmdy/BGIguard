namespace BGIguard.Tests;

public sealed class CommandLineConfigServiceTests
{
    [Fact]
    public void SetMemory_RejectsOutOfRangeValue()
    {
        using var tempConfig = new TempConfig();

        CommandLineConfigResult result = CommandLineConfigService.SetMemory(tempConfig.Store, "101");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void SetMemory_SavesValidValue()
    {
        using var tempConfig = new TempConfig();

        CommandLineConfigResult result = CommandLineConfigService.SetMemory(tempConfig.Store, "70");
        RuntimeConfig config = tempConfig.Store.Load();

        Assert.True(result.IsSuccess);
        Assert.Equal(70, config.MemoryPercent);
    }

    [Fact]
    public void SetProcessMemoryLimit_AllowsZeroToDisable()
    {
        using var tempConfig = new TempConfig();

        CommandLineConfigResult result = CommandLineConfigService.SetProcessMemoryLimit(tempConfig.Store, "0");
        RuntimeConfig config = tempConfig.Store.Load();

        Assert.True(result.IsSuccess);
        Assert.Equal(0, config.BetterGiMemoryLimitMB);
    }

    private sealed class TempConfig : IDisposable
    {
        private readonly string _directory;

        public TempConfig()
        {
            _directory = Path.Combine(Path.GetTempPath(), "BGIguard.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            Store = new ConfigService(Path.Combine(_directory, "BGIguard_config.json"), (_, _) => { });
        }

        public ConfigService Store { get; }

        public void Dispose()
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
