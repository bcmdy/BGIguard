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
        var config = LoadConfig();
        string resolvedPath = PathService.ResolveBetterGiPath(_exeDirectory, BetterGiExeName, config.betterGiPath);
        if (string.IsNullOrEmpty(resolvedPath))
            return false;

        _betterGiExePath = resolvedPath;
        return true;
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
        PathValidationResult validation = PathService.ValidateExecutablePath(pathInput ?? "");
        while (!validation.IsValid)
        {
            Console.WriteLine("文件不存在或不是有效的 .exe，请重新输入 BetterGI.exe 路径:");
            Console.Write("> ");
            pathInput = Console.ReadLine();
            validation = PathService.ValidateExecutablePath(pathInput ?? "");
        }
        SaveConfigPath(validation.NormalizedPath);
        _betterGiExePath = validation.NormalizedPath;
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
