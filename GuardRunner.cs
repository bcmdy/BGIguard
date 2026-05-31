namespace BGIguard;

internal sealed class GuardRunner
{
    private readonly GuardRunnerOptions _options;
    private readonly GuardRuntimeState _state;

    public GuardRunner(GuardRunnerOptions options, GuardRuntimeState state)
    {
        _options = options;
        _state = state;
    }

    public void Run()
    {
        _options.Log("INFO", "进入守护监控循环");

        while (true)
        {
            GuardRunnerConfig config = _options.ReloadConfig();
            _options.Sleep(config.MonitorIntervalMs);

            try
            {
                RunOnce(_options.ReloadConfig());
            }
            catch (Exception ex)
            {
                _options.Log("ERROR", $"守护循环异常: {ex.Message}");
            }
        }
    }

    public void RunOnce(GuardRunnerConfig config)
    {
        var betterGiSnapshot = _options.GetBetterGiSnapshot(config);
        bool betterGiRunning = betterGiSnapshot.Exists;
        CacheCommandLine(betterGiRunning, betterGiSnapshot.CommandLine);

        var (gameRunning, gameProcesses) = _options.GetRunningGameProcesses();

        var memorySnapshot = _options.GetSystemMemory();
        long memoryLimitMB = ConfigService.CalculateMemoryLimitMB(memorySnapshot.TotalMB, config.MemoryPercent);
        int usedPercent = (int)(memorySnapshot.UsedMB * 100 / Math.Max(1, memorySnapshot.TotalMB));

        long betterGiMemMB = config.BetterGiMemoryLimitMB > 0 && betterGiRunning
            ? betterGiSnapshot.MemoryMB
            : 0;

        LogStatus(config, betterGiRunning, gameRunning, gameProcesses, memorySnapshot, usedPercent, betterGiMemMB);

        int memWarningThreshold = Math.Max(1, config.MemoryPercent - 5);
        if (usedPercent >= memWarningThreshold)
        {
            _options.Log("WARN", $"[系统内存警告] 已用: {memorySnapshot.UsedMB}MB/{memorySnapshot.TotalMB}MB ({usedPercent}%) | 物理: {memorySnapshot.PhysicalMB}MB | 虚拟: {memorySnapshot.VirtualMB}MB");
        }

        if (GuardService.ShouldRestartForProcessMemory(betterGiRunning, betterGiMemMB, config.BetterGiMemoryLimitMB))
        {
            _options.Log("WARN", $"[进程内存超限] BetterGI 占用 {betterGiMemMB}MB > 阈值 {config.BetterGiMemoryLimitMB}MB，正在重启...");
            _options.RestartBetterGi(config, _state.CachedCommand);
            _options.Log("INFO", "进程内存超限后已重启");
            _state.MissingCount = 0;
            return;
        }

        HandleMissingProcess(config, betterGiRunning);
        HandleSystemMemory(config, memorySnapshot.UsedMB, memoryLimitMB);
        HandleGameExit(config, gameRunning, betterGiRunning);
    }

    private void CacheCommandLine(bool betterGiRunning, string? commandLine)
    {
        if (!betterGiRunning || commandLine == null)
            return;

        string extractedArgs = CommandLine.ExtractArgs(commandLine);
        string cleanedArgs = CommandLineArguments.CleanCommandArgs(extractedArgs);

        if (cleanedArgs == _state.CachedCommand)
            return;

        _state.CachedCommand = cleanedArgs;
        if (!string.IsNullOrEmpty(_state.CachedCommand))
            _options.Log("INFO", $"已缓存启动命令: {_state.CachedCommand}");
        else
            _options.Log("INFO", "检测到无启动参数");
    }

    private void LogStatus(
        GuardRunnerConfig config,
        bool betterGiRunning,
        bool gameRunning,
        List<string> gameProcesses,
        SystemMemorySnapshot memorySnapshot,
        int usedPercent,
        long betterGiMemMB)
    {
        string gameStatus = gameRunning ? string.Join(", ", gameProcesses) : "无";
        string giStatus = betterGiRunning ? "运行" : $"未运行({_state.MissingCount}/{config.MissingCountThreshold})";
        string giMemStatus = config.BetterGiMemoryLimitMB > 0 && betterGiRunning
            ? $" | BetterGI内存: {betterGiMemMB}MB"
            : "";
        _options.Log("INFO", $"检测 {DateTime.Now:HH:mm:ss} | 内存: {usedPercent}% | BetterGI: {giStatus}{giMemStatus} | 游戏: {gameStatus}");
    }

    private void HandleMissingProcess(GuardRunnerConfig config, bool betterGiRunning)
    {
        if (!betterGiRunning)
        {
            _state.MissingCount++;
            _options.Log("WARN", $"BetterGI.exe 丢失 (第 {_state.MissingCount} 次)");

            if (GuardService.ShouldRestartForMissingProcess(betterGiRunning, _state.MissingCount, config.MissingCountThreshold))
            {
                _options.Log("INFO", "连续丢失达到阈值，正在重启...");
                _options.RestartBetterGi(config, _state.CachedCommand);
                _state.MissingCount = 0;
            }
        }
        else if (_state.MissingCount > 0)
        {
            _options.Log("INFO", "BetterGI.exe 已恢复，计数重置");
            _state.MissingCount = 0;
        }
    }

    private void HandleSystemMemory(GuardRunnerConfig config, long usedMB, long memoryLimitMB)
    {
        if (!GuardService.ShouldRestartForSystemMemory(usedMB, memoryLimitMB))
            return;

        _options.Log("WARN", $"[系统内存超限] {usedMB}MB > {memoryLimitMB}MB ({config.MemoryPercent}%)");
        _options.RestartBetterGi(config, _state.CachedCommand);
        _options.Log("INFO", "系统内存超限后已重启");
    }

    private void HandleGameExit(GuardRunnerConfig config, bool gameRunning, bool betterGiRunning)
    {
        if (!gameRunning && betterGiRunning)
        {
            _state.GameExitCount++;
            _options.Log("WARN", $"游戏已退出 (第 {_state.GameExitCount} 次)");

            if (GuardService.ShouldRestartForGameExit(gameRunning, betterGiRunning, _state.GameExitCount, config.MissingCountThreshold))
            {
                _options.Log("INFO", $"游戏退出达到阈值，终止 BetterGI.exe (当前用户:{_options.CurrentUserName}, SID:{_options.CurrentUserSid})");
                _options.RestartBetterGi(config, _state.CachedCommand);
                _state.GameExitCount = 0;
            }
        }
        else if (gameRunning && _state.GameExitCount > 0)
        {
            _options.Log("INFO", "游戏已恢复，计数重置");
            _state.GameExitCount = 0;
        }
    }
}

internal sealed class GuardRuntimeState
{
    public string CachedCommand { get; set; } = "";
    public int MissingCount { get; set; }
    public int GameExitCount { get; set; }
}

internal sealed record GuardRunnerOptions(
    string BetterGiProcessName,
    IReadOnlyCollection<string> GameProcessNames,
    string CurrentUserSid,
    string CurrentUserName,
    Func<GuardRunnerConfig> ReloadConfig,
    Func<GuardRunnerConfig, BetterGiProcessSnapshot> GetBetterGiSnapshot,
    Func<(bool AnyRunning, List<string> RunningNames)> GetRunningGameProcesses,
    Func<SystemMemorySnapshot> GetSystemMemory,
    Action<GuardRunnerConfig, string> RestartBetterGi,
    Action<int> Sleep,
    Action<string, string> Log);

internal sealed record GuardRunnerConfig(
    string BetterGiExePath,
    int MonitorIntervalMs,
    int MemoryPercent,
    int MissingCountThreshold,
    int BetterGiMemoryLimitMB,
    int ProcessWaitExitMs,
    int RestartDelayMs);
