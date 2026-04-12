using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace BGIguard;

/// <summary>
/// BGIguard - BetterGI 守护程序
/// </summary>
class Program
{
    // ============== 配置常量 ==============
    private const int RestartDelayMs = 1000;
    private const int MaxLogFiles = 7;
    private const string GuardFileName = "BGIguard.exe";
    private const string BgiExeName = "BGI.exe";
    private const string BetterGiExeName = "BetterGI.exe";
    private const string LogFilePrefix = "BGI_guard";
    private static string ConfigFilePath => Path.Combine(_exeDirectory, "BGIguard_config.ini");

    // ============== 全局变量 ==============
    private static string _exeDirectory = null!;
    private static string _cachedCommand = string.Empty;
    private static string _bgiProcessName = "BGI";
    private static Mutex? _mutex;
    private static readonly object _logLock = new();
    private static readonly string _version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0";

    // 获取显示版本（使用 FileVersion 更精确）
    private static string GetDisplayVersion()
    {
        var ver = typeof(Program).Assembly.GetCustomAttribute<System.Reflection.AssemblyFileVersionAttribute>();
        return ver?.Version ?? _version;
    }

    // 运行时配置
    private static int _monitorIntervalMs = 5000;
    private static int _memoryPercent = 95;

    // 游戏进程名
    private static readonly string[] GameProcessNames = { "YuanShen", "GenshinImpact" };

    static void Main(string[] args)
    {
        _exeDirectory = AppDomain.CurrentDomain.BaseDirectory;

        // 设置控制台窗口标题
        string version = GetDisplayVersion();
        Console.Title = $"BetterGI 进程守护 v{version} By:Bcmdy";

        // 处理命令行参数
        if (args.Length > 0)
        {
            HandleCommandLine(args);
            return;
        }

        // 加载配置
        var (memoryPercent, monitorIntervalSeconds, _) = LoadConfig();
        _monitorIntervalMs = monitorIntervalSeconds * 1000;
        _memoryPercent = memoryPercent;

        // 无参数启动时显示命令行设置提示
        var config = LoadConfig();
        if (!config.skipSetup)
        {
            ShowCommandLineSetup();
        }

        // 记录启动日志
        Log("INFO", "BGIguard 启动成功");

        // 检查是否需要文件替换
        CheckAndReplaceFile();

        // 处理单实例保护
        HandleSingleInstance(args);

        // 缓存启动命令（有命令则缓存，无命令则清空）
        _cachedCommand = args.Length > 0 ? string.Join(" ", args) : string.Empty;
        if (_cachedCommand.Length > 0)
        {
            Log("INFO", $"已缓存启动命令: {_cachedCommand}");
        }

        // 启动 BGI.exe
        StartBgiProcess();

        // 进入守护主循环
        RunGuardLoop();
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
            case "config":
                // 设置配置
                if (args.Length >= 3)
                {
                    if (args[1].ToLower() == "memory")
                    {
                        if (int.TryParse(args[2], out int mem) && mem > 0 && mem <= 100)
                        {
                            SaveConfig(mem, LoadConfig().monitorIntervalSeconds);
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
                            var currentConfig = LoadConfig();
                            SaveConfig(currentConfig.memoryPercent, interval, currentConfig.skipSetup);
                            Console.WriteLine($"监控间隔已设置为 {interval} 秒");
                        }
                        else
                        {
                            Console.WriteLine("错误: 监控间隔应大于 0");
                        }
                    }
                    else if (args[1].ToLower() == "skip")
                    {
                        var currentConfig = LoadConfig();
                        bool newSkip = !currentConfig.skipSetup;
                        SaveConfig(currentConfig.memoryPercent, currentConfig.monitorIntervalSeconds, newSkip);
                        Console.WriteLine($"跳过设置界面已设置为: {newSkip}");
                    }
                    else
                    {
                        ShowHelp();
                    }
                }
                else if (args.Length == 2 && args[1].ToLower() == "show")
                {
                    // 显示当前配置
                    var config = LoadConfig();
                    Console.WriteLine($"当前配置:");
                    Console.WriteLine($"  内存阈值: {config.memoryPercent}%");
                    Console.WriteLine($"  监控间隔: {config.monitorIntervalSeconds} 秒");
                    Console.WriteLine($"  跳过设置: {config.skipSetup}");
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
                // 重置配置
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
                // 正常运行模式，启动守护进程
                var (memoryPercent, monitorIntervalSeconds, _) = LoadConfig();
                _monitorIntervalMs = monitorIntervalSeconds * 1000;
                _memoryPercent = memoryPercent;

                // 记录启动日志
                Log("INFO", "BGIguard 启动成功");

                // 检查是否需要文件替换
                CheckAndReplaceFile();

                // 处理单实例保护
                HandleSingleInstance(args);

                // 缓存启动命令
                _cachedCommand = string.Join(" ", args);
                Log("INFO", $"已缓存启动命令: {_cachedCommand}");

                // 启动 BGI.exe
                StartBgiProcess();

                // 进入守护主循环
                RunGuardLoop();
                break;
        }
    }

    /// <summary>
    /// 显示命令行帮助
    /// </summary>
    private static void ShowHelp()
    {
        Console.WriteLine("BGIguard 命令行工具");
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  BGIguard.exe                    启动守护进程（无参数）");
        Console.WriteLine("  BGIguard.exe set memory <值>     设置内存阈值 (1-100)");
        Console.WriteLine("  BGIguard.exe set interval <值>  设置监控间隔 (秒)");
        Console.WriteLine("  BGIguard.exe set skip           设置/取消跳过设置界面");
        Console.WriteLine("  BGIguard.exe set show           显示当前配置");
        Console.WriteLine("  BGIguard.exe reset              重置配置为默认值");
        Console.WriteLine("  BGIguard.exe help               显示帮助");
        Console.WriteLine();
        Console.WriteLine("默认值: 内存阈值=95%, 监控间隔=5秒, 跳过设置=否");
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
        Console.WriteLine($"  内存阈值: {config.memoryPercent}%");
        Console.WriteLine($"  监控间隔: {config.monitorIntervalSeconds} 秒");
        Console.WriteLine();

        // 提示用户是否要修改配置
        Console.WriteLine("请选择操作:");
        Console.WriteLine("  1. 修改内存阈值");
        Console.WriteLine("  2. 修改监控间隔");
        Console.WriteLine("  3. 启动守护进程");
        Console.WriteLine("  4. 跳过设置直接启动");
        Console.WriteLine("  5. 重置配置");
        Console.WriteLine("  6. 退出");
        Console.WriteLine();

        Console.Write("请输入选项 (1-6): ");
        string? input = Console.ReadLine();

        switch (input)
        {
            case "1":
                Console.Write("请输入内存阈值 (1-100): ");
                if (int.TryParse(Console.ReadLine(), out int mem) && mem > 0 && mem <= 100)
                {
                    SaveConfig(mem, config.monitorIntervalSeconds, config.skipSetup);
                    Console.WriteLine($"内存阈值已设置为 {mem}%");
                }
                else
                {
                    Console.WriteLine("输入无效，保留原值");
                }
                break;

            case "2":
                Console.Write("请输入监控间隔 (秒): ");
                if (int.TryParse(Console.ReadLine(), out int interval) && interval > 0)
                {
                    SaveConfig(config.memoryPercent, interval, config.skipSetup);
                    Console.WriteLine($"监控间隔已设置为 {interval} 秒");
                }
                else
                {
                    Console.WriteLine("输入无效，保留原值");
                }
                break;

            case "3":
                // 继续启动守护进程
                break;

            case "4":
                // 跳过设置直接启动
                SaveConfig(config.memoryPercent, config.monitorIntervalSeconds, true);
                Console.WriteLine("已设置跳过设置界面，下次启动将直接启动守护进程");
                break;

            case "5":
                if (File.Exists(ConfigFilePath))
                {
                    File.Delete(ConfigFilePath);
                    Console.WriteLine("配置已重置为默认值");
                }
                break;

            case "6":
                // 退出，不启动守护进程
                Environment.Exit(0);
                break;

            default:
                Console.WriteLine("无效选项，将启动守护进程");
                break;
        }

        Console.WriteLine();
    }

    /// <summary>
    /// 加载配置
    /// </summary>
    private static (int memoryPercent, int monitorIntervalSeconds, bool skipSetup) LoadConfig()
    {
        int memoryPercent = 95;
        int monitorIntervalSeconds = 5;
        bool skipSetup = false;

        try
        {
            if (File.Exists(ConfigFilePath))
            {
                var lines = File.ReadAllLines(ConfigFilePath);
                foreach (var line in lines)
                {
                    var parts = line.Split('=');
                    if (parts.Length != 2) continue;

                    switch (parts[0])
                    {
                        case "MemoryPercent":
                            int.TryParse(parts[1], out memoryPercent);
                            break;
                        case "MonitorInterval":
                            int.TryParse(parts[1], out monitorIntervalSeconds);
                            break;
                        case "SkipSetup":
                            skipSetup = parts[1] == "1";
                            break;
                    }
                }
            }
        }
        catch
        {
            // 使用默认值
        }

        return (memoryPercent, monitorIntervalSeconds, skipSetup);
    }

    /// <summary>
    /// 保存配置
    /// </summary>
    private static void SaveConfig(int memoryPercent, int monitorIntervalSeconds, bool skipSetup = false)
    {
        var config = new Dictionary<string, string>
        {
            { "MemoryPercent", memoryPercent.ToString() },
            { "MonitorInterval", monitorIntervalSeconds.ToString() },
            { "SkipSetup", skipSetup ? "1" : "0" }
        };

        File.WriteAllLines(ConfigFilePath, config.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    /// <summary>
    /// 检查并执行文件替换
    /// </summary>
    private static void CheckAndReplaceFile()
    {
        string currentProcessName = Process.GetCurrentProcess().ProcessName;
        string currentExeName = Path.GetFileName(Environment.ProcessPath ?? "");

        // 如果当前进程名不是 BetterGI，说明需要执行替换
        if (!currentProcessName.Equals(BetterGiExeName, StringComparison.OrdinalIgnoreCase) &&
            !currentExeName.Equals(BetterGiExeName, StringComparison.OrdinalIgnoreCase))
        {
            // 检查原始守护文件是否存在
            string guardFilePath = Path.Combine(_exeDirectory, GuardFileName);
            string betterGiPath = Path.Combine(_exeDirectory, BetterGiExeName);
            string bgiPath = Path.Combine(_exeDirectory, BgiExeName);
            string backupPath = Path.Combine(_exeDirectory, "BetterGI.exe.bak");

            if (File.Exists(betterGiPath))
            {
                try
                {
                    // 获取 BetterGI.exe 文件大小 (MB)
                    FileInfo fileInfo = new FileInfo(betterGiPath);
                    long fileSizeMB = fileInfo.Length / (1024 * 1024);

                    if (fileSizeMB > 50 && !File.Exists(bgiPath))
                    {
                        // 大于 50MB：执行原有替换操作
                        File.Move(betterGiPath, bgiPath);
                        Log("INFO", $"已替换文件 BetterGI.exe ({fileSizeMB}MB) -> BGI.exe");

                        // 复制自身为 BetterGI.exe
                        string selfPath = Environment.ProcessPath ?? "";
                        File.Copy(selfPath, betterGiPath, true);
                        Log("INFO", "已复制守护器到 BetterGI.exe");

                        // 使用新名称重新启动
                        ProcessStartInfo startInfo = new ProcessStartInfo
                        {
                            FileName = betterGiPath,
                            Arguments = string.Join(" ", Environment.GetCommandLineArgs().Skip(1)),
                            UseShellExecute = true
                        };
                        Process.Start(startInfo);
                        Log("INFO", "以 BetterGI.exe 身份重新启动");

                        Environment.Exit(0);
                    }
                    else
                    {
                        // 小于等于 50MB：备份原文件并替换
                        if (File.Exists(betterGiPath))
                        {
                            // 如果备份文件已存在，先删除
                            if (File.Exists(backupPath))
                            {
                                File.Delete(backupPath);
                            }
                            File.Move(betterGiPath, backupPath);
                            Log("INFO", $"已备份 BetterGI.exe -> BetterGI.exe.bak ({fileSizeMB}MB)");
                        }

                        // 复制自身为 BetterGI.exe
                        string selfPath = Environment.ProcessPath ?? "";
                        File.Copy(selfPath, betterGiPath, true);
                        Log("INFO", "已复制守护器到 BetterGI.exe");

                        // 使用新名称重新启动
                        ProcessStartInfo startInfo = new ProcessStartInfo
                        {
                            FileName = betterGiPath,
                            Arguments = string.Join(" ", Environment.GetCommandLineArgs().Skip(1)),
                            UseShellExecute = true
                        };
                        Process.Start(startInfo);
                        Log("INFO", "以 BetterGI.exe 身份重新启动");

                        Environment.Exit(0);
                    }
                }
                catch (Exception ex)
                {
                    Log("ERROR", $"文件替换失败: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// 处理单实例保护
    /// </summary>
    private static void HandleSingleInstance(string[] args)
    {
        string mutexName = "BGIguard_SingleInstance_Mutex";

        // 尝试创建互斥体
        bool createdNew;
        _mutex = new Mutex(true, mutexName, out createdNew);

        if (!createdNew)
        {
            // 已存在守护进程，终止旧进程
            Log("WARN", "检测到已存在的守护进程，正在终止...");
            TerminateExistingGuard();

            // 重新创建互斥体
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
                    Log("INFO", "已终止旧守护进程");
                }
            }
            catch (Exception ex)
            {
                Log("ERROR", $"终止旧进程失败: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 启动 BGI.exe 进程
    /// </summary>
    private static void StartBgiProcess()
    {
        string bgiPath = Path.Combine(_exeDirectory, BgiExeName);
        string backupPath = Path.Combine(_exeDirectory, "BetterGI.exe.bak");

        // 检查 BGI.exe 是否存在，否则使用备份
        string? targetPath = File.Exists(bgiPath) ? bgiPath : (File.Exists(backupPath) ? backupPath : null);

        if (targetPath == null)
        {
            Log("ERROR", $"找不到 BGI.exe 或 BetterGI.exe.bak");
            return;
        }

        // 先终止可能已存在的 BGI.exe 进程
        bool hadBgiProcess = IsProcessRunning(_bgiProcessName);
        if (hadBgiProcess)
        {
            Log("INFO", "终止已存在的 BGI.exe 进程");
            TerminateBgiProcess();
        }

        try
        {
            // 使用 cmd /c start 让进程完全独立于父进程
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "cmd",
                Arguments = $"/c start \"\" \"{targetPath}\" {_cachedCommand}",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _exeDirectory
            };

            Process.Start(startInfo);
            Log("INFO", $"已启动 {Path.GetFileName(targetPath)}, 命令: {_cachedCommand}");
        }
        catch (Exception ex)
        {
            Log("ERROR", $"启动失败: {ex.Message}");
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
            // 等待后再检测
            Thread.Sleep(_monitorIntervalMs);

            try
            {
                // 1. 检查 BGI.exe 是否存在
                bool bgiRunning = IsProcessRunning(_bgiProcessName);

                // 2. 检查系统内存使用
                var (totalBytes, usedBytes, physicalBytes, virtualBytes) = GetSystemMemory();
                long totalMemoryMB = totalBytes / (1024 * 1024);
                long usedMemoryMB = usedBytes / (1024 * 1024);
                long availableMemoryMB = totalMemoryMB - usedMemoryMB;
                long memoryLimitMB = totalMemoryMB * _memoryPercent / 100;
                long physicalMB = physicalBytes / (1024 * 1024);
                long virtualMB = virtualBytes / (1024 * 1024);

                // 3. 检查游戏进程是否运行
                bool gameRunning = IsAnyGameRunning();
                var gameProcesses = GetRunningGameProcesses();

                // 计算内存使用百分比
                int usedPercent = (int)(usedMemoryMB * 100 / Math.Max(1, totalMemoryMB));
                const int warnPercent = 85; // 内存占用85%时开始打印详细警告

                // 每次检测打印一行简单日志
                string gameStatus = gameRunning ? string.Join(", ", gameProcesses) : "无";
                Log("INFO", $"检测 {DateTime.Now:HH:mm:ss} | 内存: {usedPercent}% | BGI: {(bgiRunning ? "运行" : "未运行")} | 游戏: {gameStatus}");

                // 当内存占用超过85%时打印详细内存信息
                if (usedPercent >= warnPercent)
                {
                    Log("WARN", $"[内存警告] 已用: {usedMemoryMB}MB/{totalMemoryMB}MB ({usedPercent}%) | 阈值: {_memoryPercent}%");
                    Log("WARN", $"[内存详情] 物理: {physicalMB}MB | 虚拟: {virtualMB}MB | 可用: {availableMemoryMB}MB");
                }

                // 判断是否需要重启
                bool needRestart = false;
                string restartReason = "";

                if (usedMemoryMB > memoryLimitMB)
                {
                    Log("WARN", $"系统内存超限: 已用={usedMemoryMB}MB (阈值={memoryLimitMB}MB, {_memoryPercent}%)");
                    restartReason = "系统内存超限";
                    needRestart = true;
                }

                if (!bgiRunning)
                {
                    // BGI 未运行，重启（无论是否有游戏运行）
                    Log("WARN", "检测到 BGI.exe 未运行");
                    restartReason = string.IsNullOrEmpty(restartReason) ? "进程未运行" : restartReason + " + 进程未运行";
                    needRestart = true;
                }
                else if (!gameRunning)
                {
                    // 游戏已退出，终止 BGI 后重启
                    Log("WARN", "检测到游戏进程退出");
                    TerminateBgiProcess();
                    Log("INFO", "BGI.exe 已终止");
                    restartReason = "游戏退出后重启";
                    needRestart = true;
                }

                if (needRestart && !string.IsNullOrEmpty(restartReason))
                {
                    RestartBgiProcess(restartReason);
                }
            }
            catch (Exception ex)
            {
                Log("ERROR", $"守护循环异常: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 获取系统内存使用情况（物理+虚拟）
    /// </summary>
    private static (long totalBytes, long usedBytes, long physicalBytes, long virtualBytes) GetSystemMemory()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT TotalVisibleMemorySize, FreePhysicalMemory, TotalVirtualMemorySize, FreeVirtualMemory FROM Win32_OperatingSystem");
            foreach (var obj in searcher.Get())
            {
                // 物理内存
                long totalPhysicalKB = Convert.ToInt64(obj["TotalVisibleMemorySize"]);
                long freePhysicalKB = Convert.ToInt64(obj["FreePhysicalMemory"]);
                long usedPhysicalKB = totalPhysicalKB - freePhysicalKB;

                // 虚拟内存（分页文件）
                long totalVirtualKB = Convert.ToInt64(obj["TotalVirtualMemorySize"]);
                long freeVirtualKB = Convert.ToInt64(obj["FreeVirtualMemory"]);
                long usedVirtualKB = totalVirtualKB - freeVirtualKB;

                // 总内存 = 物理内存 + 虚拟内存
                long totalKB = totalPhysicalKB + totalVirtualKB;
                long usedKB = usedPhysicalKB + usedVirtualKB;

                return (totalKB * 1024, usedKB * 1024, totalPhysicalKB * 1024, totalVirtualKB * 1024);
            }
        }
        catch
        {
            // 默认返回 16GB 物理 + 16GB 虚拟
        }
        return (32L * 1024 * 1024 * 1024, 0, 16L * 1024 * 1024 * 1024, 16L * 1024 * 1024 * 1024);
    }

    /// <summary>
    /// 获取系统总物理内存
    /// </summary>
    private static long GetTotalPhysicalMemory()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (var obj in searcher.Get())
            {
                return Convert.ToInt64(obj["TotalPhysicalMemory"]);
            }
        }
        catch
        {
            // 默认返回 16GB
        }
        return 16L * 1024 * 1024 * 1024;
    }

    /// <summary>
    /// 检查指定进程是否运行
    /// </summary>
    private static bool IsProcessRunning(string processName)
    {
        return Process.GetProcessesByName(processName).Length > 0;
    }

    /// <summary>
    /// 检查是否有游戏进程运行
    /// </summary>
    private static bool IsAnyGameRunning()
    {
        foreach (var name in GameProcessNames)
        {
            if (IsProcessRunning(name))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 获取正在运行的游戏进程名称列表
    /// </summary>
    private static List<string> GetRunningGameProcesses()
    {
        var runningGames = new List<string>();
        foreach (var name in GameProcessNames)
        {
            if (IsProcessRunning(name))
            {
                runningGames.Add(name);
            }
        }
        return runningGames;
    }

    /// <summary>
    /// 获取进程内存使用
    /// </summary>
    private static (long workingSet, long virtualMem) GetProcessMemory(string processName)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                return (process.WorkingSet64, process.VirtualMemorySize64);
            }
            catch
            {
                // 忽略访问异常
            }
        }
        return (0, 0);
    }

    /// <summary>
    /// 终止 BGI.exe 进程
    /// </summary>
    private static void TerminateBgiProcess()
    {
        foreach (var process in Process.GetProcessesByName(_bgiProcessName))
        {
            try
            {
                process.Kill();
                process.WaitForExit(3000);
            }
            catch (Exception ex)
            {
                Log("ERROR", $"终止 BGI.exe 失败: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 重启 BGI.exe
    /// </summary>
    private static void RestartBgiProcess(string reason)
    {
        Log("INFO", $"正在重启 BGI.exe, 原因: {reason}");

        // 终止现有进程
        TerminateBgiProcess();

        // 等待
        Thread.Sleep(RestartDelayMs);

        // 重新启动
        StartBgiProcess();
    }

    /// <summary>
    /// 记录日志
    /// </summary>
    private static void Log(string level, string message)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string logMessage = $"[{timestamp}] [BGIguard_v{GetDisplayVersion()}] [{level}] {message}";

        // 控制台输出
        Console.WriteLine(logMessage);

        // 写入文件
        lock (_logLock)
        {
            try
            {
                string logFileName = $"{LogFilePrefix}{DateTime.Now:yyyyMMdd}.log";
                string logPath = Path.Combine(_exeDirectory, logFileName);

                File.AppendAllText(logPath, logMessage + Environment.NewLine);

                // 清理旧日志
                CleanOldLogs();
            }
            catch
            {
                // 忽略写入错误
            }
        }
    }

    /// <summary>
    /// 清理旧日志文件
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
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // 忽略删除错误
                }
            }
        }
        catch
        {
            // 忽略错误
        }
    }
}