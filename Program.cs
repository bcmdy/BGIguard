using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace BGIguard;

/// <summary>
/// BGIguard - BetterGI 守护程序
/// </summary>
partial class Program
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
    private static extern IntPtr LocalFree(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(
        [MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine,
        out int pNumArgs);

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

}
