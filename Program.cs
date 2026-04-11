using System.Diagnostics;
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
    private const int MonitorIntervalMs = 5000;
    private const int RestartDelayMs = 1000;
    private const int WorkingSetMemoryLimitMB = 500;
    private const int VirtualMemoryLimitMB = 1500;
    private const int MaxLogFiles = 7;
    private const string GuardFileName = "bgi守护.exe";
    private const string BgiExeName = "BGI.exe";
    private const string BetterGiExeName = "BetterGI.exe";
    private const string LogFilePrefix = "BGI_guard";

    // ============== 全局变量 ==============
    private static string _exeDirectory = null!;
    private static string _cachedCommand = string.Empty;
    private static string _bgiProcessName = "BGI";
    private static Mutex? _mutex;
    private static readonly object _logLock = new();

    // 游戏进程名
    private static readonly string[] GameProcessNames = { "YuanShen", "GenshinImpact" };

    static void Main(string[] args)
    {
        _exeDirectory = AppDomain.CurrentDomain.BaseDirectory;

        // 记录启动日志
        Log("INFO", "BGIguard 启动成功");

        // 检查是否需要文件替换
        CheckAndReplaceFile();

        // 处理单实例保护
        HandleSingleInstance(args);

        // 缓存启动命令
        if (args.Length > 0)
        {
            _cachedCommand = string.Join(" ", args);
            Log("INFO", $"已缓存启动命令: {_cachedCommand}");
        }

        // 启动 BGI.exe
        StartBgiProcess();

        // 进入守护主循环
        RunGuardLoop();
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

            if (File.Exists(betterGiPath) && !File.Exists(bgiPath))
            {
                try
                {
                    // 1. 重命名 BetterGI.exe -> BGI.exe
                    File.Move(betterGiPath, bgiPath);
                    Log("INFO", "已替换文件 BetterGI.exe -> BGI.exe");

                    // 2. 复制自身为 BetterGI.exe
                    string selfPath = Environment.ProcessPath ?? "";
                    File.Copy(selfPath, betterGiPath, true);
                    Log("INFO", $"已复制守护器到 BetterGI.exe");

                    // 3. 使用新名称重新启动
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = betterGiPath,
                        Arguments = string.Join(" ", Environment.GetCommandLineArgs().Skip(1)),
                        UseShellExecute = true
                    };
                    Process.Start(startInfo);
                    Log("INFO", "以 BetterGI.exe 身份重新启动");

                    // 退出当前进程
                    Environment.Exit(0);
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
                    // 获取旧进程的启动命令
                    string? oldCommand = GetProcessStartupCommand(process.Id);
                    if (!string.IsNullOrEmpty(oldCommand))
                    {
                        _cachedCommand = oldCommand;
                        Log("INFO", $"已获取旧进程启动命令: {_cachedCommand}");
                    }

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
    /// 获取进程的启动命令行
    /// </summary>
    private static string? GetProcessStartupCommand(int processId)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}");

            foreach (var obj in searcher.Get())
            {
                return obj["CommandLine"]?.ToString();
            }
        }
        catch
        {
            // 忽略错误
        }
        return null;
    }

    /// <summary>
    /// 启动 BGI.exe 进程
    /// </summary>
    private static void StartBgiProcess()
    {
        string bgiPath = Path.Combine(_exeDirectory, BgiExeName);

        if (!File.Exists(bgiPath))
        {
            Log("ERROR", $"找不到 BGI.exe: {bgiPath}");
            return;
        }

        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = bgiPath,
                Arguments = _cachedCommand,
                UseShellExecute = true,
                WorkingDirectory = _exeDirectory
            };

            Process.Start(startInfo);
            Log("INFO", $"BGI.exe 已启动, 命令: {_cachedCommand}");
        }
        catch (Exception ex)
        {
            Log("ERROR", $"启动 BGI.exe 失败: {ex.Message}");
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
            try
            {
                // 1. 检查 BGI.exe 是否存在
                bool bgiRunning = IsProcessRunning(_bgiProcessName);

                // 2. 检查内存使用
                if (bgiRunning)
                {
                    var (workingSet, virtualMem) = GetProcessMemory(_bgiProcessName);
                    bool needRestart = false;

                    if (workingSet > WorkingSetMemoryLimitMB * 1024 * 1024)
                    {
                        Log("WARN", $"BGI.exe 内存超限: 工作集={workingSet / 1024 / 1024}MB");
                        needRestart = true;
                    }

                    if (virtualMem > VirtualMemoryLimitMB * 1024 * 1024)
                    {
                        Log("WARN", $"BGI.exe 内存超限: 虚拟内存={virtualMem / 1024 / 1024}MB");
                        needRestart = true;
                    }

                    if (needRestart)
                    {
                        RestartBgiProcess("内存超限");
                    }
                }

                // 3. 检查游戏进程是否运行
                bool gameRunning = IsAnyGameRunning();

                if (!gameRunning && bgiRunning)
                {
                    // 游戏已退出，终止 BGI.exe
                    Log("WARN", "检测到游戏进程退出");
                    TerminateBgiProcess();
                    Log("INFO", "BGI.exe 已终止");
                }
                else if (!bgiRunning && _cachedCommand.Length > 0)
                {
                    // BGI.exe 未运行，重启
                    Log("WARN", "检测到 BGI.exe 未运行");
                    RestartBgiProcess("进程未运行");
                }
            }
            catch (Exception ex)
            {
                Log("ERROR", $"守护循环异常: {ex.Message}");
            }

            Thread.Sleep(MonitorIntervalMs);
        }
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
        string logMessage = $"[{timestamp}] [{level}] {message}";

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