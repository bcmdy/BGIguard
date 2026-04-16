using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
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
    private const string BetterGiExeName = "BetterGI.exe";
    private const string LogFilePrefix = "BGI_guard";
    private static string ConfigFilePath => Path.Combine(_exeDirectory, "BGIguard_config.json");

    // 配置类
    private class Config
    {
        public string BetterGiPath { get; set; } = "";
        public int MemoryPercent { get; set; } = 95;
        public int MonitorInterval { get; set; } = 5;
        public int MissingCount { get; set; } = 3;
        public bool SkipSetup { get; set; }
    }

    [DllImport("ntdll.dll")]
    private static extern IntPtr RtlGetCurrentPeb();

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
    private static string _betterGiPath = "";
    private static string _cachedCommand = "";
    private static Mutex? _mutex;
    private static readonly object _logLock = new();
    private static readonly string _version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0";

    // 游戏进程名
    private static readonly string[] GameProcessNames = { "YuanShen", "GenshinImpact" };

    // 运行时配置
    private static int _monitorIntervalMs = 5000;
    private static int _memoryPercent = 95;
    private static int _missingCountThreshold = 3;
    private static bool _skipSetup = false;

    // 丢失计数
    private static int _missingCount = 0;

    // 配置缓存
    private static (string betterGiPath, int memoryPercent, int monitorIntervalSeconds, int missingCountThreshold, bool skipSetup)? _configCache = null;
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

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

        // 加载配置
        var initConfig = LoadConfig();
        _skipSetup = initConfig.skipSetup;
        _monitorIntervalMs = initConfig.monitorIntervalSeconds * 1000;
        _memoryPercent = initConfig.memoryPercent;
        _missingCountThreshold = initConfig.missingCountThreshold;

        // 检测 BetterGI.exe 路径
        if (!DetectBetterGiPath())
        {
            // 未找到 BetterGI.exe，强制要求设置
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
            _betterGiPath = pathInput;
            Console.WriteLine($"路径已设置为: {_betterGiPath}");
        }

        // 处理命令行参数
        if (args.Length > 0)
        {
            HandleCommandLine(args);
            return;
        }

        // 无参数启动时显示设置界面（除非已跳过）
        if (!_skipSetup)
        {
            ShowCommandLineSetup();
        }

        // 记录启动日志
        Log("INFO", "BGIguard 启动成功");
        Log("INFO", $"BetterGI路径: {_betterGiPath}");

        // 处理单实例保护
        HandleSingleInstance();

        // 检查 BetterGI 是否已运行，若已运行则不重复启动
        bool alreadyRunning = IsBetterGiRunningByPath();
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
    /// 检测 BetterGI.exe 路径
    /// </summary>
    private static bool DetectBetterGiPath()
    {
        // 1. 先检测自身目录下是否存在
        string localPath = Path.Combine(_exeDirectory, BetterGiExeName);
        if (File.Exists(localPath))
        {
            _betterGiPath = localPath;
            return true;
        }

        // 2. 检查配置文件中的路径
        var config = LoadConfig();
        if (!string.IsNullOrEmpty(config.betterGiPath) && File.Exists(config.betterGiPath))
        {
            _betterGiPath = config.betterGiPath;
            return true;
        }

        return false;
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
                            SaveConfig(mem, cfg.monitorIntervalSeconds, cfg.missingCountThreshold, cfg.skipSetup);
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
                            SaveConfig(cfg.memoryPercent, interval, cfg.missingCountThreshold, cfg.skipSetup);
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
                            SaveConfig(cfg.memoryPercent, cfg.monitorIntervalSeconds, count, cfg.skipSetup);
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
                        SaveConfig(cfg.memoryPercent, cfg.monitorIntervalSeconds, cfg.missingCountThreshold, newSkip);
                        Console.WriteLine($"跳过设置界面已设置为: {newSkip}");
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
                _monitorIntervalMs = config.monitorIntervalSeconds * 1000;
                _memoryPercent = config.memoryPercent;
                _missingCountThreshold = config.missingCountThreshold;
                _skipSetup = config.skipSetup;

                // 检测 BetterGI 路径，未找到则强制要求设置
                if (!DetectBetterGiPath())
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
                        Console.WriteLine("文件不存在，请重新输入:");
                        Console.Write("> ");
                        pathInput = Console.ReadLine();
                    }
                    SaveConfigPath(pathInput);
                    _betterGiPath = pathInput;
                }

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
        Console.WriteLine("  BGIguard.exe                    启动守护进程（无参数）");
        Console.WriteLine("  BGIguard.exe set path <路径>    设置 BetterGI.exe 路径");
        Console.WriteLine("  BGIguard.exe set memory <值>    设置内存阈值 (1-100)");
        Console.WriteLine("  BGIguard.exe set interval <值>  设置监控间隔 (秒)");
        Console.WriteLine("  BGIguard.exe set count <值>     设置丢失计数阈值 (1-10)");
        Console.WriteLine("  BGIguard.exe set skip           设置/取消跳过设置界面");
        Console.WriteLine("  BGIguard.exe set show           显示当前配置");
        Console.WriteLine("  BGIguard.exe reset              重置配置为默认值");
        Console.WriteLine("  BGIguard.exe help               显示帮助");
        Console.WriteLine();
        Console.WriteLine("默认值: 内存阈值=95%, 监控间隔=5秒, 丢失计数=2次, 跳过设置=否");
    }

    /// <summary>
    /// 显示当前配置
    /// </summary>
    private static void ShowConfig()
    {
        var config = LoadConfig();
        Console.WriteLine("当前配置:");
        Console.WriteLine($"  BetterGI路径: {config.betterGiPath}");
        Console.WriteLine($"  内存阈值: {config.memoryPercent}%");
        Console.WriteLine($"  监控间隔: {config.monitorIntervalSeconds} 秒");
        Console.WriteLine($"  丢失计数阈值: {config.missingCountThreshold} 次");
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
        Console.WriteLine($"  内存阈值: {config.memoryPercent}%");
        Console.WriteLine($"  监控间隔: {config.monitorIntervalSeconds} 秒");
        Console.WriteLine($"  丢失计数阈值: {config.missingCountThreshold} 次");
        Console.WriteLine();

        Console.WriteLine("请选择操作:");
        Console.WriteLine("  1. 修改 BetterGI 路径");
        Console.WriteLine("  2. 修改内存阈值");
        Console.WriteLine("  3. 修改监控间隔");
        Console.WriteLine("  4. 修改丢失计数阈值");
        Console.WriteLine("  5. 启动守护进程");
        Console.WriteLine("  6. 跳过设置直接启动");
        Console.WriteLine("  7. 重置配置");
        Console.WriteLine("  8. 退出");
        Console.WriteLine();

        Console.Write("请输入选项 (1-8): ");
        string? input = Console.ReadLine();

        switch (input)
        {
            case "1":
                Console.Write("请输入 BetterGI.exe 路径（或拖入文件）: ");
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
                Console.Write("请输入内存阈值 (1-100): ");
                if (int.TryParse(Console.ReadLine(), out int mem) && mem > 0 && mem <= 100)
                {
                    var cfg2 = LoadConfig();
                    SaveConfig(mem, cfg2.monitorIntervalSeconds, cfg2.missingCountThreshold, cfg2.skipSetup);
                    Console.WriteLine($"内存阈值已设置为 {mem}%");
                }
                break;

            case "3":
                Console.Write("请输入监控间隔 (秒): ");
                if (int.TryParse(Console.ReadLine(), out int interval) && interval > 0)
                {
                    var cfg3 = LoadConfig();
                    SaveConfig(cfg3.memoryPercent, interval, cfg3.missingCountThreshold, cfg3.skipSetup);
                    Console.WriteLine($"监控间隔已设置为 {interval} 秒");
                }
                break;

            case "4":
                Console.Write("请输入丢失计数阈值 (1-10): ");
                if (int.TryParse(Console.ReadLine(), out int count) && count > 0 && count <= 10)
                {
                    var cfg4 = LoadConfig();
                    SaveConfig(cfg4.memoryPercent, cfg4.monitorIntervalSeconds, count, cfg4.skipSetup);
                    Console.WriteLine($"丢失计数阈值已设置为 {count} 次");
                }
                break;

            case "5":
                break;

            case "6":
                var cfg6 = LoadConfig();
                SaveConfig(cfg6.memoryPercent, cfg6.monitorIntervalSeconds, cfg6.missingCountThreshold, true);
                Console.WriteLine("已设置跳过设置界面");
                break;

            case "7":
                if (File.Exists(ConfigFilePath))
                {
                    File.Delete(ConfigFilePath);
                    Console.WriteLine("配置已重置");
                }
                break;

            case "8":
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
    private static (string betterGiPath, int memoryPercent, int monitorIntervalSeconds, int missingCountThreshold, bool skipSetup) LoadConfig()
    {
        if (_configCache.HasValue)
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
                config.MemoryPercent = 95;
            if (config.MonitorInterval <= 0)
                config.MonitorInterval = 5;
            if (config.MissingCount <= 0 || config.MissingCount > 10)
                config.MissingCount = 3;
        }
        catch { }

        var result = (config.BetterGiPath, config.MemoryPercent, config.MonitorInterval, config.MissingCount, config.SkipSetup);
        _configCache = result;
        return result;
    }

    /// <summary>
    /// 保存配置
    /// </summary>
    private static void SaveConfig(int memoryPercent, int monitorIntervalSeconds, int missingCountThreshold, bool skipSetup)
    {
        _configCache = null;
        var config = new Config
        {
            MemoryPercent = memoryPercent,
            MonitorInterval = monitorIntervalSeconds,
            MissingCount = missingCountThreshold,
            SkipSetup = skipSetup
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
    }

    /// <summary>
    /// 保存路径配置
    /// </summary>
    private static void SaveConfigPath(string path)
    {
        // 去除首尾引号，支持带引号的路径输入
        if (!string.IsNullOrEmpty(path))
        {
            path = path.Trim().Trim('"');
        }
        _configCache = null;
        var existing = LoadConfig();
        var config = new Config
        {
            BetterGiPath = path,
            MemoryPercent = existing.memoryPercent,
            MonitorInterval = existing.monitorIntervalSeconds,
            MissingCount = existing.missingCountThreshold,
            SkipSetup = existing.skipSetup
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
    /// 终止已存在的守护进程
    /// </summary>
    private static void TerminateExistingGuard()
    {
        string currentProcessName = Process.GetCurrentProcess().ProcessName;
        string currentExePath = Environment.ProcessPath ?? "";

        foreach (var process in Process.GetProcessesByName(currentProcessName))
        {
            try
            {
                if (process.Id != Environment.ProcessId)
                {
                    process.Kill();
                    process.WaitForExit(3000);
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// 启动 BetterGI.exe 进程
    /// </summary>
    private static void StartBetterGiProcess(string? commandLine = null)
    {
        if (string.IsNullOrEmpty(_betterGiPath) || !File.Exists(_betterGiPath))
        {
            Log("ERROR", $"找不到 BetterGI.exe: {_betterGiPath}");
            return;
        }

        // 使用传入的命令行或缓存的命令行
        string cmdArgs = commandLine ?? _cachedCommand;

        try
        {
            // 使用 cmd /c start 方式启动，这样可以创建一个独立的窗口
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"BetterGI\" \"{_betterGiPath}\" {cmdArgs}",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process.Start(startInfo);
            Log("INFO", $"已启动 BetterGI.exe" + (string.IsNullOrEmpty(cmdArgs) ? "" : $" (参数: {cmdArgs})"));
        }
        catch (Exception ex)
        {
            Log("ERROR", $"启动失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 按路径终止 BetterGI.exe 进程
    /// </summary>
    private static void TerminateBetterGiProcessByPath()
    {
        if (string.IsNullOrEmpty(_betterGiPath)) return;

        foreach (var process in Process.GetProcessesByName(BetterGiExeName.Replace(".exe", "")))
        {
            try
            {
                string? modulePath = process.MainModule?.FileName;
                if (string.Equals(modulePath, _betterGiPath, StringComparison.OrdinalIgnoreCase))
                {
                    process.Kill();
                    process.WaitForExit(3000);
                    Log("INFO", "已终止现有 BetterGI.exe 进程");
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// 守护主循环
    /// </summary>
    private static void RunGuardLoop()
    {
        Log("INFO", "进入守护监控循环");

        while (true)
        {
            Thread.Sleep(_monitorIntervalMs);

            try
            {
                // 1. 获取 BetterGI 进程信息并缓存（合并遍历）
                var (betterGiRunning, commandLine) = GetBetterGiInfo();
                if (betterGiRunning && commandLine != null)
                {
                    string extractedArgs = ExtractArgs(commandLine);
                    if (extractedArgs != _cachedCommand)
                    {
                        _cachedCommand = extractedArgs;
                        Log("INFO", $"已缓存启动命令: {_cachedCommand}");
                    }
                }

                // 3. 检查游戏进程
                bool gameRunning = IsAnyGameRunning();
                var gameProcesses = GetRunningGameProcesses();

                // 4. 检查系统内存
                var (totalMB, usedMB, physicalMB, virtualMB) = GetSystemMemory();
                long memoryLimitMB = totalMB * _memoryPercent / 100;
                int usedPercent = (int)(usedMB * 100 / Math.Max(1, totalMB));

                // 打印检测日志
                string gameStatus = gameRunning ? string.Join(", ", gameProcesses) : "无";
                string giStatus = betterGiRunning ? "运行" : $"未运行({_missingCount}/{_missingCountThreshold})";
                Log("INFO", $"检测 {DateTime.Now:HH:mm:ss} | 内存: {usedPercent}% | BetterGI: {giStatus} | 游戏: {gameStatus}");

                // 内存警告 (使用配置值-5作为阈值)
                int memWarningThreshold = Math.Max(1, _memoryPercent - 5);
                if (usedPercent >= memWarningThreshold)
                {
                    Log("WARN", $"[内存警告] 已用: {usedMB}MB/{totalMB}MB ({usedPercent}%) | 物理: {physicalMB}MB | 虚拟: {virtualMB}MB");
                }

                // 判断是否需要重启
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

                // 内存超限时重启
                if (usedMB > memoryLimitMB)
                {
                    Log("WARN", $"内存超限: {usedMB}MB > {memoryLimitMB}MB ({_memoryPercent}%)");
                    TerminateBetterGiProcessByPath();
                    Thread.Sleep(RestartDelayMs);
                    StartBetterGiProcess(_cachedCommand);
                    Log("INFO", "内存超限后已重启");
                }

                // 游戏退出后重启
                if (!gameRunning && betterGiRunning)
                {
                    Log("INFO", "游戏已退出，终止 BetterGI.exe");
                    TerminateBetterGiProcessByPath();
                    Thread.Sleep(RestartDelayMs);
                    StartBetterGiProcess(_cachedCommand);
                }
            }
            catch (Exception ex)
            {
                Log("ERROR", $"守护循环异常: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 按路径检查 BetterGI 是否运行
    /// </summary>
    private static bool IsBetterGiRunningByPath()
    {
        if (string.IsNullOrEmpty(_betterGiPath)) return false;

        foreach (var process in Process.GetProcessesByName(BetterGiExeName.Replace(".exe", "")))
        {
            try
            {
                string? modulePath = process.MainModule?.FileName;
                if (string.Equals(modulePath, _betterGiPath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }
        }
        return false;
    }

    /// <summary>
    /// 获取 BetterGI 进程的信息（合并遍历）
    /// </summary>
    private static (bool exists, string? commandLine) GetBetterGiInfo()
    {
        if (string.IsNullOrEmpty(_betterGiPath)) return (false, null);

        foreach (var process in Process.GetProcessesByName(BetterGiExeName.Replace(".exe", "")))
        {
            try
            {
                string? modulePath = process.MainModule?.FileName;
                if (string.Equals(modulePath, _betterGiPath, StringComparison.OrdinalIgnoreCase))
                {
                    return (true, GetProcessCommandLine(process.Id));
                }
            }
            catch { }
        }
        return (false, null);
    }

    /// <summary>
    /// 重启 BetterGI.exe
    /// </summary>
    private static void RestartBetterGiProcess()
    {
        TerminateBetterGiProcessByPath();
        Thread.Sleep(RestartDelayMs);
        StartBetterGiProcess(_cachedCommand);
    }

    /// <summary>
    /// 检查是否有游戏进程运行
    /// </summary>
    private static bool IsAnyGameRunning()
    {
        foreach (var name in GameProcessNames)
        {
            if (Process.GetProcessesByName(name).Length > 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 获取正在运行的游戏进程
    /// </summary>
    private static List<string> GetRunningGameProcesses()
    {
        var games = new List<string>();
        foreach (var name in GameProcessNames)
        {
            if (Process.GetProcessesByName(name).Length > 0)
                games.Add(name);
        }
        return games;
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

        if (GlobalMemoryStatusEx(ref memStatus))
        {
            // 物理内存
            ulong totalPhysKB = memStatus.ullTotalPhys / 1024;
            ulong usedPhysKB = (memStatus.ullTotalPhys - memStatus.ullAvailPhys) / 1024;

            // 页面文件（虚拟内存）
            ulong totalPageKB = memStatus.ullTotalPageFile / 1024;
            ulong usedPageKB = (memStatus.ullTotalPageFile - memStatus.ullAvailPageFile) / 1024;

            // 总内存 = 物理 + 页面文件（或者直接用 totalPageFile，因为它就是物理+页面文件）
            // 注意：ullTotalPageFile 已经包含了物理内存，所以不需要再加 ullTotalPhys
            ulong totalKB = totalPageKB;  // 这是系统总提交限制
            ulong usedKB = usedPageKB;    // 这是系统总已用（物理已用 + 页面文件已用）

            return ((long)totalKB, (long)usedKB,
                    (long)totalPhysKB, (long)totalPageKB);
        }

        Log("ERROR", "获取系统内存信息失败，使用默认值");
        return (32 * 1024, 0, 32 * 1024, 16 * 1024);  // 修正默认值单位
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
                CleanOldLogs();
            }
            catch { }
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
        catch { }
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
            return string.Empty;

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
                return fullCommandLine.Substring(argStart).Trim();
            }
        }

        // 备用：找第一个空格（无 .exe 的情况）
        int firstSpace = fullCommandLine.IndexOf(' ');
        return firstSpace > 0 ? fullCommandLine.Substring(firstSpace + 1).Trim() : string.Empty;
    }
}