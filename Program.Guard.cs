namespace BGIguard;

partial class Program
{
    private static void RunGuardLoop()
    {
        Log("INFO", "进入守护监控循环");

        while (true)
        {
            ApplyRuntimeConfig(LoadConfig());
            Thread.Sleep(_monitorIntervalMs);

            try
            {
                ApplyRuntimeConfig(LoadConfig());

                // 1. 获取 BetterGI 进程信息、命令行和内存占用（单次遍历）
                var betterGiSnapshot = ProcessService.GetOwnedProcessSnapshot(
                    BetterGiExeName.Replace(".exe", ""),
                    _betterGiExePath,
                    _currentUserSid,
                    _currentUserName,
                    includeCommandLine: true,
                    includeMemory: _betterGiMemoryLimitMB > 0,
                    Log);
                bool betterGiRunning = betterGiSnapshot.Exists;
                if (betterGiRunning && betterGiSnapshot.CommandLine != null)
                {
                    string extractedArgs = CommandLine.ExtractArgs(betterGiSnapshot.CommandLine);
                    string cleanedArgs = CommandLineArguments.CleanCommandArgs(extractedArgs);  // 必须先清理

                    if (cleanedArgs != _cachedCommand)
                    {
                        _cachedCommand = cleanedArgs;
                        if (!string.IsNullOrEmpty(_cachedCommand))
                            Log("INFO", $"已缓存启动命令: {_cachedCommand}");
                        else
                            Log("INFO", "检测到无启动参数");
                    }
                }

                // 2. 检查游戏进程
                var (gameRunning, gameProcesses) = ProcessService.GetRunningOwnedProcesses(GameProcessNames, _currentUserSid, _currentUserName);

                // 3. 检查系统内存
                var memorySnapshot = MemoryMonitor.GetSystemMemory(Log);
                long totalMB = memorySnapshot.TotalMB;
                long usedMB = memorySnapshot.UsedMB;
                long physicalMB = memorySnapshot.PhysicalMB;
                long virtualMB = memorySnapshot.VirtualMB;
                long memoryLimitMB = ConfigService.CalculateMemoryLimitMB(totalMB, _memoryPercent);
                int usedPercent = (int)(usedMB * 100 / Math.Max(1, totalMB));

                // 4. 检查 BetterGI 进程自身内存（OOM 精准监控）
                long betterGiMemMB = 0;
                if (_betterGiMemoryLimitMB > 0 && betterGiRunning)
                {
                    betterGiMemMB = betterGiSnapshot.MemoryMB;
                }

                // 打印检测日志
                string gameStatus = gameRunning ? string.Join(", ", gameProcesses) : "无";
                string giStatus = betterGiRunning ? "运行" : $"未运行({_missingCount}/{_missingCountThreshold})";
                string giMemStatus = (_betterGiMemoryLimitMB > 0 && betterGiRunning)
                    ? $" | BetterGI内存: {betterGiMemMB}MB"
                    : "";
                Log("INFO", $"检测 {DateTime.Now:HH:mm:ss} | 内存: {usedPercent}% | BetterGI: {giStatus}{giMemStatus} | 游戏: {gameStatus}");

                // 内存警告 (使用配置值-5作为阈值)
                int memWarningThreshold = Math.Max(1, _memoryPercent - 5);
                if (usedPercent >= memWarningThreshold)
                {
                    Log("WARN", $"[系统内存警告] 已用: {usedMB}MB/{totalMB}MB ({usedPercent}%) | 物理: {physicalMB}MB | 虚拟: {virtualMB}MB");
                }

                // ========== 进程级内存超限检查（精准 OOM 防护）==========
                if (GuardService.ShouldRestartForProcessMemory(betterGiRunning, betterGiMemMB, _betterGiMemoryLimitMB))
                {
                    Log("WARN", $"[进程内存超限] BetterGI 占用 {betterGiMemMB}MB > 阈值 {_betterGiMemoryLimitMB}MB，正在重启...");
                    TerminateBetterGiProcessByUser();
                    Thread.Sleep(RestartDelayMs);
                    StartBetterGiProcess(_cachedCommand);
                    Log("INFO", "进程内存超限后已重启");
                    // 重置相关计数
                    _missingCount = 0;
                    continue;  // 本轮已处理，跳过下方逻辑
                }

                // 判断是否需要重启（BetterGI 丢失）
                if (!betterGiRunning)
                {
                    _missingCount++;
                    Log("WARN", $"BetterGI.exe 丢失 (第 {_missingCount} 次)");

                    if (GuardService.ShouldRestartForMissingProcess(betterGiRunning, _missingCount, _missingCountThreshold))
                    {
                        Log("INFO", "连续丢失达到阈值，正在重启...");
                        TerminateBetterGiProcessByUser();
                        Thread.Sleep(RestartDelayMs);
                        StartBetterGiProcess(_cachedCommand);
                        _missingCount = 0;
                    }
                }
                else
                {
                    // 进程存在，重置计数
                    if (_missingCount > 0)
                    {
                        Log("INFO", "BetterGI.exe 已恢复，计数重置");
                        _missingCount = 0;
                    }
                }

                // 系统内存超限时重启
                if (GuardService.ShouldRestartForSystemMemory(usedMB, memoryLimitMB))
                {
                    Log("WARN", $"[系统内存超限] {usedMB}MB > {memoryLimitMB}MB ({_memoryPercent}%)");
                    TerminateBetterGiProcessByUser();
                    Thread.Sleep(RestartDelayMs);
                    StartBetterGiProcess(_cachedCommand);
                    Log("INFO", "系统内存超限后已重启");
                }

                // 游戏退出后重启（使用与 BetterGI 相同的计次阈值）
                if (!gameRunning && betterGiRunning)
                {
                    _gameExitCount++;
                    Log("WARN", $"游戏已退出 (第 {_gameExitCount} 次)");

                    if (GuardService.ShouldRestartForGameExit(gameRunning, betterGiRunning, _gameExitCount, _missingCountThreshold))
                    {
                        Log("INFO", $"游戏退出达到阈值，终止 BetterGI.exe (当前用户:{_currentUserName}, SID:{_currentUserSid})");
                        TerminateBetterGiProcessByUser();
                        Thread.Sleep(RestartDelayMs);
                        StartBetterGiProcess(_cachedCommand);
                        _gameExitCount = 0;
                    }
                }
                else if (gameRunning)
                {
                    if (_gameExitCount > 0)
                    {
                        Log("INFO", "游戏已恢复，计数重置");
                        _gameExitCount = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Log("ERROR", $"守护循环异常: {ex.Message}");
            }
        }
    }
}
