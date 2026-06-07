namespace BGIguard.Tests;

public sealed class BetterGiRuntimeServiceTests
{
    [Fact]
    public void EnsureStartedAndCacheCommand_StartsWhenMissingAndCachesLatestCommand()
    {
        var operations = new FakeProcessOperations();
        operations.Snapshots.Enqueue(default);
        operations.Snapshots.Enqueue(new BetterGiProcessSnapshot(true, "\"C:\\Apps\\BetterGI.exe\" --profile test", 0));
        var service = CreateService(operations);

        service.EnsureStartedAndCacheCommand(@"C:\Apps\BetterGI.exe");

        Assert.Single(operations.StartRequests);
        Assert.Equal(@"C:\Apps\BetterGI.exe", operations.StartRequests[0].ExePath);
        Assert.Equal("", operations.StartRequests[0].CommandLine);
        Assert.Equal("--profile test", service.CachedCommand);
        Assert.Equal(500, operations.SleepCalls.Single());
    }

    [Fact]
    public void RestartBetterGi_TerminatesOwnedProcessAndStartsWithCachedCommand()
    {
        var operations = new FakeProcessOperations();
        var service = CreateService(operations);
        var config = new GuardRunnerConfig(
            BetterGiExePath: @"C:\Apps\BetterGI.exe",
            MonitorIntervalMs: 5000,
            MemoryPercent: 85,
            MissingCountThreshold: 6,
            BetterGiMemoryLimitMB: 4096,
            ProcessWaitExitMs: 3000,
            RestartDelayMs: 1000,
            RestartCooldownSeconds: 60);

        service.RestartBetterGi(config, "--from-cache");

        Assert.Single(operations.TerminateRequests);
        Assert.Equal("BetterGI", operations.TerminateRequests[0].ProcessName);
        Assert.Equal("BetterGI.exe", operations.TerminateRequests[0].LogPrefix);
        Assert.Single(operations.StartRequests);
        Assert.Equal("--from-cache", operations.StartRequests[0].CommandLine);
        Assert.Equal(1000, operations.SleepCalls.Single());
    }

    private static BetterGiRuntimeService CreateService(FakeProcessOperations operations)
    {
        return new BetterGiRuntimeService(
            "BetterGI.exe",
            processWaitExitMs: 3000,
            restartDelayMs: 1000,
            log: (_, _) => { },
            processOperations: operations,
            currentUserSid: "S-1-5-21-test",
            currentUserName: @"DOMAIN\User",
            sleep: operations.SleepCalls.Add);
    }

    private sealed class FakeProcessOperations : IProcessOperations
    {
        public Queue<BetterGiProcessSnapshot> Snapshots { get; } = new();
        public List<(string ExePath, string CommandLine)> StartRequests { get; } = new();
        public List<(string ProcessName, string LogPrefix)> TerminateRequests { get; } = new();
        public List<int> SleepCalls { get; } = new();

        public Mutex EnsureSingleInstance(
            string mutexName,
            int waitExitMs,
            string currentUserSid,
            string currentUserName,
            Action<string, string> log)
        {
            return new Mutex(false);
        }

        public bool StartBetterGiProcess(
            string exePath,
            string commandLine,
            char[] dangerousCmdArgumentChars,
            Action<string, string> log)
        {
            StartRequests.Add((exePath, commandLine));
            return true;
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
            return Snapshots.Count > 0 ? Snapshots.Dequeue() : default;
        }

        public (bool AnyRunning, List<string> RunningNames) GetRunningOwnedProcesses(
            IReadOnlyCollection<string> processNames,
            string currentUserSid,
            string currentUserName)
        {
            return (false, new List<string>());
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
            TerminateRequests.Add((processName, logPrefix));
        }
    }
}
