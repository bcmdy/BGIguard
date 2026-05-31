namespace BGIguard;

partial class Program
{
    private static string ConfigFilePath => Path.Combine(_exeDirectory, "BGIguard_config.json");

    // 配置类
    private class Config
    {
        public string BetterGiPath { get; set; } = "";
        public int MemoryPercent { get; set; } = 85;
        public int MonitorInterval { get; set; } = 5;
        public int MissingCount { get; set; } = 6;
        public bool SkipSetup { get; set; }
        /// <summary>
        /// BetterGI 进程内存阈值（MB），超过则重启。0 表示禁用进程级监控。
        /// </summary>
        public int BetterGiMemoryLimitMB { get; set; } = 4096;
    }

    // 配置缓存
    private static (string betterGiPath, int memoryPercent, int monitorIntervalSeconds, int missingCountThreshold, bool skipSetup, int betterGiMemoryLimitMB)? _configCache = null;
    private static DateTime _configCacheLastWriteUtc = DateTime.MinValue;
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { WriteIndented = true };


    /// <summary>
    /// 确保已设置可用的 BetterGI.exe 路径。
    /// </summary>
    private static void EnsureBetterGiPath()
    {
        if (!DetectBetterGiPath())
        {
            PromptForBetterGiPath();
        }
    }

    /// <summary>
    /// 应用运行时配置，供启动和热更新复用。
    /// </summary>
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

    /// <summary>
    /// 检测 BetterGI.exe 路径
    /// </summary>
    private static bool DetectBetterGiPath()
    {
        // 1. 先检测自身目录下是否存在
        string localPath = Path.Combine(_exeDirectory, BetterGiExeName);
        if (File.Exists(localPath))
        {
            _betterGiExePath = localPath;
            return true;
        }

        // 2. 检查配置文件中的路径
        var config = LoadConfig();
        if (!string.IsNullOrEmpty(config.betterGiPath) && File.Exists(config.betterGiPath))
        {
            _betterGiExePath = config.betterGiPath;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 强制要求用户输入 BetterGI.exe 路径
    /// </summary>
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

    /// <summary>
    /// 加载配置（带缓存）
    /// </summary>
    private static (string betterGiPath, int memoryPercent, int monitorIntervalSeconds, int missingCountThreshold, bool skipSetup, int betterGiMemoryLimitMB) LoadConfig()
    {
        DateTime lastWriteUtc = GetConfigLastWriteUtc();
        if (_configCache.HasValue && _configCacheLastWriteUtc == lastWriteUtc)
            return _configCache.Value;

        var config = new Config();
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                var json = File.ReadAllText(ConfigFilePath);
                config = System.Text.Json.JsonSerializer.Deserialize<Config>(json) ?? new Config();
            }
            // 验证并修正
            var normalized = ConfigService.Normalize(new RuntimeConfig(
                config.BetterGiPath,
                config.MemoryPercent,
                config.MonitorInterval,
                config.MissingCount,
                config.SkipSetup,
                config.BetterGiMemoryLimitMB));
            config.MemoryPercent = normalized.MemoryPercent;
            config.MonitorInterval = normalized.MonitorIntervalSeconds;
            config.MissingCount = normalized.MissingCountThreshold;
            config.BetterGiMemoryLimitMB = normalized.BetterGiMemoryLimitMB;
        }
        catch (Exception ex)
        {
            Log("ERROR", $"加载配置文件失败: {ex.Message}");
        }

        var result = (config.BetterGiPath, config.MemoryPercent, config.MonitorInterval, config.MissingCount, config.SkipSetup, config.BetterGiMemoryLimitMB);
        _configCache = result;
        _configCacheLastWriteUtc = lastWriteUtc;
        return result;
    }

    /// <summary>
    /// 获取配置文件最后修改时间，用于配置热更新。
    /// </summary>
    private static DateTime GetConfigLastWriteUtc()
    {
        try
        {
            return File.Exists(ConfigFilePath) ? File.GetLastWriteTimeUtc(ConfigFilePath) : DateTime.MinValue;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// 清空配置缓存。
    /// </summary>
    private static void ClearConfigCache()
    {
        _configCache = null;
        _configCacheLastWriteUtc = DateTime.MinValue;
    }

    /// <summary>
    /// 保存配置
    /// </summary>
    private static void SaveConfig(int memoryPercent, int monitorIntervalSeconds, int missingCountThreshold, bool skipSetup, int betterGiMemoryLimitMB)
    {
        ClearConfigCache();
        var config = new Config
        {
            MemoryPercent = memoryPercent,
            MonitorInterval = monitorIntervalSeconds,
            MissingCount = missingCountThreshold,
            SkipSetup = skipSetup,
            BetterGiMemoryLimitMB = betterGiMemoryLimitMB
        };
        var existing = LoadConfig();
        config.BetterGiPath = existing.betterGiPath;
        SaveConfigFile(config);
    }

    /// <summary>
    /// 保存配置文件
    /// </summary>
    private static void SaveConfigFile(Config config)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigFilePath, json);
        ClearConfigCache();
    }

    /// <summary>
    /// 验证并规范化可执行文件路径
    /// </summary>
    private static bool ValidateAndNormalizePath(string path, out string normalizedPath)
    {
        normalizedPath = path.Trim().Trim('"');

        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            Log("ERROR", "路径不能为空");
            return false;
        }

        if (!File.Exists(normalizedPath))
        {
            Log("ERROR", $"文件不存在: {normalizedPath}");
            return false;
        }

        string extension = Path.GetExtension(normalizedPath);
        if (!string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase))
        {
            Log("ERROR", $"不是有效的可执行文件 (.exe): {extension}");
            return false;
        }

        // 规范化路径
        normalizedPath = Path.GetFullPath(normalizedPath);
        return true;
    }

    /// <summary>
    /// 保存路径配置
    /// </summary>
    private static void SaveConfigPath(string path)
    {
        if (!ValidateAndNormalizePath(path, out string normalizedPath))
            return;

        ClearConfigCache();
        var existing = LoadConfig();
        var config = new Config
        {
            BetterGiPath = normalizedPath,
            MemoryPercent = existing.memoryPercent,
            MonitorInterval = existing.monitorIntervalSeconds,
            MissingCount = existing.missingCountThreshold,
            SkipSetup = existing.skipSetup,
            BetterGiMemoryLimitMB = existing.betterGiMemoryLimitMB
        };
        SaveConfigFile(config);
    }
}
