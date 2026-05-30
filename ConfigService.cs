namespace BGIguard;

internal sealed class ConfigService
{
    public const int DefaultMemoryPercent = 85;
    public const int DefaultMonitorIntervalSeconds = 5;
    public const int DefaultMissingCountThreshold = 6;
    public const int DefaultBetterGiMemoryLimitMB = 4096;

    public static RuntimeConfig Normalize(RuntimeConfig config)
    {
        int memoryPercent = config.MemoryPercent is > 0 and <= 100
            ? config.MemoryPercent
            : DefaultMemoryPercent;
        int monitorInterval = config.MonitorIntervalSeconds > 0
            ? config.MonitorIntervalSeconds
            : DefaultMonitorIntervalSeconds;
        int missingCount = config.MissingCountThreshold is > 0 and <= 10
            ? config.MissingCountThreshold
            : DefaultMissingCountThreshold;
        int memoryLimit = config.BetterGiMemoryLimitMB >= 0
            ? config.BetterGiMemoryLimitMB
            : DefaultBetterGiMemoryLimitMB;

        return config with
        {
            MemoryPercent = memoryPercent,
            MonitorIntervalSeconds = monitorInterval,
            MissingCountThreshold = missingCount,
            BetterGiMemoryLimitMB = memoryLimit
        };
    }

    public static long CalculateMemoryLimitMB(long totalMemoryMB, int memoryPercent)
    {
        int normalizedPercent = Normalize(new RuntimeConfig("", memoryPercent, 1, 1, false, 0)).MemoryPercent;
        return totalMemoryMB * normalizedPercent / 100;
    }
}

internal readonly record struct RuntimeConfig(
    string BetterGiPath,
    int MemoryPercent,
    int MonitorIntervalSeconds,
    int MissingCountThreshold,
    bool SkipSetup,
    int BetterGiMemoryLimitMB);
