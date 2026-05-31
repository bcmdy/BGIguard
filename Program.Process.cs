namespace BGIguard;

partial class Program
{
    private static void HandleSingleInstance()
    {
        _mutex = ProcessService.EnsureSingleInstance(
            "BGIguard_SingleInstance_Mutex",
            ProcessWaitExitMs,
            _currentUserSid,
            _currentUserName,
            Log);
    }

    private static void TerminateBetterGiProcessByUser()
    {
        ProcessService.TerminateProcessesByCurrentUser(
            BetterGiExeName.Replace(".exe", ""),
            "BetterGI.exe",
            excludePid: null,
            ProcessWaitExitMs,
            _currentUserSid,
            _currentUserName,
            Log);
    }

    private static void StartBetterGiProcess(string? commandLine = null)
    {
        ProcessService.StartBetterGiProcess(
            _betterGiExePath,
            commandLine ?? _cachedCommand,
            DangerousCmdArgumentChars,
            Log);
    }

    private static bool IsBetterGiRunningByUser()
    {
        return ProcessService.GetOwnedProcessSnapshot(
            BetterGiExeName.Replace(".exe", ""),
            _betterGiExePath,
            _currentUserSid,
            _currentUserName,
            includeCommandLine: false,
            includeMemory: false,
            Log).Exists;
    }

    private static void RestartBetterGiProcess()
    {
        TerminateBetterGiProcessByUser();
        Thread.Sleep(RestartDelayMs);
        StartBetterGiProcess(_cachedCommand);
    }
}
