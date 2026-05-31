namespace BGIguard;

internal sealed class BetterGiRuntimeService
{
    private static readonly char[] DangerousCmdArgumentChars = { '&', '|', '<', '>', '^', '%', '\r', '\n' };

    private readonly string _betterGiExeName;
    private readonly string _betterGiProcessName;
    private readonly int _processWaitExitMs;
    private readonly int _restartDelayMs;
    private readonly string _currentUserSid;
    private readonly string _currentUserName;
    private readonly Action<string, string> _log;
    private Mutex? _mutex;

    public BetterGiRuntimeService(
        string betterGiExeName,
        int processWaitExitMs,
        int restartDelayMs,
        Action<string, string> log)
    {
        _betterGiExeName = betterGiExeName;
        _betterGiProcessName = betterGiExeName.Replace(".exe", "");
        _processWaitExitMs = processWaitExitMs;
        _restartDelayMs = restartDelayMs;
        _log = log;
        _currentUserSid = CurrentUserService.GetCurrentUserSid();
        _currentUserName = CurrentUserService.GetCurrentUserDisplayName();
    }

    public string CurrentUserSid => _currentUserSid;
    public string CurrentUserName => _currentUserName;
    public string CachedCommand { get; private set; } = "";

    public void EnsureSingleInstance()
    {
        _mutex = ProcessService.EnsureSingleInstance(
            "BGIguard_SingleInstance_Mutex",
            _processWaitExitMs,
            _currentUserSid,
            _currentUserName,
            _log);
    }

    public void EnsureStartedAndCacheCommand(string exePath)
    {
        if (!IsBetterGiRunning(exePath))
        {
            StartBetterGiProcess(exePath);
        }
        else
        {
            _log("INFO", "BetterGI.exe 已在运行中，跳过启动");
        }

        Thread.Sleep(500);
        BetterGiProcessSnapshot initialSnapshot = GetBetterGiSnapshot(exePath, includeCommandLine: true, includeMemory: false);
        if (initialSnapshot.Exists && initialSnapshot.CommandLine != null)
        {
            CachedCommand = CommandLine.ExtractArgs(initialSnapshot.CommandLine);
            _log("INFO", $"已缓存启动命令: {CachedCommand}");
        }
    }

    public void StartBetterGiProcess(string exePath, string? commandLine = null)
    {
        ProcessService.StartBetterGiProcess(
            exePath,
            commandLine ?? CachedCommand,
            DangerousCmdArgumentChars,
            _log);
    }

    public bool IsBetterGiRunning(string exePath)
    {
        return GetBetterGiSnapshot(exePath, includeCommandLine: false, includeMemory: false).Exists;
    }

    public BetterGiProcessSnapshot GetBetterGiSnapshot(
        string exePath,
        bool includeCommandLine,
        bool includeMemory)
    {
        return ProcessService.GetOwnedProcessSnapshot(
            _betterGiProcessName,
            exePath,
            _currentUserSid,
            _currentUserName,
            includeCommandLine,
            includeMemory,
            _log);
    }

    public (bool AnyRunning, List<string> RunningNames) GetRunningGameProcesses(IReadOnlyCollection<string> gameProcessNames)
    {
        return ProcessService.GetRunningOwnedProcesses(gameProcessNames, _currentUserSid, _currentUserName);
    }

    public void RestartBetterGi(GuardRunnerConfig config, string cachedCommand)
    {
        ProcessService.TerminateProcessesByCurrentUser(
            _betterGiProcessName,
            _betterGiExeName,
            excludePid: null,
            config.ProcessWaitExitMs,
            _currentUserSid,
            _currentUserName,
            _log);

        Thread.Sleep(_restartDelayMs);
        StartBetterGiProcess(config.BetterGiExePath, cachedCommand);
    }
}
