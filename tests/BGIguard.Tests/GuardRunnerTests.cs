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

    [Fact]
    public void Run_UsesSameConfigSnapshotForSleepAndRunOnce()
    {
        var firstConfig = CreateConfig(missingCountThreshold: 1) with { MonitorIntervalMs = 1234 };
        var secondConfig = CreateConfig(missingCountThreshold: 2) with { MonitorIntervalMs = 9999 };
        var state = new GuardRuntimeState();
        int reloadCount = 0;
        int sleptMs = 0;
        int? observedThreshold = null;
        int errors = 0;

        var runner = new GuardRunner(
            new GuardRunnerOptions(
                "BetterGI",
                new[] { "Game" },
                "sid",
                "user",
                () => reloadCount++ == 0 ? firstConfig : throw new InvalidOperationException("stop"),
                config =>
                {
                    observedThreshold = config.MissingCountThreshold;
                    throw new InvalidOperationException("stop");
                },
                () => (true, new List<string> { "Game" }),
                () => new SystemMemorySnapshot(1000, 100, 100, 0),
                (_, _) => { },
                () => DateTime.UtcNow,
                (ms, _) =>
                {
                    sleptMs = ms;
                    return true;
                },
                (level, _) =>
                {
                    if (level == "ERROR")
                        errors++;
                }),
            state);

        Assert.Throws<InvalidOperationException>(() => runner.Run());

        Assert.Equal(1234, sleptMs);
        Assert.Equal(1, observedThreshold);
        Assert.Equal(2, reloadCount);
        Assert.Equal(1, errors);
    }

    [Fact]
    public void Run_StopsWithoutRunOnce_WhenSleepIsCancelled()
    {
        GuardRunnerConfig config = CreateConfig(missingCountThreshold: 1);
        int reloadCount = 0;
        bool runOnceCalled = false;

        var runner = new GuardRunner(
            new GuardRunnerOptions(
                "BetterGI",
                new[] { "Game" },
                "sid",
                "user",
                () =>
                {
                    reloadCount++;
                    return config;
                },
                _ =>
                {
                    runOnceCalled = true;
                    return default;
                },
                () => (true, new List<string> { "Game" }),
                () => new SystemMemorySnapshot(1000, 100, 100, 0),
                (_, _) => { },
                () => DateTime.UtcNow,
                (_, _) => false,
                (_, _) => { }),
            new GuardRuntimeState());

        runner.Run();

        Assert.Equal(1, reloadCount);
        Assert.False(runOnceCalled);
    }

    [Fact]
    public void RunOnce_SkipsRestart_WhenRestartCooldownIsActive()
    {
        DateTime now = new DateTime(2026, 6, 7, 12, 0, 0, DateTimeKind.Utc);
        GuardRunnerConfig config = CreateConfig(missingCountThreshold: 1) with { RestartCooldownSeconds = 60 };
        var state = new GuardRuntimeState { LastRestartUtc = now.AddSeconds(-10) };
        bool restarted = false;

        var runner = CreateRunner(
            state,
            config,
            betterGiSnapshot: new BetterGiProcessSnapshot(true, null, 5000),
            gameRunning: true,
            restart: (_, _) => restarted = true,
            getUtcNow: () => now);

        runner.RunOnce(config);

        Assert.False(restarted);
        Assert.Equal(now.AddSeconds(-10), state.LastRestartUtc);
    }

    [Fact]
    public void RunOnce_Restarts_WhenRestartCooldownHasElapsed()
    {
        DateTime now = new DateTime(2026, 6, 7, 12, 0, 0, DateTimeKind.Utc);
        GuardRunnerConfig config = CreateConfig(missingCountThreshold: 1) with { RestartCooldownSeconds = 60 };
        var state = new GuardRuntimeState { LastRestartUtc = now.AddSeconds(-61) };
        bool restarted = false;

        var runner = CreateRunner(
            state,
            config,
            betterGiSnapshot: new BetterGiProcessSnapshot(true, null, 5000),
            gameRunning: true,
            restart: (_, _) => restarted = true,
            getUtcNow: () => now);

        runner.RunOnce(config);

        Assert.True(restarted);
        Assert.Equal(now, state.LastRestartUtc);
    }

    private static GuardRunner CreateRunner(
        GuardRuntimeState state,
        GuardRunnerConfig config,
        BetterGiProcessSnapshot betterGiSnapshot,
        bool gameRunning,
        Action<GuardRunnerConfig, string> restart,
        Func<DateTime>? getUtcNow = null)
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
                getUtcNow ?? (() => DateTime.UtcNow),
                (_, _) => true,
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
            RestartDelayMs: 1000,
            RestartCooldownSeconds: 60);
    }
}
