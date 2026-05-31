using System.Reflection;

namespace BGIguard;

/// <summary>
/// BGIguard - BetterGI 进程守护程序。
/// </summary>
class Program
{
    private const int RestartDelayMs = 1000;
    private const int ProcessWaitExitMs = 3000;
    private const string BetterGiExeName = "BetterGI.exe";
    private const int MaxLogFiles = 7;
    private const string LogFilePrefix = "BGI_guard";

    private static string _exeDirectory = null!;
    private static string _betterGiExePath = "";
    private static readonly string _version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0";
    private static readonly string[] GameProcessNames = { "YuanShen", "GenshinImpact" };

    private static int _monitorIntervalMs = 5000;
    private static int _memoryPercent = 95;
    private static int _missingCountThreshold = 3;
    private static bool _skipSetup = false;
    private static int _betterGiMemoryLimitMB = 4096;

    private static AppLogger? _logger;
    private static ConfigService? _configService;
    private static BetterGiRuntimeService? _runtimeService;

    private static AppLogger LoggerStore => _logger ??= new AppLogger(_exeDirectory, LogFilePrefix, MaxLogFiles, GetDisplayVersion);
    private static string ConfigFilePath => Path.Combine(_exeDirectory, "BGIguard_config.json");
    private static ConfigService ConfigStore => _configService ??= new ConfigService(ConfigFilePath, Log);
    private static ConsoleUiService ConsoleUi => new(ConfigStore, ConfigFilePath, ClearConfigCache);
    private static BetterGiRuntimeService RuntimeService => _runtimeService ??= new BetterGiRuntimeService(BetterGiExeName, ProcessWaitExitMs, RestartDelayMs, Log);

    static void Main(string[] args)
    {
        _exeDirectory = AppDomain.CurrentDomain.BaseDirectory;

        string version = GetDisplayVersion();
        Console.Title = $"BetterGI 进程守护 v{version} By:Bcmdy";

        // 先处理 help/reset/set show 等命令，避免被 BetterGI 路径检测阻塞。
        if (args.Length > 0)
        {
            HandleCommandLine(args);
            return;
        }

        ApplyRuntimeConfig(LoadConfig());
        EnsureBetterGiPath();

        if (!_skipSetup)
        {
            ConsoleUi.ShowCommandLineSetup();
            ApplyRuntimeConfig(LoadConfig());
            EnsureBetterGiPath();
        }

        Log("INFO", "BGIguard 启动成功");
        Log("INFO", $"BetterGI 路径: {_betterGiExePath}");
        Log("INFO", $"进程内存阈值: {(_betterGiMemoryLimitMB > 0 ? $"{_betterGiMemoryLimitMB}MB" : "已禁用")}");

        RuntimeService.EnsureSingleInstance();
        RuntimeService.EnsureStartedAndCacheCommand(_betterGiExePath);

        RunGuardLoop();
    }

    private static string GetDisplayVersion()
    {
        var ver = typeof(Program).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
        return ver?.Version ?? _version;
    }

    private static void Log(string level, string message)
    {
        LoggerStore.Write(level, message);
    }

    private static void EnsureBetterGiPath()
    {
        if (!DetectBetterGiPath())
        {
            _betterGiExePath = ConsoleUi.PromptForBetterGiPath();
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

    private static (string betterGiPath, int memoryPercent, int monitorIntervalSeconds, int missingCountThreshold, bool skipSetup, int betterGiMemoryLimitMB) LoadConfig()
    {
        RuntimeConfig config = ConfigStore.Load();
        return (config.BetterGiPath, config.MemoryPercent, config.MonitorIntervalSeconds, config.MissingCountThreshold, config.SkipSetup, config.BetterGiMemoryLimitMB);
    }

    private static void ClearConfigCache()
    {
        ConfigStore.ClearCache();
    }

    private static void RunGuardLoop()
    {
        var runner = new GuardRunner(
            new GuardRunnerOptions(
                BetterGiExeName.Replace(".exe", ""),
                GameProcessNames,
                RuntimeService.CurrentUserSid,
                RuntimeService.CurrentUserName,
                ReloadGuardRunnerConfig,
                GetBetterGiSnapshotForRunner,
                GetRunningGameProcessesForRunner,
                () => MemoryMonitor.GetSystemMemory(Log),
                RestartBetterGiForRunner,
                Thread.Sleep,
                Log),
            new GuardRuntimeState
            {
                CachedCommand = RuntimeService.CachedCommand
            });

        runner.Run();
    }

    private static GuardRunnerConfig ReloadGuardRunnerConfig()
    {
        ApplyRuntimeConfig(LoadConfig());
        return new GuardRunnerConfig(
            _betterGiExePath,
            _monitorIntervalMs,
            _memoryPercent,
            _missingCountThreshold,
            _betterGiMemoryLimitMB,
            ProcessWaitExitMs,
            RestartDelayMs);
    }

    private static BetterGiProcessSnapshot GetBetterGiSnapshotForRunner(GuardRunnerConfig config)
    {
        return RuntimeService.GetBetterGiSnapshot(
            config.BetterGiExePath,
            includeCommandLine: true,
            includeMemory: config.BetterGiMemoryLimitMB > 0);
    }

    private static (bool AnyRunning, List<string> RunningNames) GetRunningGameProcessesForRunner()
    {
        return RuntimeService.GetRunningGameProcesses(GameProcessNames);
    }

    private static void RestartBetterGiForRunner(GuardRunnerConfig config, string cachedCommand)
    {
        RuntimeService.RestartBetterGi(config, cachedCommand);
    }

    private static void HandleCommandLine(string[] args)
    {
        string command = args[0].ToLower();

        switch (command)
        {
            case "set":
                if (args.Length == 2 && args[1].ToLower() == "show")
                {
                    ConsoleUi.ShowConfig();
                }
                else if (args.Length >= 2)
                {
                    ConsoleUi.HandleSetCommand(args);
                }
                else
                {
                    ConsoleUi.ShowHelp();
                }
                break;

            case "help":
            case "?":
                ConsoleUi.ShowHelp();
                break;

            case "reset":
                ConsoleUi.ResetConfig();
                break;

            default:
                var config = LoadConfig();
                ApplyRuntimeConfig(config);
                EnsureBetterGiPath();

                Log("INFO", "BGIguard 启动成功");
                RuntimeService.EnsureSingleInstance();
                RuntimeService.StartBetterGiProcess(_betterGiExePath);
                RunGuardLoop();
                break;
        }
    }

}
