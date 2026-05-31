namespace BGIguard;

internal static class CommandLineConfigService
{
    public static CommandLineConfigResult SetPath(ConfigService configStore, string path)
    {
        if (configStore.SavePath(path, out string normalizedPath))
            return CommandLineConfigResult.Success($"BetterGI路径已设置为: {normalizedPath}");

        return CommandLineConfigResult.Failure($"错误: 文件不存在或不是有效的 .exe: {path}");
    }

    public static CommandLineConfigResult SetMemory(ConfigService configStore, string value)
    {
        if (!int.TryParse(value, out int memoryPercent) || memoryPercent is <= 0 or > 100)
            return CommandLineConfigResult.Failure("错误: 内存阈值应在 1-100 之间");

        RuntimeConfig config = configStore.Load();
        configStore.SaveSettings(memoryPercent, config.MonitorIntervalSeconds, config.MissingCountThreshold, config.SkipSetup, config.BetterGiMemoryLimitMB);
        return CommandLineConfigResult.Success($"内存阈值已设置为 {memoryPercent}%");
    }

    public static CommandLineConfigResult SetInterval(ConfigService configStore, string value)
    {
        if (!int.TryParse(value, out int interval) || interval <= 0)
            return CommandLineConfigResult.Failure("错误: 监控间隔应大于 0");

        RuntimeConfig config = configStore.Load();
        configStore.SaveSettings(config.MemoryPercent, interval, config.MissingCountThreshold, config.SkipSetup, config.BetterGiMemoryLimitMB);
        return CommandLineConfigResult.Success($"监控间隔已设置为 {interval} 秒");
    }

    public static CommandLineConfigResult SetMissingCount(ConfigService configStore, string value)
    {
        if (!int.TryParse(value, out int count) || count is <= 0 or > 10)
            return CommandLineConfigResult.Failure("错误: 丢失计数阈值应在 1-10 之间");

        RuntimeConfig config = configStore.Load();
        configStore.SaveSettings(config.MemoryPercent, config.MonitorIntervalSeconds, count, config.SkipSetup, config.BetterGiMemoryLimitMB);
        return CommandLineConfigResult.Success($"丢失计数阈值已设置为 {count} 次");
    }

    public static CommandLineConfigResult ToggleSkipSetup(ConfigService configStore)
    {
        RuntimeConfig config = configStore.Load();
        bool newSkip = !config.SkipSetup;
        configStore.SaveSettings(config.MemoryPercent, config.MonitorIntervalSeconds, config.MissingCountThreshold, newSkip, config.BetterGiMemoryLimitMB);
        return CommandLineConfigResult.Success($"跳过设置界面已设置为: {newSkip}");
    }

    public static CommandLineConfigResult SetProcessMemoryLimit(ConfigService configStore, string value)
    {
        if (!int.TryParse(value, out int limit) || limit < 0)
            return CommandLineConfigResult.Failure("错误: 进程内存阈值应为 >= 0 的整数 (0 表示禁用)");

        RuntimeConfig config = configStore.Load();
        configStore.SaveSettings(config.MemoryPercent, config.MonitorIntervalSeconds, config.MissingCountThreshold, config.SkipSetup, limit);
        return CommandLineConfigResult.Success(limit == 0
            ? "进程内存监控已禁用"
            : $"进程内存阈值已设置为 {limit}MB");
    }
}

internal readonly record struct CommandLineConfigResult(bool IsSuccess, string Message)
{
    public static CommandLineConfigResult Success(string message) => new(true, message);
    public static CommandLineConfigResult Failure(string message) => new(false, message);
}
