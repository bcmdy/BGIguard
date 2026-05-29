using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace BGIguard;

/// <summary>
/// BGIguard - BetterGI 守护程序
/// </summary>
class Program
{
    // ============== P/Invoke API ==============
    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref PROCESS_BASIC_INFORMATION processInformation,
        int processInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        byte[] lpBuffer,
        int dwSize,
        out int lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        int dwDesiredAccess,
        bool bInheritHandle,
        int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    // ============== P/Invoke API（进程所有者查询） ==============
    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenUser = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct SID_AND_ATTRIBUTES
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_USER
    {
        public SID_AND_ATTRIBUTES User;
    }

    private readonly record struct ProcessOwnerInfo(string UserName, string Sid)
    {
        public bool HasIdentity => !string.IsNullOrEmpty(Sid);
        public string Display => string.IsNullOrEmpty(UserName) ? $"SID:{Sid}" : $"用户:{UserName}, SID:{Sid}";
    }

    private readonly record struct BetterGiProcessSnapshot(bool Exists, string? CommandLine, long MemoryMB);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass, IntPtr TokenInformation, int TokenInformationLength, out int ReturnLength);

    [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool LookupAccountSid(string? lpSystemName, IntPtr Sid, StringBuilder lpName, ref int cchName, StringBuilder? lpReferencedDomainName, ref int cchReferencedDomainName, out int peUse);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    private const int PROCESS_QUERY_INFORMATION = 0x0400;
    private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int PROCESS_VM_READ = 0x0010;
    private const int ProcessBasicInformation = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr Reserved3;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    // ============== 配置常量 ==============
    private const int RestartDelayMs = 1000;
    private const int MaxLogFiles = 7;
    private const int ProcessWaitExitMs = 3000;
    private const string BetterGiExeName = "BetterGI.exe";
    private const string LogFilePrefix = "BGI_guard";
    private static readonly char[] DangerousCmdArgumentChars = { '&', '|', '<', '>', '^', '%', '\r', '\n' };
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

    // PEB 偏移量（动态获取）
    private static readonly int PEB_OFFSET;
    private static readonly int CMDLINE_OFFSET;

    static Program()
    {
        // PEB 偏移量（基于 Windows 系统架构）
        // ProcessParameters 在 PEB 中的偏移量
        PEB_OFFSET = IntPtr.Size == 8 ? 0x20 : 0x10;
        // CommandLine 在 ProcessParameters 中的偏移量
        CMDLINE_OFFSET = IntPtr.Size == 8 ? 0x70 : 0x40;
    }

    // ============== 全局变量 ==============
    private static string _exeDirectory = null!;
    private static string _betterGiExePath = "";
    private static string _cachedCommand = "";
    private static Mutex? _mutex;
    private static readonly object _logLock = new();
    private static readonly string _version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0";
    private static readonly string _currentUserSid = GetCurrentUserSid();
    private static readonly string _currentUserName = GetCurrentUserDisplayName();

    // 游戏进程名
    private static readonly string[] GameProcessNames = { "YuanShen", "GenshinImpact" };

    // 运行时配置
    private static int _monitorIntervalMs = 5000;
    private static int _memoryPercent = 95;
    private static int _missingCountThreshold = 3;
    private static bool _skipSetup = false;
    private static int _betterGiMemoryLimitMB = 4096;

    // 丢失计数
    private static int _missingCount = 0;
    private static int _gameExitCount = 0;

    // 配置缓存
    private static (string betterGiPath, int memoryPercent, int monitorIntervalSeconds, int missingCountThreshold, bool skipSetup, int betterGiMemoryLimitMB)? _configCache = null;
    private static DateTime _configCacheLastWriteUtc = DateTime.MinValue;
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // 日志清理状态
    private static DateTime _lastLogCleanupDate = DateTime.MinValue;

    // 获取显示版本
    private static string GetDisplayVersion()
    {
        var ver = typeof(Program).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
        return ver?.Version ?? _version;
    }

    private static string GetCurrentUserSid()
    {
        try
        {
            return WindowsIdentity.GetCurrent().User?.Value ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string GetCurrentUserDisplayName()
    {
        string domain = Environment.UserDomainName;
        string user = Environment.UserName;
        return string.IsNullOrEmpty(domain) ? user : $@"{domain}\{user}";
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
        bool alreadyRunning = IsBetterGiRunningByUser();
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
        var (exists, initialCommand) = GetBetterGiInfo();
        if (exists && initialCommand != null)
        {
            _cachedCommand = ExtractArgs(initialCommand);
            Log("INFO", $"已缓存启动命令: {_cachedCommand}");
        }

        // 进入守护主循环
        RunGuardLoop();
    }

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
    /// 处理命令行参数
    /// </summary>
    private static void HandleCommandLine(string[] args)
    {
        string command = args[0].ToLower();

        switch (command)
        {
            case "set":
                if (args.Length >= 3)
                {
                    if (args[1].ToLower() == "path")
                    {
                        // 设置 BetterGI 路径
                        string newPath = args[2].Trim().Trim('"');
                        if (File.Exists(newPath))
                        {
                            SaveConfigPath(newPath);
                            Console.WriteLine($"BetterGI路径已设置为: {newPath}");
                        }
                        else
                        {
                            Console.WriteLine($"错误: 文件不存在: {newPath}");
                        }
                    }
                    else if (args[1].ToLower() == "memory")
                    {
                        if (int.TryParse(args[2], out int mem) && mem > 0 && mem <= 100)
                        {
                            var cfg = LoadConfig();
                            SaveConfig(mem, cfg.monitorIntervalSeconds, cfg.missingCountThreshold, cfg.skipSetup, cfg.betterGiMemoryLimitMB);
                            Console.WriteLine($"内存阈值已设置为 {mem}%");
                        }
                        else
                        {
                            Console.WriteLine("错误: 内存阈值应在 1-100 之间");
                        }
                    }
                    else if (args[1].ToLower() == "interval")
                    {
                        if (int.TryParse(args[2], out int interval) && interval > 0)
                        {
                            var cfg = LoadConfig();
                            SaveConfig(cfg.memoryPercent, interval, cfg.missingCountThreshold, cfg.skipSetup, cfg.betterGiMemoryLimitMB);
                            Console.WriteLine($"监控间隔已设置为 {interval} 秒");
                        }
                        else
                        {
                            Console.WriteLine("错误: 监控间隔应大于 0");
                        }
                    }
                    else if (args[1].ToLower() == "count")
                    {
                        if (int.TryParse(args[2], out int count) && count > 0 && count <= 10)
                        {
                            var cfg = LoadConfig();
                            SaveConfig(cfg.memoryPercent, cfg.monitorIntervalSeconds, count, cfg.skipSetup, cfg.betterGiMemoryLimitMB);
                            Console.WriteLine($"丢失计数阈值已设置为 {count} 次");
                        }
                        else
                        {
                            Console.WriteLine("错误: 丢失计数阈值应在 1-10 之间");
                        }
                    }
                    else if (args[1].ToLower() == "skip")
                    {
                        var cfg = LoadConfig();
                        bool newSkip = !cfg.skipSetup;
                        SaveConfig(cfg.memoryPercent, cfg.monitorIntervalSeconds, cfg.missingCountThreshold, newSkip, cfg.betterGiMemoryLimitMB);
                        Console.WriteLine($"跳过设置界面已设置为: {newSkip}");
                    }
                    else if (args[1].ToLower() == "memlimit")
                    {
                        if (int.TryParse(args[2], out int limit) && limit >= 0)
                        {
                            var cfg = LoadConfig();
                            SaveConfig(cfg.memoryPercent, cfg.monitorIntervalSeconds, cfg.missingCountThreshold, cfg.skipSetup, limit);
                            if (limit == 0)
                                Console.WriteLine("进程内存监控已禁用");
                            else
                                Console.WriteLine($"进程内存阈值已设置为 {limit}MB");
                        }
                        else
                        {
                            Console.WriteLine("错误: 进程内存阈值应为 >= 0 的整数 (0 表示禁用)");
                        }
                    }
                    else
                    {
                        ShowHelp();
                    }
                }
                else if (args.Length == 2 && args[1].ToLower() == "show")
                {
                    ShowConfig();
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
                    Console.WriteLine("配置已是默认值");
                }
                break;

            default:
                // 正常运行模式
                var config = LoadConfig();
                ApplyRuntimeConfig(config);

                // 检测 BetterGI 路径，未找到则强制要求设置
                EnsureBetterGiPath();

                Log("INFO", "BGIguard 启动成功");
                HandleSingleInstance();
                StartBetterGiProcess();
                RunGuardLoop();
                break;
        }
    }

    /// <summary>
    /// 显示帮助
    /// </summary>
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
        Console.WriteLine("默认值: 系统内存=85%, 监控间隔=5秒, 丢失计数=6次, 进程内存=4096MB, 跳过设置=否");
    }

    /// <summary>
    /// 显示当前配置
    /// </summary>
    private static void ShowConfig()
    {
        var config = LoadConfig();
        Console.WriteLine("当前配置:");
        Console.WriteLine($"  BetterGI路径: {config.betterGiPath}");
        Console.WriteLine($"  系统内存阈值: {config.memoryPercent}%");
        Console.WriteLine($"  监控间隔: {config.monitorIntervalSeconds} 秒");
        Console.WriteLine($"  丢失计数阈值: {config.missingCountThreshold} 次");
        Console.WriteLine($"  进程内存阈值: {(config.betterGiMemoryLimitMB > 0 ? $"{config.betterGiMemoryLimitMB}MB" : "已禁用")}");
        Console.WriteLine($"  跳过设置: {config.skipSetup}");
    }

    /// <summary>
    /// 显示命令行设置界面
    /// </summary>
    private static void ShowCommandLineSetup()
    {
        Console.WriteLine("=== BGIguard 设置 ===");
        Console.WriteLine();

        var config = LoadConfig();
        Console.WriteLine($"当前配置:");
        Console.WriteLine($"  BetterGI路径: {config.betterGiPath}");
        Console.WriteLine($"  系统内存阈值: {config.memoryPercent}%");
        Console.WriteLine($"  监控间隔: {config.monitorIntervalSeconds} 秒");
        Console.WriteLine($"  丢失计数阈值: {config.missingCountThreshold} 次");
        Console.WriteLine($"  进程内存阈值: {(config.betterGiMemoryLimitMB > 0 ? $"{config.betterGiMemoryLimitMB}MB" : "已禁用")}");
        Console.WriteLine();

        Console.WriteLine("请选择操作:");
        Console.WriteLine("  1. 修改 BetterGI 路径        (BetterGI.exe 完整路径)");
        Console.WriteLine("  2. 修改系统内存阈值        (1-100%，超阈值重启)");
        Console.WriteLine("  3. 修改监控间隔            (1-999秒，检测频率)");
        Console.WriteLine("  4. 修改丢失计数阈值        (1-10次，连续退出触发重启)");
        Console.WriteLine("  5. 修改进程内存阈值        (MB, 0=禁用, BetterGI独占内存超限重启)");
        Console.WriteLine("  6. 启动守护进程            (进入守护监控模式)");
        Console.WriteLine("  7. 跳过设置直接启动        (直接进入守护)");
        Console.WriteLine("  8. 重置配置                (恢复默认设置)");
        Console.WriteLine("  9. 退出");
        Console.WriteLine();

        Console.Write("请输入选项 (1-9): ");
        string? input = Console.ReadLine();

        switch (input)
        {
            case "1":
                Console.Write("请输入 BetterGI.exe 路径（或拖入文件，可带引号）: ");
                string? pathInput = Console.ReadLine();
                if (!string.IsNullOrEmpty(pathInput))
                {
                    pathInput = pathInput.Trim().Trim('"');
                }
                if (!string.IsNullOrWhiteSpace(pathInput) && File.Exists(pathInput))
                {
                    SaveConfigPath(pathInput);
                    Console.WriteLine($"路径已设置为: {pathInput}");
                }
                else
                {
                    Console.WriteLine("文件不存在，保留原值");
                }
                break;

            case "2":
                Console.Write("请输入系统内存阈值 (1-100): ");
                if (int.TryParse(Console.ReadLine(), out int mem) && mem > 0 && mem <= 100)
                {
                    var cfg2 = LoadConfig();
                    SaveConfig(mem, cfg2.monitorIntervalSeconds, cfg2.missingCountThreshold, cfg2.skipSetup, cfg2.betterGiMemoryLimitMB);
                    Console.WriteLine($"系统内存阈值已设置为 {mem}%");
                }
                break;

            case "3":
                Console.Write("请输入监控间隔 (秒): ");
                if (int.TryParse(Console.ReadLine(), out int interval) && interval > 0)
                {
                    var cfg3 = LoadConfig();
                    SaveConfig(cfg3.memoryPercent, interval, cfg3.missingCountThreshold, cfg3.skipSetup, cfg3.betterGiMemoryLimitMB);
                    Console.WriteLine($"监控间隔已设置为 {interval} 秒");
                }
                break;

            case "4":
                Console.Write("请输入丢失计数阈值 (1-10): ");
                if (int.TryParse(Console.ReadLine(), out int count) && count > 0 && count <= 10)
                {
                    var cfg4 = LoadConfig();
                    SaveConfig(cfg4.memoryPercent, cfg4.monitorIntervalSeconds, count, cfg4.skipSetup, cfg4.betterGiMemoryLimitMB);
                    Console.WriteLine($"丢失计数阈值已设置为 {count} 次");
                }
                break;

            case "5":
                Console.Write("请输入进程内存阈值 (MB, 0=禁用): ");
                if (int.TryParse(Console.ReadLine(), out int limit) && limit >= 0)
                {
                    var cfg5 = LoadConfig();
                    SaveConfig(cfg5.memoryPercent, cfg5.monitorIntervalSeconds, cfg5.missingCountThreshold, cfg5.skipSetup, limit);
                    if (limit == 0)
                        Console.WriteLine("进程内存监控已禁用");
                    else
                        Console.WriteLine($"进程内存阈值已设置为 {limit}MB");
                }
                break;

            case "6":
                break;

            case "7":
                var cfg7 = LoadConfig();
                SaveConfig(cfg7.memoryPercent, cfg7.monitorIntervalSeconds, cfg7.missingCountThreshold, true, cfg7.betterGiMemoryLimitMB);
                Console.WriteLine("已设置跳过设置界面");
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
            if (config.MemoryPercent <= 0 || config.MemoryPercent > 100)
                config.MemoryPercent = 85;
            if (config.MonitorInterval <= 0)
                config.MonitorInterval = 5;
            if (config.MissingCount <= 0 || config.MissingCount > 10)
                config.MissingCount = 6;
            if (config.BetterGiMemoryLimitMB < 0)
                config.BetterGiMemoryLimitMB = 4096;
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

    /// <summary>
    /// 处理单实例保护
    /// </summary>
    private static void HandleSingleInstance()
    {
        string mutexName = "BGIguard_SingleInstance_Mutex";
        bool createdNew;
        _mutex = new Mutex(true, mutexName, out createdNew);

        if (!createdNew)
        {
            Log("WARN", "检测到已存在的守护进程，正在终止...");
            TerminateExistingGuard();
            _mutex = new Mutex(true, mutexName, out createdNew);
        }

        Log("INFO", "单实例保护已生效");
    }

    /// <summary>
    /// 按用户终止指定进程
    /// </summary>
    private static void TerminateProcessesByUser(string processName, string logPrefix, int? excludePid = null)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                if (excludePid.HasValue && process.Id == excludePid.Value)
                    continue;

                var owner = GetProcessOwner(process.Id);
                if (IsCurrentUserProcess(owner))
                {
                    process.Kill();
                    process.WaitForExit(ProcessWaitExitMs);
                    Log("INFO", $"已终止 {logPrefix} PID:{process.Id} ({owner.Display})");
                }
                else if (owner.HasIdentity)
                {
                    Log("WARN", $"{logPrefix} PID:{process.Id} 属于{owner.Display}，跳过终止");
                }
            }
            catch (Exception ex)
            {
                Log("ERROR", $"终止 {logPrefix} PID:{process.Id} 失败: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    /// <summary>
    /// 终止已存在的守护进程（按用户和进程名）
    /// </summary>
    private static void TerminateExistingGuard()
    {
        using var currentProcess = Process.GetCurrentProcess();
        string currentProcessName = currentProcess.ProcessName;
        TerminateProcessesByUser(currentProcessName, "旧守护进程", Environment.ProcessId);
    }

    /// <summary>
    /// 获取进程所有者（P/Invoke 方式，替代 WMI）
    /// 性能：WMI 单次约 200-500ms，P/Invoke 单次约 1-3ms
    /// </summary>
    private static ProcessOwnerInfo GetProcessOwner(int processId)
    {
        IntPtr hProcess = IntPtr.Zero;
        IntPtr hToken = IntPtr.Zero;
        IntPtr tokenInfo = IntPtr.Zero;

        try
        {
            // 打开目标进程（只需查询信息权限，无需 VM_READ）
            hProcess = OpenProcess(PROCESS_QUERY_INFORMATION, false, processId);
            if (hProcess == IntPtr.Zero)
            {
                // 权限不足时回退到有限查询权限
                hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
                if (hProcess == IntPtr.Zero)
                    return default;
            }

            // 打开进程的访问令牌
            if (!OpenProcessToken(hProcess, TOKEN_QUERY, out hToken))
                return default;

            // 第一次调用：获取所需缓冲区大小
            int returnLength = 0;
            GetTokenInformation(hToken, TokenUser, IntPtr.Zero, 0, out returnLength);
            if (returnLength == 0)
                return default;

            // 分配非托管内存并获取 TOKEN_USER
            tokenInfo = Marshal.AllocHGlobal(returnLength);
            if (!GetTokenInformation(hToken, TokenUser, tokenInfo, returnLength, out returnLength))
                return default;

            var tokenUser = Marshal.PtrToStructure<TOKEN_USER>(tokenInfo);
            if (tokenUser.User.Sid == IntPtr.Zero)
                return default;

            string sid = new SecurityIdentifier(tokenUser.User.Sid).Value;

            // 获取所需缓冲区大小
            int nameSize = 0, domainSize = 0;
            if (!LookupAccountSid(null, tokenUser.User.Sid, null!, ref nameSize, null!, ref domainSize, out _))
            {
                if (nameSize == 0) return new ProcessOwnerInfo("", sid);
            }

            var nameBuilder = new StringBuilder(nameSize);
            var domainBuilder = new StringBuilder(Math.Max(1, domainSize));
            if (!LookupAccountSid(null, tokenUser.User.Sid, nameBuilder, ref nameSize, domainBuilder, ref domainSize, out _))
                return new ProcessOwnerInfo("", sid);

            string name = nameBuilder.ToString();
            string domain = domainBuilder.ToString();
            string displayName = string.IsNullOrEmpty(domain) ? name : $@"{domain}\{name}";
            return new ProcessOwnerInfo(displayName, sid);
        }
        catch
        {
            // 静默失败：权限不足或进程已退出是正常情况，避免日志风暴
            return default;
        }
        finally
        {
            if (tokenInfo != IntPtr.Zero)
                Marshal.FreeHGlobal(tokenInfo);
            if (hToken != IntPtr.Zero)
                CloseHandle(hToken);
            if (hProcess != IntPtr.Zero)
                CloseHandle(hProcess);
        }
    }

    private static bool IsCurrentUserProcess(ProcessOwnerInfo owner)
    {
        if (string.IsNullOrEmpty(_currentUserSid))
            return string.Equals(owner.UserName, _currentUserName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(owner.UserName, Environment.UserName, StringComparison.OrdinalIgnoreCase);

        return string.Equals(owner.Sid, _currentUserSid, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 启动 BetterGI.exe 进程
    /// </summary>
    private static void StartBetterGiProcess(string? commandLine = null)
    {
        if (string.IsNullOrEmpty(_betterGiExePath) || !File.Exists(_betterGiExePath))
        {
            Log("ERROR", $"找不到 BetterGI.exe: {_betterGiExePath}");
            return;
        }

        // 使用传入的命令行或缓存的命令行
        string cmdArgs = CleanCommandArgs(commandLine ?? _cachedCommand);
        string filteredArgs = FilterCmdArguments(cmdArgs);
        if (!string.Equals(cmdArgs, filteredArgs, StringComparison.Ordinal))
        {
            Log("WARN", $"启动参数包含 cmd 特殊字符，已过滤。原始参数: {FormatArgumentForLog(cmdArgs)} | 过滤后: {FormatArgumentForLog(filteredArgs)}");
            cmdArgs = filteredArgs;
        }

        try
        {
            // 关键修复：使用 "" 作为空窗口标题占位
            // 否则如果 cmdArgs 以引号开头，会被 start 当作窗口标题解析
            string arguments = string.IsNullOrEmpty(cmdArgs)
                ? $"/c start \"\" \"{_betterGiExePath}\""
                : $"/c start \"\" \"{_betterGiExePath}\" {cmdArgs}";

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var startedProcess = Process.Start(startInfo);
            Log("INFO", $"已启动 BetterGI.exe" + (string.IsNullOrEmpty(cmdArgs) ? "" : $" (参数: {cmdArgs})"));
        }
        catch (Exception ex)
        {
            Log("ERROR", $"启动失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 按用户终止 BetterGI.exe 进程（终止当前用户启动的进程）
    /// </summary>
    private static void TerminateBetterGiProcessByUser()
    {
        TerminateProcessesByUser(BetterGiExeName.Replace(".exe", ""), "BetterGI.exe");
    }

    /// <summary>
    /// 守护主循环
    /// </summary>
    private static void RunGuardLoop()
    {
        Log("INFO", "进入守护监控循环");

        while (true)
        {
            ApplyRuntimeConfig(LoadConfig());
            Thread.Sleep(_monitorIntervalMs);

            try
            {
                ApplyRuntimeConfig(LoadConfig());

                // 1. 获取 BetterGI 进程信息、命令行和内存占用（单次遍历）
                var betterGiSnapshot = GetBetterGiSnapshot(includeCommandLine: true, includeMemory: _betterGiMemoryLimitMB > 0);
                bool betterGiRunning = betterGiSnapshot.Exists;
                if (betterGiRunning && betterGiSnapshot.CommandLine != null)
                {
                    string extractedArgs = ExtractArgs(betterGiSnapshot.CommandLine);
                    string cleanedArgs = CleanCommandArgs(extractedArgs);  // 必须先清理

                    if (cleanedArgs != _cachedCommand)
                    {
                        _cachedCommand = cleanedArgs;
                        if (!string.IsNullOrEmpty(_cachedCommand))
                            Log("INFO", $"已缓存启动命令: {_cachedCommand}");
                        else
                            Log("INFO", "检测到无启动参数");
                    }
                }

                // 2. 检查游戏进程
                var (gameRunning, gameProcesses) = GetRunningGameProcesses();

                // 3. 检查系统内存
                var (totalMB, usedMB, physicalMB, virtualMB) = GetSystemMemory();
                long memoryLimitMB = totalMB * _memoryPercent / 100;
                int usedPercent = (int)(usedMB * 100 / Math.Max(1, totalMB));

                // 4. 检查 BetterGI 进程自身内存（OOM 精准监控）
                long betterGiMemMB = 0;
                if (_betterGiMemoryLimitMB > 0 && betterGiRunning)
                {
                    betterGiMemMB = betterGiSnapshot.MemoryMB;
                }

                // 打印检测日志
                string gameStatus = gameRunning ? string.Join(", ", gameProcesses) : "无";
                string giStatus = betterGiRunning ? "运行" : $"未运行({_missingCount}/{_missingCountThreshold})";
                string giMemStatus = (_betterGiMemoryLimitMB > 0 && betterGiRunning)
                    ? $" | BetterGI内存: {betterGiMemMB}MB"
                    : "";
                Log("INFO", $"检测 {DateTime.Now:HH:mm:ss} | 内存: {usedPercent}% | BetterGI: {giStatus}{giMemStatus} | 游戏: {gameStatus}");

                // 内存警告 (使用配置值-5作为阈值)
                int memWarningThreshold = Math.Max(1, _memoryPercent - 5);
                if (usedPercent >= memWarningThreshold)
                {
                    Log("WARN", $"[系统内存警告] 已用: {usedMB}MB/{totalMB}MB ({usedPercent}%) | 物理: {physicalMB}MB | 虚拟: {virtualMB}MB");
                }

                // ========== 进程级内存超限检查（精准 OOM 防护）==========
                if (_betterGiMemoryLimitMB > 0 && betterGiRunning && betterGiMemMB > _betterGiMemoryLimitMB)
                {
                    Log("WARN", $"[进程内存超限] BetterGI 占用 {betterGiMemMB}MB > 阈值 {_betterGiMemoryLimitMB}MB，正在重启...");
                    TerminateBetterGiProcessByUser();
                    Thread.Sleep(RestartDelayMs);
                    StartBetterGiProcess(_cachedCommand);
                    Log("INFO", "进程内存超限后已重启");
                    // 重置相关计数
                    _missingCount = 0;
                    continue;  // 本轮已处理，跳过下方逻辑
                }

                // 判断是否需要重启（BetterGI 丢失）
                if (!betterGiRunning)
                {
                    _missingCount++;
                    Log("WARN", $"BetterGI.exe 丢失 (第 {_missingCount} 次)");

                    if (_missingCount >= _missingCountThreshold)
                    {
                        Log("INFO", "连续丢失达到阈值，正在重启...");
                        RestartBetterGiProcess();
                        _missingCount = 0;
                    }
                }
                else
                {
                    // 进程存在，重置计数
                    if (_missingCount > 0)
                    {
                        Log("INFO", "BetterGI.exe 已恢复，计数重置");
                        _missingCount = 0;
                    }
                }

                // 系统内存超限时重启
                if (usedMB > memoryLimitMB)
                {
                    Log("WARN", $"[系统内存超限] {usedMB}MB > {memoryLimitMB}MB ({_memoryPercent}%)");
                    TerminateBetterGiProcessByUser();
                    Thread.Sleep(RestartDelayMs);
                    StartBetterGiProcess(_cachedCommand);
                    Log("INFO", "系统内存超限后已重启");
                }

                // 游戏退出后重启（使用与 BetterGI 相同的计次阈值）
                if (!gameRunning && betterGiRunning)
                {
                    _gameExitCount++;
                    Log("WARN", $"游戏已退出 (第 {_gameExitCount} 次)");

                    if (_gameExitCount >= _missingCountThreshold)
                    {
                        Log("INFO", $"游戏退出达到阈值，终止 BetterGI.exe (当前用户:{_currentUserName}, SID:{_currentUserSid})");
                        TerminateBetterGiProcessByUser();
                        Thread.Sleep(RestartDelayMs);
                        StartBetterGiProcess(_cachedCommand);
                        _gameExitCount = 0;
                    }
                }
                else if (gameRunning)
                {
                    if (_gameExitCount > 0)
                    {
                        Log("INFO", "游戏已恢复，计数重置");
                        _gameExitCount = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Log("ERROR", $"守护循环异常: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 获取 BetterGI 进程当前独占内存（PrivateMemorySize64），单位 MB
    /// </summary>
    private static long GetBetterGiMemoryMB()
    {
        return GetBetterGiSnapshot(includeCommandLine: false, includeMemory: true).MemoryMB;
    }

    /// <summary>
    /// 检查进程是否属于当前用户
    /// </summary>
    private static bool IsProcessOwnedByCurrentUser(int processId)
    {
        return IsCurrentUserProcess(GetProcessOwner(processId));
    }

    /// <summary>
    /// 过滤 cmd.exe /c start 参数中的高风险控制字符。
    /// </summary>
    private static string FilterCmdArguments(string args)
    {
        if (string.IsNullOrEmpty(args))
            return "";

        var builder = new StringBuilder(args.Length);
        foreach (char c in args)
        {
            if (Array.IndexOf(DangerousCmdArgumentChars, c) >= 0)
                continue;

            builder.Append(c);
        }

        return builder.ToString().Trim();
    }

    private static string FormatArgumentForLog(string args)
    {
        return args.Replace("\r", "\\r").Replace("\n", "\\n");
    }

    /// <summary>
    /// 按用户和路径检查 BetterGI 是否运行
    /// </summary>
    private static bool IsBetterGiRunningByUser()
    {
        return GetBetterGiSnapshot(includeCommandLine: false, includeMemory: false).Exists;
    }

    // 重启 BetterGI.exe
    private static void RestartBetterGiProcess()
    {
        TerminateBetterGiProcessByUser();
        Thread.Sleep(RestartDelayMs);
        StartBetterGiProcess(_cachedCommand);
    }

    /// <summary>
    /// 获取当前用户 BetterGI 进程的信息
    /// </summary>
    private static (bool exists, string? commandLine) GetBetterGiInfo()
    {
        var snapshot = GetBetterGiSnapshot(includeCommandLine: true, includeMemory: false);
        return (snapshot.Exists, snapshot.CommandLine);
    }

    /// <summary>
    /// 获取当前用户 BetterGI 进程快照。
    /// </summary>
    private static BetterGiProcessSnapshot GetBetterGiSnapshot(bool includeCommandLine, bool includeMemory)
    {
        if (string.IsNullOrEmpty(_betterGiExePath)) return default;

        foreach (var process in Process.GetProcessesByName(BetterGiExeName.Replace(".exe", "")))
        {
            try
            {
                if (!IsProcessOwnedByCurrentUser(process.Id))
                    continue;
                string? modulePath = process.MainModule?.FileName;
                if (string.Equals(modulePath, _betterGiExePath, StringComparison.OrdinalIgnoreCase))
                {
                    string? commandLine = includeCommandLine ? GetProcessCommandLine(process.Id) : null;
                    long memoryMB = includeMemory ? process.PrivateMemorySize64 / 1024 / 1024 : 0;
                    return new BetterGiProcessSnapshot(true, commandLine, memoryMB);
                }
            }
            catch (Exception ex)
            {
                Log("ERROR", $"获取 BetterGI 进程信息失败: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }
        return default;
    }

    /// <summary>
    /// 获取当前用户正在运行的游戏进程
    /// </summary>
    private static (bool anyRunning, List<string> runningNames) GetRunningGameProcesses()
    {
        var games = new List<string>();
        foreach (var name in GameProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                try
                {
                    if (IsProcessOwnedByCurrentUser(process.Id))
                        games.Add(name);
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        return (games.Count > 0, games);
    }

    /// <summary>
    /// 获取系统内存（物理内存 + 页面文件）
    /// </summary>
    private static (long totalMB, long usedMB, long physicalMB, long virtualMB) GetSystemMemory()
    {
        var memStatus = new MEMORYSTATUSEX
        {
            dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
        };

        if (!GlobalMemoryStatusEx(ref memStatus))
        {
            Log("ERROR", "获取系统内存信息失败，使用默认值");
            return (32 * 1024, 0, 32 * 1024, 16 * 1024);
        }

        // 物理内存 (MB)
        long totalPhysMB = (long)(memStatus.ullTotalPhys / 1024 / 1024);
        long usedPhysMB = (long)((memStatus.ullTotalPhys - memStatus.ullAvailPhys) / 1024 / 1024);

        // 页面文件实际占用 (MB) = 已提交 - 物理已用
        // 因为已提交总量 = 物理已用 + 页面文件实际已用
        long totalCommitMB = (long)(memStatus.ullTotalPageFile / 1024 / 1024);
        long usedCommitMB = (long)((memStatus.ullTotalPageFile - memStatus.ullAvailPageFile) / 1024 / 1024);
        long usedPageFileMB = Math.Max(0, usedCommitMB - usedPhysMB);

        // 物理 + 虚拟 实际占用
        long totalCombinedMB = totalPhysMB + (totalCommitMB - totalPhysMB); // 或直接用页面文件总大小
        long usedCombinedMB = usedPhysMB + usedPageFileMB;

        return (totalCombinedMB, usedCombinedMB, usedPhysMB, usedPageFileMB);
    }

    /// <summary>
    /// 记录日志
    /// </summary>
    private static void Log(string level, string message)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string logMessage = $"[{timestamp}] [BGIguard_v{GetDisplayVersion()}] [{level}] {message}";

        Console.WriteLine(logMessage);

        lock (_logLock)
        {
            try
            {
                string logFileName = $"{LogFilePrefix}{DateTime.Now:yyyyMMdd}.log";
                string logPath = Path.Combine(_exeDirectory, logFileName);
                File.AppendAllText(logPath, logMessage + Environment.NewLine, Encoding.UTF8);

                DateTime today = DateTime.Today;
                if (_lastLogCleanupDate != today)
                {
                    CleanOldLogs();
                    _lastLogCleanupDate = today;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"写入日志失败: {ex.Message}");
                Console.WriteLine($"日志目录: {_exeDirectory}");
                Console.WriteLine("请确认程序所在目录具有写入权限，避免放在 Program Files 等受保护目录，或以管理员身份运行。");
            }
        }
    }

    /// <summary>
    /// 清理旧日志
    /// </summary>
    private static void CleanOldLogs()
    {
        try
        {
            var logFiles = Directory.GetFiles(_exeDirectory, $"{LogFilePrefix}*.log")
                .OrderByDescending(f => f)
                .Skip(MaxLogFiles)
                .ToList();

            foreach (var file in logFiles)
            {
                try { File.Delete(file); } catch { }
            }
        }
        catch (Exception ex)
        {
            try { Console.WriteLine($"清理旧日志失败: {ex.Message}"); } catch { }
        }
    }

    /// <summary>
    /// 获取进程命令行（通过 NtQueryInformationProcess 读取 PEB）
    /// </summary>
    private static string? GetProcessCommandLine(int processId)
    {
        IntPtr hProcess = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, processId);
        if (hProcess == IntPtr.Zero)
            return null;

        try
        {
            var pbi = new PROCESS_BASIC_INFORMATION();
            int status = NtQueryInformationProcess(hProcess, ProcessBasicInformation, ref pbi, Marshal.SizeOf(pbi), out _);
            if (status != 0) return null;

            // 读取 PEB 中的 ProcessParameters 指针
            byte[] buffer = new byte[IntPtr.Size];
            if (!ReadProcessMemory(hProcess, IntPtr.Add(pbi.PebBaseAddress, PEB_OFFSET), buffer, IntPtr.Size, out _))
                return null;

            IntPtr processParameters = IntPtr.Size == 8
                ? (IntPtr)BitConverter.ToInt64(buffer, 0)
                : (IntPtr)BitConverter.ToInt32(buffer, 0);

            // 读取 CommandLine UNICODE_STRING
            byte[] cmdLineBuffer = new byte[Marshal.SizeOf<UNICODE_STRING>()];
            if (!ReadProcessMemory(hProcess, IntPtr.Add(processParameters, CMDLINE_OFFSET), cmdLineBuffer, cmdLineBuffer.Length, out _))
                return null;

            var unicodeString = MemoryMarshal.Read<UNICODE_STRING>(cmdLineBuffer);
            if (unicodeString.Buffer == IntPtr.Zero || unicodeString.Length == 0)
                return null;

            // 读取实际的命令行字符串
            byte[] cmdLineBytes = new byte[unicodeString.Length];
            if (!ReadProcessMemory(hProcess, unicodeString.Buffer, cmdLineBytes, unicodeString.Length, out _))
                return null;

            return Encoding.Unicode.GetString(cmdLineBytes);
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    /// <summary>
    /// 从完整命令行中提取参数部分（去掉可执行文件路径）
    /// </summary>
    private static string ExtractArgs(string fullCommandLine)
    {
        if (string.IsNullOrWhiteSpace(fullCommandLine))
            return "";

        // 策略：先找 .exe，再找参数
        int exeIndex = fullCommandLine.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);

        if (exeIndex > 0)
        {
            // 找到 .exe 后的第一个非空格字符
            int argStart = exeIndex + 4; // 跳过 ".exe"

            // 跳过空格
            while (argStart < fullCommandLine.Length &&
                   fullCommandLine[argStart] == ' ')
            {
                argStart++;
            }

            if (argStart < fullCommandLine.Length)
            {
                return CleanCommandArgs(fullCommandLine[argStart..]);
            }
        }

        // 备用：找第一个空格（无 .exe 的情况）
        int firstSpace = fullCommandLine.IndexOf(' ');
        string rawArgs = firstSpace > 0 ? fullCommandLine[(firstSpace + 1)..] : "";
        return CleanCommandArgs(rawArgs);
    }


    /// <summary>
    /// 彻底清理命令行参数，去除所有首尾引号和空格（包括不成对的单个引号）
    /// </summary>
    private static string CleanCommandArgs(string args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return "";

        // 循环去除所有首尾引号和空格
        string cleaned = args;
        bool changed;
        do
        {
            string before = cleaned;
            cleaned = cleaned.Trim();

            // 去除开头的单个引号（不管结尾有没有）
            if (cleaned.StartsWith("\""))
                cleaned = cleaned[1..];

            // 去除结尾的单个引号（不管开头有没有）
            if (cleaned.EndsWith("\""))
                cleaned = cleaned[..^1];

            changed = cleaned != before;
        } while (changed && cleaned.Length > 0);

        // 如果清理后只剩下引号和空格，视为空参数
        if (string.IsNullOrWhiteSpace(cleaned.Replace("\"", "")))
            return "";

        return cleaned;
    }
}
