namespace BGIguard;

internal interface IProcessOperations
{
    Mutex EnsureSingleInstance(
        string mutexName,
        int waitExitMs,
        string currentUserSid,
        string currentUserName,
        Action<string, string> log);

    bool StartBetterGiProcess(
        string exePath,
        string commandLine,
        char[] dangerousCmdArgumentChars,
        Action<string, string> log);

    BetterGiProcessSnapshot GetOwnedProcessSnapshot(
        string processName,
        string expectedExePath,
        string currentUserSid,
        string currentUserName,
        bool includeCommandLine,
        bool includeMemory,
        Action<string, string> log);

    (bool AnyRunning, List<string> RunningNames) GetRunningOwnedProcesses(
        IReadOnlyCollection<string> processNames,
        string currentUserSid,
        string currentUserName);

    void TerminateProcessesByCurrentUser(
        string processName,
        string logPrefix,
        int? excludePid,
        int waitExitMs,
        string currentUserSid,
        string currentUserName,
        Action<string, string> log);
}

internal sealed class WindowsProcessOperations : IProcessOperations
{
    public Mutex EnsureSingleInstance(
        string mutexName,
        int waitExitMs,
        string currentUserSid,
        string currentUserName,
        Action<string, string> log)
    {
        return ProcessService.EnsureSingleInstance(mutexName, waitExitMs, currentUserSid, currentUserName, log);
    }

    public bool StartBetterGiProcess(
        string exePath,
        string commandLine,
        char[] dangerousCmdArgumentChars,
        Action<string, string> log)
    {
        return ProcessService.StartBetterGiProcess(exePath, commandLine, dangerousCmdArgumentChars, log);
    }

    public BetterGiProcessSnapshot GetOwnedProcessSnapshot(
        string processName,
        string expectedExePath,
        string currentUserSid,
        string currentUserName,
        bool includeCommandLine,
        bool includeMemory,
        Action<string, string> log)
    {
        return ProcessService.GetOwnedProcessSnapshot(
            processName,
            expectedExePath,
            currentUserSid,
            currentUserName,
            includeCommandLine,
            includeMemory,
            log);
    }

    public (bool AnyRunning, List<string> RunningNames) GetRunningOwnedProcesses(
        IReadOnlyCollection<string> processNames,
        string currentUserSid,
        string currentUserName)
    {
        return ProcessService.GetRunningOwnedProcesses(processNames, currentUserSid, currentUserName);
    }

    public void TerminateProcessesByCurrentUser(
        string processName,
        string logPrefix,
        int? excludePid,
        int waitExitMs,
        string currentUserSid,
        string currentUserName,
        Action<string, string> log)
    {
        ProcessService.TerminateProcessesByCurrentUser(
            processName,
            logPrefix,
            excludePid,
            waitExitMs,
            currentUserSid,
            currentUserName,
            log);
    }
}
