namespace BGIguard;

partial class Program
{
    private const int MaxLogFiles = 7;
    private const string LogFilePrefix = "BGI_guard";
    private static AppLogger? _logger;
    private static AppLogger LoggerStore => _logger ??= new AppLogger(_exeDirectory, LogFilePrefix, MaxLogFiles, GetDisplayVersion);

    private static void Log(string level, string message)
    {
        LoggerStore.Write(level, message);
    }

}
