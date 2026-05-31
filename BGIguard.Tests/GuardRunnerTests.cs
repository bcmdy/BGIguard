namespace BGIguard.Tests;

public sealed class GuardRunnerTests
{
    [Fact]
    public void RunOnce_RestartsAndResetsMissingCount_WhenMissingThresholdReached()
    {
        GuardRunnerConfig config = CreateConfig(missingCountThreshold: 2);
        var state = new GuardRuntimeState { MissingCount = 1 };
        bool restarted = false;

        var runner = CreateRunner(
            state,
            config,
            betterGiSnapshot: default,
            gameRunning: true,
            restart: (_, _) => restarted = true);

        runner.RunOnce(config);

        Assert.True(restarted);
        Assert.Equal(0, state.MissingCount);
    }

    [Fact]
    public void RunOnce_RestartsAndResetsGameExitCount_WhenGameExitThresholdReached()
    {
        GuardRunnerConfig config = CreateConfig(missingCountThreshold: 2);
        var state = new GuardRuntimeState { GameExitCount = 1 };
        bool restarted = false;

        var runner = CreateRunner(
            state,
            config,
            betterGiSnapshot: new BetterGiProcessSnapshot(true, null, 100),
            gameRunning: false,
            restart: (_, _) => restarted = true);

        runner.RunOnce(config);

        Assert.True(restarted);
        Assert.Equal(0, state.GameExitCount);
    }

    [Fact]
    public void RunOnce_UsesLatestCachedCommand_WhenRestarting()
    {
        GuardRunnerConfig config = CreateConfig(missingCountThreshold: 1);
        var state = new GuardRuntimeState { CachedCommand = "--old" };
        string? restartCommand = null;

        var runner = CreateRunner(
            state,
            config,
            betterGiSnapshot: new BetterGiProcessSnapshot(true, "\"C:\\Apps\\BetterGI.exe\" --new", 5000),
            gameRunning: true,
            restart: (_, command) => restartCommand = command);

        runner.RunOnce(config);

        Assert.Equal("--new", state.CachedCommand);
        Assert.Equal("--new", restartCommand);
    }

    private static GuardRunner CreateRunner(
        GuardRuntimeState state,
        GuardRunnerConfig config,
        BetterGiProcessSnapshot betterGiSnapshot,
        bool gameRunning,
        Action<GuardRunnerConfig, string> restart)
    {
        return new GuardRunner(
            new GuardRunnerOptions(
                "BetterGI",
                new[] { "Game" },
                "sid",
                "user",
                () => config,
                _ => betterGiSnapshot,
                () => (gameRunning, gameRunning ? new List<string> { "Game" } : new List<string>()),
                () => new SystemMemorySnapshot(1000, 100, 100, 0),
                restart,
                _ => { },
                (_, _) => { }),
            state);
    }

    private static GuardRunnerConfig CreateConfig(int missingCountThreshold)
    {
        return new GuardRunnerConfig(
            BetterGiExePath: @"C:\Apps\BetterGI.exe",
            MonitorIntervalMs: 5000,
            MemoryPercent: 90,
            MissingCountThreshold: missingCountThreshold,
            BetterGiMemoryLimitMB: 4096,
            ProcessWaitExitMs: 3000,
            RestartDelayMs: 1000);
    }
}
