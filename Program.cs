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
            ShowCommandLineSetup();
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
                    ShowConfig();
                }
                else if (args.Length >= 2)
                {
                    WriteConfigResult(HandleSetCommand(args));
                }
                else
                {
                    ShowHelp();
                }
                break;

            case "help":
            case "?":
                ShowHelp();
                break;

            case "reset":
                if (File.Exists(ConfigFilePath))
                {
                    File.Delete(ConfigFilePath);
                    ClearConfigCache();
                    Console.WriteLine("配置已重置为默认值");
                }
                else
                {
                    Console.WriteLine("配置已经是默认值");
                }
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

    private static CommandLineConfigResult HandleSetCommand(string[] args)
    {
        if (args.Length < 2)
            return CommandLineConfigResult.Failure("");

        string option = args[1].ToLower();
        if (option == "skip")
            return CommandLineConfigService.ToggleSkipSetup(ConfigStore);

        if (args.Length < 3)
        {
            ShowHelp();
            return CommandLineConfigResult.Failure("");
        }

        return option switch
        {
            "path" => CommandLineConfigService.SetPath(ConfigStore, args[2]),
            "memory" => CommandLineConfigService.SetMemory(ConfigStore, args[2]),
            "interval" => CommandLineConfigService.SetInterval(ConfigStore, args[2]),
            "count" => CommandLineConfigService.SetMissingCount(ConfigStore, args[2]),
            "memlimit" => CommandLineConfigService.SetProcessMemoryLimit(ConfigStore, args[2]),
            _ => ShowHelpAndReturnEmpty()
        };
    }

    private static void WriteConfigResult(CommandLineConfigResult result)
    {
        if (!string.IsNullOrEmpty(result.Message))
            Console.WriteLine(result.Message);
    }

    private static CommandLineConfigResult ShowHelpAndReturnEmpty()
    {
        ShowHelp();
        return CommandLineConfigResult.Failure("");
    }

    private static void ShowHelp()
    {
        Console.WriteLine("BGIguard 命令行工具");
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  BGIguard.exe                       启动守护进程（无参数）");
        Console.WriteLine("  BGIguard.exe set path <路径>       设置 BetterGI.exe 路径");
        Console.WriteLine("  BGIguard.exe set memory <值>       设置系统内存阈值 (1-100)");
        Console.WriteLine("  BGIguard.exe set interval <值>     设置监控间隔 (秒)");
        Console.WriteLine("  BGIguard.exe set count <值>        设置丢失计数阈值 (1-10)");
        Console.WriteLine("  BGIguard.exe set memlimit <值>     设置进程内存阈值 MB (0=禁用)");
        Console.WriteLine("  BGIguard.exe set skip              设置/取消跳过设置界面");
        Console.WriteLine("  BGIguard.exe set show              显示当前配置");
        Console.WriteLine("  BGIguard.exe reset                 重置配置为默认值");
        Console.WriteLine("  BGIguard.exe help                  显示帮助");
        Console.WriteLine();
        Console.WriteLine("默认值: 系统内存=85%, 监控间隔=5秒, 丢失计数=6次, 进程内存=4096MB, 跳过设置=false");
    }

    private static void ShowConfig()
    {
        var config = LoadConfig();
        Console.WriteLine("当前配置:");
        Console.WriteLine($"  BetterGI 路径: {config.betterGiPath}");
        Console.WriteLine($"  系统内存阈值: {config.memoryPercent}%");
        Console.WriteLine($"  监控间隔: {config.monitorIntervalSeconds} 秒");
        Console.WriteLine($"  丢失计数阈值: {config.missingCountThreshold} 次");
        Console.WriteLine($"  进程内存阈值: {(config.betterGiMemoryLimitMB > 0 ? $"{config.betterGiMemoryLimitMB}MB" : "已禁用")}");
        Console.WriteLine($"  跳过设置: {config.skipSetup}");
    }

    private static void ShowCommandLineSetup()
    {
        Console.WriteLine("=== BGIguard 设置 ===");
        Console.WriteLine();

        var config = LoadConfig();
        Console.WriteLine("当前配置:");
        Console.WriteLine($"  BetterGI 路径: {config.betterGiPath}");
        Console.WriteLine($"  系统内存阈值: {config.memoryPercent}%");
        Console.WriteLine($"  监控间隔: {config.monitorIntervalSeconds} 秒");
        Console.WriteLine($"  丢失计数阈值: {config.missingCountThreshold} 次");
        Console.WriteLine($"  进程内存阈值: {(config.betterGiMemoryLimitMB > 0 ? $"{config.betterGiMemoryLimitMB}MB" : "已禁用")}");
        Console.WriteLine();

        Console.WriteLine("请选择操作:");
        Console.WriteLine("  1. 修改 BetterGI 路径        (BetterGI.exe 完整路径)");
        Console.WriteLine("  2. 修改系统内存阈值          (1-100%，超阈值重启)");
        Console.WriteLine("  3. 修改监控间隔              (1-999 秒，检测频率)");
        Console.WriteLine("  4. 修改丢失计数阈值          (1-10 次，连续退出触发重启)");
        Console.WriteLine("  5. 修改进程内存阈值          (MB, 0=禁用)");
        Console.WriteLine("  6. 启动守护进程              (进入守护监控模式)");
        Console.WriteLine("  7. 跳过设置直接启动          (直接进入守护)");
        Console.WriteLine("  8. 重置配置                  (恢复默认设置)");
        Console.WriteLine("  9. 退出");
        Console.WriteLine();

        Console.Write("请输入选项 (1-9): ");
        string? input = Console.ReadLine();

        switch (input)
        {
            case "1":
                Console.Write("请输入 BetterGI.exe 路径（或拖入文件，可带引号）: ");
                WriteConfigResult(CommandLineConfigService.SetPath(ConfigStore, Console.ReadLine() ?? ""));
                break;

            case "2":
                Console.Write("请输入系统内存阈值 (1-100): ");
                WriteConfigResult(CommandLineConfigService.SetMemory(ConfigStore, Console.ReadLine() ?? ""));
                break;

            case "3":
                Console.Write("请输入监控间隔 (秒): ");
                WriteConfigResult(CommandLineConfigService.SetInterval(ConfigStore, Console.ReadLine() ?? ""));
                break;

            case "4":
                Console.Write("请输入丢失计数阈值 (1-10): ");
                WriteConfigResult(CommandLineConfigService.SetMissingCount(ConfigStore, Console.ReadLine() ?? ""));
                break;

            case "5":
                Console.Write("请输入进程内存阈值 (MB, 0=禁用): ");
                WriteConfigResult(CommandLineConfigService.SetProcessMemoryLimit(ConfigStore, Console.ReadLine() ?? ""));
                break;

            case "6":
                break;

            case "7":
                WriteConfigResult(CommandLineConfigService.SetSkipSetup(ConfigStore, true));
                break;

            case "8":
                if (File.Exists(ConfigFilePath))
                {
                    File.Delete(ConfigFilePath);
                    ClearConfigCache();
                    Console.WriteLine("配置已重置");
                }
                break;

            case "9":
                Environment.Exit(0);
                break;

            default:
                break;
        }

        Console.WriteLine();
    }
}
