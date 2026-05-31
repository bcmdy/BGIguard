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

}
