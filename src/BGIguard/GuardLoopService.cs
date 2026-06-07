namespace BGIguard;

internal sealed class GuardLoopService
{
    private readonly string _betterGiProcessName;
    private readonly IReadOnlyCollection<string> _gameProcessNames;
    private readonly RuntimeConfigProvider _configProvider;
    private readonly BetterGiRuntimeService _runtimeService;
    private readonly int _processWaitExitMs;
    private readonly int _restartDelayMs;
    private readonly int _restartCooldownSeconds;
    private readonly Action<string, string> _log;

    public GuardLoopService(
        string betterGiExeName,
        IReadOnlyCollection<string> gameProcessNames,
        RuntimeConfigProvider configProvider,
        BetterGiRuntimeService runtimeService,
        int processWaitExitMs,
        int restartDelayMs,
        int restartCooldownSeconds,
        Action<string, string> log)
    {
        _betterGiProcessName = betterGiExeName.Replace(".exe", "");
        _gameProcessNames = gameProcessNames;
        _configProvider = configProvider;
        _runtimeService = runtimeService;
        _processWaitExitMs = processWaitExitMs;
        _restartDelayMs = restartDelayMs;
        _restartCooldownSeconds = restartCooldownSeconds;
        _log = log;
    }

    public void Run(CancellationToken cancellationToken = default)
    {
        var runner = new GuardRunner(
            new GuardRunnerOptions(
                _betterGiProcessName,
                _gameProcessNames,
                _runtimeService.CurrentUserSid,
                _runtimeService.CurrentUserName,
                ReloadGuardRunnerConfig,
                GetBetterGiSnapshot,
                GetRunningGameProcesses,
                () => MemoryMonitor.GetSystemMemory(_log),
                _runtimeService.RestartBetterGi,
                () => DateTime.UtcNow,
                Sleep,
                _log),
            new GuardRuntimeState
            {
                CachedCommand = _runtimeService.CachedCommand
            });

        runner.Run(cancellationToken);
    }

    private static bool Sleep(int millisecondsTimeout, CancellationToken cancellationToken)
    {
        return !cancellationToken.WaitHandle.WaitOne(millisecondsTimeout);
    }

    private GuardRunnerConfig ReloadGuardRunnerConfig()
    {
        return _configProvider.ReloadGuardRunnerConfig(_processWaitExitMs, _restartDelayMs, _restartCooldownSeconds);
    }

    private BetterGiProcessSnapshot GetBetterGiSnapshot(GuardRunnerConfig config)
    {
        return _runtimeService.GetBetterGiSnapshot(
            config.BetterGiExePath,
            includeCommandLine: true,
            includeMemory: config.BetterGiMemoryLimitMB > 0);
    }

    private (bool AnyRunning, List<string> RunningNames) GetRunningGameProcesses()
    {
        return _runtimeService.GetRunningGameProcesses(_gameProcessNames);
    }
}
