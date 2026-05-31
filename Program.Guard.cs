namespace BGIguard;

partial class Program
{
    private static void RunGuardLoop()
    {
        var runner = new GuardRunner(
            new GuardRunnerOptions(
                BetterGiExeName.Replace(".exe", ""),
                GameProcessNames,
                _currentUserSid,
                _currentUserName,
                ReloadGuardRunnerConfig,
                GetBetterGiSnapshotForRunner,
                GetRunningGameProcessesForRunner,
                () => MemoryMonitor.GetSystemMemory(Log),
                RestartBetterGiForRunner,
                Thread.Sleep,
                Log),
            new GuardRuntimeState
            {
                CachedCommand = _cachedCommand
            });

        runner.Run();
    }

    private static GuardRunnerConfig ReloadGuardRunnerConfig()
    {
        ApplyRuntimeConfig(LoadConfig());
        return new GuardRunnerConfig(
            _betterGiExePath,
            _monitorIntervalMs,
            _memoryPercent,
            _missingCountThreshold,
            _betterGiMemoryLimitMB,
            ProcessWaitExitMs,
            RestartDelayMs);
    }

    private static BetterGiProcessSnapshot GetBetterGiSnapshotForRunner(GuardRunnerConfig config)
    {
        return ProcessService.GetOwnedProcessSnapshot(
            BetterGiExeName.Replace(".exe", ""),
            config.BetterGiExePath,
            _currentUserSid,
            _currentUserName,
            includeCommandLine: true,
            includeMemory: config.BetterGiMemoryLimitMB > 0,
            Log);
    }

    private static (bool AnyRunning, List<string> RunningNames) GetRunningGameProcessesForRunner()
    {
        return ProcessService.GetRunningOwnedProcesses(GameProcessNames, _currentUserSid, _currentUserName);
    }

    private static void RestartBetterGiForRunner(GuardRunnerConfig config, string cachedCommand)
    {
        ProcessService.TerminateProcessesByCurrentUser(
            BetterGiExeName.Replace(".exe", ""),
            "BetterGI.exe",
            excludePid: null,
            config.ProcessWaitExitMs,
            _currentUserSid,
            _currentUserName,
            Log);

        Thread.Sleep(config.RestartDelayMs);
        ProcessService.StartBetterGiProcess(
            config.BetterGiExePath,
            cachedCommand,
            DangerousCmdArgumentChars,
            Log);
    }
}
