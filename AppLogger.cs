namespace BGIguard;

internal sealed class AppLogger
{
    private readonly string _exeDirectory;
    private readonly string _logFilePrefix;
    private readonly int _maxLogFiles;
    private readonly Func<string> _getDisplayVersion;
    private readonly object _logLock = new();
    private DateTime _lastLogCleanupDate = DateTime.MinValue;

    public AppLogger(string exeDirectory, string logFilePrefix, int maxLogFiles, Func<string> getDisplayVersion)
    {
        _exeDirectory = exeDirectory;
        _logFilePrefix = logFilePrefix;
        _maxLogFiles = maxLogFiles;
        _getDisplayVersion = getDisplayVersion;
    }

    public void Write(string level, string message)
    {
        Logger.Write(
            _exeDirectory,
            _logFilePrefix,
            _maxLogFiles,
            _getDisplayVersion(),
            _logLock,
            ref _lastLogCleanupDate,
            level,
            message);
    }
}
