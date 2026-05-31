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
    private static readonly char[] DangerousCmdArgumentChars = { '&', '|', '<', '>', '^', '%', '\r', '\n' };

    private static string _exeDirectory = null!;
    private static string _betterGiExePath = "";
    private static string _cachedCommand = "";
    private static Mutex? _mutex;
    private static readonly string _version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0";
    private static readonly string _currentUserSid = CurrentUserService.GetCurrentUserSid();
    private static readonly string _currentUserName = CurrentUserService.GetCurrentUserDisplayName();
    private static readonly string[] GameProcessNames = { "YuanShen", "GenshinImpact" };

    private static int _monitorIntervalMs = 5000;
    private static int _memoryPercent = 95;
    private static int _missingCountThreshold = 3;
    private static bool _skipSetup = false;
    private static int _betterGiMemoryLimitMB = 4096;

    private static AppLogger? _logger;
    private static ConfigService? _configService;

    private static AppLogger LoggerStore => _logger ??= new AppLogger(_exeDirectory, LogFilePrefix, MaxLogFiles, GetDisplayVersion);
    private static string ConfigFilePath => Path.Combine(_exeDirectory, "BGIguard_config.json");
    private static ConfigService ConfigStore => _configService ??= new ConfigService(ConfigFilePath, Log);
    private static ConsoleUiService ConsoleUi => new(ConfigStore, ConfigFilePath, ClearConfigCache);

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

        HandleSingleInstance();

        bool alreadyRunning = ProcessService.GetOwnedProcessSnapshot(
            BetterGiExeName.Replace(".exe", ""),
            _betterGiExePath,
            _currentUserSid,
            _currentUserName,
            includeCommandLine: false,
            includeMemory: false,
            Log).Exists;

        if (!alreadyRunning)
        {
            StartBetterGiProcess();
        }
        else
        {
            Log("INFO", "BetterGI.exe 已在运行中，跳过启动");
        }

        Thread.Sleep(500);
        var initialSnapshot = ProcessService.GetOwnedProcessSnapshot(
            BetterGiExeName.Replace(".exe", ""),
            _betterGiExePath,
            _currentUserSid,
            _currentUserName,
            includeCommandLine: true,
            includeMemory: false,
            Log);

        if (initialSnapshot.Exists && initialSnapshot.CommandLine != null)
        {
            _cachedCommand = CommandLine.ExtractArgs(initialSnapshot.CommandLine);
            Log("INFO", $"已缓存启动命令: {_cachedCommand}");
        }

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

    private static void SaveConfigPath(string path)
    {
        ConfigStore.SavePath(path, out _);
    }

    private static void HandleSingleInstance()
    {
        _mutex = ProcessService.EnsureSingleInstance(
            "BGIguard_SingleInstance_Mutex",
            ProcessWaitExitMs,
            _currentUserSid,
            _currentUserName,
            Log);
    }

    private static void StartBetterGiProcess(string? commandLine = null)
    {
        ProcessService.StartBetterGiProcess(
            _betterGiExePath,
            commandLine ?? _cachedCommand,
            DangerousCmdArgumentChars,
            Log);
    }

    private static void RunGuardLoop()
    {
        var runner = new GuardRunner(
            new GuardRunnerOptions(
                BetterGiExeName.Replace(".exe", ""),
                GameProcessNames,
                _currentUserSid,
                _currentUserName,
                ReloadGuardRunnerConfig,
                GetBetterGiSnapshotForRunner,
                GetRunningGameProcessesForRunner,
                () => MemoryMonitor.GetSystemMemory(Log),
                RestartBetterGiForRunner,
                Thread.Sleep,
                Log),
            new GuardRuntimeState
            {
                CachedCommand = _cachedCommand
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
        return ProcessService.GetOwnedProcessSnapshot(
            BetterGiExeName.Replace(".exe", ""),
            config.BetterGiExePath,
            _currentUserSid,
            _currentUserName,
            includeCommandLine: true,
            includeMemory: config.BetterGiMemoryLimitMB > 0,
            Log);
    }

    private static (bool AnyRunning, List<string> RunningNames) GetRunningGameProcessesForRunner()
    {
        return ProcessService.GetRunningOwnedProcesses(GameProcessNames, _currentUserSid, _currentUserName);
    }

    private static void RestartBetterGiForRunner(GuardRunnerConfig config, string cachedCommand)
    {
        ProcessService.TerminateProcessesByCurrentUser(
            BetterGiExeName.Replace(".exe", ""),
            "BetterGI.exe",
            excludePid: null,
            config.ProcessWaitExitMs,
            _currentUserSid,
            _currentUserName,
            Log);

        Thread.Sleep(config.RestartDelayMs);
        ProcessService.StartBetterGiProcess(
            config.BetterGiExePath,
            cachedCommand,
            DangerousCmdArgumentChars,
            Log);
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
                HandleSingleInstance();
                StartBetterGiProcess();
                RunGuardLoop();
                break;
        }
    }

}
