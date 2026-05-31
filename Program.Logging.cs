namespace BGIguard;

partial class Program
{
    private const int MaxLogFiles = 7;
    private const string LogFilePrefix = "BGI_guard";
    private static readonly object _logLock = new();
    private static DateTime _lastLogCleanupDate = DateTime.MinValue;

    private static void Log(string level, string message)
    {
        Logger.Write(_exeDirectory, LogFilePrefix, MaxLogFiles, GetDisplayVersion(), _logLock, ref _lastLogCleanupDate, level, message);
    }

}
