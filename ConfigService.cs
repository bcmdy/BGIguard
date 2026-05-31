namespace BGIguard;

internal sealed class ConfigService
{
    public const int DefaultMemoryPercent = 85;
    public const int DefaultMonitorIntervalSeconds = 5;
    public const int DefaultMissingCountThreshold = 6;
    public const int DefaultBetterGiMemoryLimitMB = 4096;

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _configFilePath;
    private readonly Action<string, string> _log;
    private RuntimeConfig? _cache;
    private DateTime _cacheLastWriteUtc = DateTime.MinValue;

    public ConfigService(string configFilePath, Action<string, string> log)
    {
        _configFilePath = configFilePath;
        _log = log;
    }

    public RuntimeConfig Load()
    {
        DateTime lastWriteUtc = GetLastWriteUtc();
        if (_cache.HasValue && _cacheLastWriteUtc == lastWriteUtc)
            return _cache.Value;

        var fileConfig = new ConfigFileModel();
        try
        {
            if (File.Exists(_configFilePath))
            {
                var json = File.ReadAllText(_configFilePath);
                fileConfig = System.Text.Json.JsonSerializer.Deserialize<ConfigFileModel>(json) ?? new ConfigFileModel();
            }
        }
        catch (Exception ex)
        {
            _log("ERROR", $"加载配置文件失败: {ex.Message}");
        }

        RuntimeConfig normalized = Normalize(fileConfig.ToRuntimeConfig());
        _cache = normalized;
        _cacheLastWriteUtc = lastWriteUtc;
        return normalized;
    }

    public void SaveSettings(int memoryPercent, int monitorIntervalSeconds, int missingCountThreshold, bool skipSetup, int betterGiMemoryLimitMB)
    {
        ClearCache();
        RuntimeConfig existing = Load();
        Save(new RuntimeConfig(
            existing.BetterGiPath,
            memoryPercent,
            monitorIntervalSeconds,
            missingCountThreshold,
            skipSetup,
            betterGiMemoryLimitMB));
    }

    public bool SavePath(string path, out string normalizedPath)
    {
        PathValidationResult result = PathService.ValidateExecutablePath(path);
        normalizedPath = result.NormalizedPath;

        if (!result.IsValid)
        {
            _log("ERROR", result.ErrorMessage ?? "路径无效");
            return false;
        }

        ClearCache();
        RuntimeConfig existing = Load();
        Save(existing with { BetterGiPath = normalizedPath });
        return true;
    }

    public void ClearCache()
    {
        _cache = null;
        _cacheLastWriteUtc = DateTime.MinValue;
    }

    public DateTime GetLastWriteUtc()
    {
        try
        {
            return File.Exists(_configFilePath) ? File.GetLastWriteTimeUtc(_configFilePath) : DateTime.MinValue;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

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

    private void Save(RuntimeConfig config)
    {
        var fileConfig = ConfigFileModel.FromRuntimeConfig(config);
        var json = System.Text.Json.JsonSerializer.Serialize(fileConfig, JsonOptions);
        File.WriteAllText(_configFilePath, json);
        ClearCache();
    }

    private sealed class ConfigFileModel
    {
        public string BetterGiPath { get; set; } = "";
        public int MemoryPercent { get; set; } = DefaultMemoryPercent;
        public int MonitorInterval { get; set; } = DefaultMonitorIntervalSeconds;
        public int MissingCount { get; set; } = DefaultMissingCountThreshold;
        public bool SkipSetup { get; set; }
        public int BetterGiMemoryLimitMB { get; set; } = DefaultBetterGiMemoryLimitMB;

        public RuntimeConfig ToRuntimeConfig()
        {
            return new RuntimeConfig(BetterGiPath, MemoryPercent, MonitorInterval, MissingCount, SkipSetup, BetterGiMemoryLimitMB);
        }

        public static ConfigFileModel FromRuntimeConfig(RuntimeConfig config)
        {
            return new ConfigFileModel
            {
                BetterGiPath = config.BetterGiPath,
                MemoryPercent = config.MemoryPercent,
                MonitorInterval = config.MonitorIntervalSeconds,
                MissingCount = config.MissingCountThreshold,
                SkipSetup = config.SkipSetup,
                BetterGiMemoryLimitMB = config.BetterGiMemoryLimitMB
            };
        }
    }
}

internal readonly record struct RuntimeConfig(
    string BetterGiPath,
    int MemoryPercent,
    int MonitorIntervalSeconds,
    int MissingCountThreshold,
    bool SkipSetup,
    int BetterGiMemoryLimitMB);
