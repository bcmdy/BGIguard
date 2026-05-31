namespace BGIguard;

partial class Program
{
    /// <summary>
    /// 处理单实例保护
    /// </summary>
    private static void HandleSingleInstance()
    {
        _mutex = ProcessService.EnsureSingleInstance(
            "BGIguard_SingleInstance_Mutex",
            ProcessWaitExitMs,
            _currentUserSid,
            _currentUserName,
            Log);
    }

    /// <summary>
    /// 按用户终止指定进程
    /// </summary>
    private static void TerminateProcessesByUser(string processName, string logPrefix, int? excludePid = null)
    {
        ProcessService.TerminateProcessesByCurrentUser(processName, logPrefix, excludePid, ProcessWaitExitMs, _currentUserSid, _currentUserName, Log);
    }

    /// <summary>
    /// 启动 BetterGI.exe 进程
    /// </summary>
    private static void StartBetterGiProcess(string? commandLine = null)
    {
        ProcessService.StartBetterGiProcess(
            _betterGiExePath,
            commandLine ?? _cachedCommand,
            DangerousCmdArgumentChars,
            Log);
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
