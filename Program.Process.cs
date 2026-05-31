using System.Diagnostics;

namespace BGIguard;

partial class Program
{
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
        ProcessService.TerminateProcessesByCurrentUser(processName, logPrefix, excludePid, ProcessWaitExitMs, _currentUserSid, _currentUserName, Log);
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

        ProcessService.StartDetachedWithCmdStart(_betterGiExePath, cmdArgs, Log);
    }

    /// <summary>
    /// 按用户终止 BetterGI.exe 进程（终止当前用户启动的进程）
    /// </summary>
    private static void TerminateBetterGiProcessByUser()
    {
        TerminateProcessesByUser(BetterGiExeName.Replace(".exe", ""), "BetterGI.exe");
    }

    /// <summary>
    /// 获取 BetterGI 进程当前独占内存（PrivateMemorySize64），单位 MB
    /// </summary>
    private static long GetBetterGiMemoryMB()
    {
        return GetBetterGiSnapshot(includeCommandLine: false, includeMemory: true).MemoryMB;
    }

    /// <summary>
    /// 过滤 cmd.exe /c start 参数中的高风险控制字符。
    /// </summary>
    private static string FilterCmdArguments(string args)
    {
        return CommandLineArguments.FilterDangerousCmdArguments(args, DangerousCmdArgumentChars);
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
        return ProcessService.GetOwnedProcessSnapshot(
            BetterGiExeName.Replace(".exe", ""),
            _betterGiExePath,
            _currentUserSid,
            _currentUserName,
            includeCommandLine,
            includeMemory,
            Log);
    }

    /// <summary>
    /// 获取当前用户正在运行的游戏进程。
    /// </summary>
    private static (bool anyRunning, List<string> runningNames) GetRunningGameProcesses()
    {
        return ProcessService.GetRunningOwnedProcesses(GameProcessNames, _currentUserSid, _currentUserName);
    }

}
