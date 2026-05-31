namespace BGIguard;

partial class Program
{
    private static string ConfigFilePath => Path.Combine(_exeDirectory, "BGIguard_config.json");
    private static ConfigService? _configService;
    private static ConfigService ConfigStore => _configService ??= new ConfigService(ConfigFilePath, Log);

    private static void EnsureBetterGiPath()
    {
        if (!DetectBetterGiPath())
        {
            PromptForBetterGiPath();
        }
    }

    private static void ApplyRuntimeConfig((string betterGiPath, int memoryPercent, int monitorIntervalSeconds, int missingCountThreshold, bool skipSetup, int betterGiMemoryLimitMB) config)
    {
        _skipSetup = config.skipSetup;
        _monitorIntervalMs = config.monitorIntervalSeconds * 1000;
        _memoryPercent = config.memoryPercent;
        _missingCountThreshold = config.missingCountThreshold;
        _betterGiMemoryLimitMB = config.betterGiMemoryLimitMB;

        if (!string.IsNullOrEmpty(config.betterGiPath) &&
            File.Exists(config.betterGiPath) &&
            !string.Equals(_betterGiExePath, config.betterGiPath, StringComparison.OrdinalIgnoreCase))
        {
            _betterGiExePath = config.betterGiPath;
        }
    }

    private static bool DetectBetterGiPath()
    {
        string localPath = Path.Combine(_exeDirectory, BetterGiExeName);
        if (File.Exists(localPath))
        {
            _betterGiExePath = localPath;
            return true;
        }

        var config = LoadConfig();
        if (!string.IsNullOrEmpty(config.betterGiPath) && File.Exists(config.betterGiPath))
        {
            _betterGiExePath = config.betterGiPath;
            return true;
        }

        return false;
    }

    private static void PromptForBetterGiPath()
    {
        Console.WriteLine("错误: 未找到 BetterGI.exe");
        Console.WriteLine("请设置 BetterGI.exe 路径:");
        Console.Write("> ");
        string? pathInput = Console.ReadLine();
        if (!string.IsNullOrEmpty(pathInput))
        {
            pathInput = pathInput.Trim().Trim('"');
        }
        while (string.IsNullOrWhiteSpace(pathInput) || !File.Exists(pathInput))
        {
            Console.WriteLine("文件不存在，请重新输入 BetterGI.exe 路径:");
            Console.Write("> ");
            pathInput = Console.ReadLine();
        }
        SaveConfigPath(pathInput);
        _betterGiExePath = pathInput;
        Console.WriteLine($"路径已设置为: {_betterGiExePath}");
    }

    private static (string betterGiPath, int memoryPercent, int monitorIntervalSeconds, int missingCountThreshold, bool skipSetup, int betterGiMemoryLimitMB) LoadConfig()
    {
        RuntimeConfig config = ConfigStore.Load();
        return (config.BetterGiPath, config.MemoryPercent, config.MonitorIntervalSeconds, config.MissingCountThreshold, config.SkipSetup, config.BetterGiMemoryLimitMB);
    }

    private static void ClearConfigCache()
    {
        ConfigStore.ClearCache();
    }

    private static void SaveConfig(int memoryPercent, int monitorIntervalSeconds, int missingCountThreshold, bool skipSetup, int betterGiMemoryLimitMB)
    {
        ConfigStore.SaveSettings(memoryPercent, monitorIntervalSeconds, missingCountThreshold, skipSetup, betterGiMemoryLimitMB);
    }

    private static void SaveConfigPath(string path)
    {
        ConfigStore.SavePath(path, out _);
    }
}
