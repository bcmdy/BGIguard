using System.Reflection;

namespace BGIguard;

/// <summary>
/// BGIguard - BetterGI 守护程序
/// </summary>
partial class Program
{

    // ============== 配置常量 ==============
    private const int RestartDelayMs = 1000;
    private const int ProcessWaitExitMs = 3000;
    private const string BetterGiExeName = "BetterGI.exe";
    private static readonly char[] DangerousCmdArgumentChars = { '&', '|', '<', '>', '^', '%', '\r', '\n' };

    // ============== 全局变量 ==============
    private static string _exeDirectory = null!;
    private static string _betterGiExePath = "";
    private static string _cachedCommand = "";
    private static Mutex? _mutex;
    private static readonly string _version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0";
    private static readonly string _currentUserSid = CurrentUserService.GetCurrentUserSid();
    private static readonly string _currentUserName = CurrentUserService.GetCurrentUserDisplayName();

    // 游戏进程名
    private static readonly string[] GameProcessNames = { "YuanShen", "GenshinImpact" };

    // 运行时配置
    private static int _monitorIntervalMs = 5000;
    private static int _memoryPercent = 95;
    private static int _missingCountThreshold = 3;
    private static bool _skipSetup = false;
    private static int _betterGiMemoryLimitMB = 4096;

    // 获取显示版本
    private static string GetDisplayVersion()
    {
        var ver = typeof(Program).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
        return ver?.Version ?? _version;
    }

    static void Main(string[] args)
    {
        _exeDirectory = AppDomain.CurrentDomain.BaseDirectory;

        // 设置控制台窗口标题
        string version = GetDisplayVersion();
        Console.Title = $"BetterGI 进程守护 v{version} By:Bcmdy";

        // 先处理命令行参数，避免 help/reset/set show 等命令被 BetterGI 路径检测阻塞
        if (args.Length > 0)
        {
            HandleCommandLine(args);
            return;
        }

        // 加载配置
        var initConfig = LoadConfig();
        ApplyRuntimeConfig(initConfig);

        // 检测 BetterGI.exe 路径
        EnsureBetterGiPath();

        // 无参数启动时显示设置界面（除非已跳过）
        if (!_skipSetup)
        {
            ShowCommandLineSetup();
            ApplyRuntimeConfig(LoadConfig());
            EnsureBetterGiPath();
        }

        // 记录启动日志
        Log("INFO", "BGIguard 启动成功");
        Log("INFO", $"BetterGI路径: {_betterGiExePath}");
        Log("INFO", $"进程内存阈值: {(_betterGiMemoryLimitMB > 0 ? $"{_betterGiMemoryLimitMB}MB" : "已禁用")}");

        // 处理单实例保护
        HandleSingleInstance();

        // 检查 BetterGI 是否已运行，若已运行则不重复启动
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
            // 未运行，启动 BetterGI.exe
            StartBetterGiProcess();
        }
        else
        {
            Log("INFO", "BetterGI.exe 已在运行中，跳过启动");
        }

        // 启动后立即缓存启动命令
        Thread.Sleep(500); // 等待进程启动
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

        // 进入守护主循环
        RunGuardLoop();
    }

}
