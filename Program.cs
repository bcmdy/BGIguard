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
    private static readonly string[] GameProcessNames = { "YuanShen", "GenshinImpact" };

    private static string _exeDirectory = null!;
    private static readonly string _version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0";

    private static AppLogger? _logger;
    private static ConfigService? _configService;
    private static ConsoleUiService? _consoleUiService;
    private static BetterGiRuntimeService? _runtimeService;
    private static RuntimeConfigProvider? _runtimeConfigProvider;
    private static GuardLoopService? _guardLoopService;

    private static AppLogger LoggerStore => _logger ??= new AppLogger(_exeDirectory, LogFilePrefix, MaxLogFiles, GetDisplayVersion);
    private static string ConfigFilePath => Path.Combine(_exeDirectory, "BGIguard_config.json");
    private static ConfigService ConfigStore => _configService ??= new ConfigService(ConfigFilePath, Log);
    private static ConsoleUiService ConsoleUi => _consoleUiService ??= new ConsoleUiService(ConfigStore, ConfigFilePath, ClearConfigCache);
    private static BetterGiRuntimeService RuntimeService => _runtimeService ??= new BetterGiRuntimeService(BetterGiExeName, ProcessWaitExitMs, RestartDelayMs, Log);
    private static RuntimeConfigProvider RuntimeConfigProvider => _runtimeConfigProvider ??= new RuntimeConfigProvider(ConfigStore, _exeDirectory, BetterGiExeName, ConsoleUi);
    private static GuardLoopService GuardLoop => _guardLoopService ??= new GuardLoopService(BetterGiExeName, GameProcessNames, RuntimeConfigProvider, RuntimeService, ProcessWaitExitMs, RestartDelayMs, Log);

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

        PrepareRuntime();
        if (!RuntimeConfigProvider.SkipSetup)
        {
            ConsoleUi.ShowCommandLineSetup();
            PrepareRuntime();
        }

        StartGuard();
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

    private static void ClearConfigCache()
    {
        ConfigStore.ClearCache();
        _runtimeConfigProvider = null;
    }

    private static void PrepareRuntime()
    {
        RuntimeConfigProvider.Reload();
        RuntimeConfigProvider.EnsureBetterGiPath();
    }

    private static void StartGuard(bool ensureStarted = true)
    {
        Log("INFO", "BGIguard 启动成功");
        Log("INFO", $"BetterGI 路径: {RuntimeConfigProvider.BetterGiExePath}");
        Log("INFO", $"进程内存阈值: {(RuntimeConfigProvider.BetterGiMemoryLimitMB > 0 ? $"{RuntimeConfigProvider.BetterGiMemoryLimitMB}MB" : "已禁用")}");

        RuntimeService.EnsureSingleInstance();
        if (ensureStarted)
        {
            RuntimeService.EnsureStartedAndCacheCommand(RuntimeConfigProvider.BetterGiExePath);
        }
        else
        {
            RuntimeService.StartBetterGiProcess(RuntimeConfigProvider.BetterGiExePath);
        }

        GuardLoop.Run();
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
                PrepareRuntime();
                StartGuard(ensureStarted: false);
                break;
        }
    }
}
