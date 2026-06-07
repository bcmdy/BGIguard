using System.Text;

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
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string logMessage = $"[{timestamp}] [BGIguard_v{_getDisplayVersion()}] [{level}] {message}";

        Console.WriteLine(logMessage);

        lock (_logLock)
        {
            try
            {
                string logFileName = $"{_logFilePrefix}{DateTime.Now:yyyyMMdd}.log";
                string logPath = Path.Combine(_exeDirectory, logFileName);
                File.AppendAllText(logPath, logMessage + Environment.NewLine, Encoding.UTF8);

                DateTime today = DateTime.Today;
                if (_lastLogCleanupDate != today)
                {
                    CleanOldLogs();
                    _lastLogCleanupDate = today;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"写入日志失败: {ex.Message}");
                Console.WriteLine($"日志目录: {_exeDirectory}");
                Console.WriteLine("请确认程序所在目录具有写入权限。建议将程序移动到用户可写目录，或以管理员身份运行。");
            }
        }
    }

    private void CleanOldLogs()
    {
        try
        {
            var logFiles = Directory.GetFiles(_exeDirectory, $"{_logFilePrefix}*.log")
                .OrderByDescending(f => f)
                .Skip(_maxLogFiles)
                .ToList();

            foreach (var file in logFiles)
            {
                try { File.Delete(file); } catch { }
            }
        }
        catch (Exception ex)
        {
            try { Console.WriteLine($"清理旧日志失败: {ex.Message}"); } catch { }
        }
    }
}
