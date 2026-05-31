namespace BGIguard;

internal sealed class RuntimeConfigProvider
{
    private readonly ConfigService _configStore;
    private readonly string _exeDirectory;
    private readonly string _betterGiExeName;
    private readonly ConsoleUiService _consoleUi;

    public RuntimeConfigProvider(
        ConfigService configStore,
        string exeDirectory,
        string betterGiExeName,
        ConsoleUiService consoleUi)
    {
        _configStore = configStore;
        _exeDirectory = exeDirectory;
        _betterGiExeName = betterGiExeName;
        _consoleUi = consoleUi;
        Current = _configStore.Load();
    }

    public RuntimeConfig Current { get; private set; }
    public string BetterGiExePath { get; private set; } = "";
    public bool SkipSetup => Current.SkipSetup;
    public int BetterGiMemoryLimitMB => Current.BetterGiMemoryLimitMB;

    public RuntimeConfig Reload()
    {
        Current = _configStore.Load();
        ApplyConfiguredPath(Current);
        return Current;
    }

    public void EnsureBetterGiPath()
    {
        if (!DetectBetterGiPath())
        {
            BetterGiExePath = _consoleUi.PromptForBetterGiPath();
        }
    }

    public GuardRunnerConfig ReloadGuardRunnerConfig(int processWaitExitMs, int restartDelayMs)
    {
        RuntimeConfig config = Reload();
        EnsureBetterGiPath();

        return new GuardRunnerConfig(
            BetterGiExePath,
            config.MonitorIntervalSeconds * 1000,
            config.MemoryPercent,
            config.MissingCountThreshold,
            config.BetterGiMemoryLimitMB,
            processWaitExitMs,
            restartDelayMs);
    }

    private bool DetectBetterGiPath()
    {
        RuntimeConfig config = _configStore.Load();
        string resolvedPath = PathService.ResolveBetterGiPath(_exeDirectory, _betterGiExeName, config.BetterGiPath);
        if (string.IsNullOrEmpty(resolvedPath))
            return false;

        BetterGiExePath = resolvedPath;
        return true;
    }

    private void ApplyConfiguredPath(RuntimeConfig config)
    {
        if (!string.IsNullOrEmpty(config.BetterGiPath) &&
            File.Exists(config.BetterGiPath) &&
            !string.Equals(BetterGiExePath, config.BetterGiPath, StringComparison.OrdinalIgnoreCase))
        {
            BetterGiExePath = config.BetterGiPath;
        }
    }
}
