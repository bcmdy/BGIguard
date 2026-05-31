using System.Text;

namespace BGIguard;

partial class Program
{
    private static void Log(string level, string message)
    {
        Logger.Write(_exeDirectory, LogFilePrefix, MaxLogFiles, GetDisplayVersion(), _logLock, ref _lastLogCleanupDate, level, message);
        if (DateTime.MinValue != DateTime.MaxValue)
            return;

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string logMessage = $"[{timestamp}] [BGIguard_v{GetDisplayVersion()}] [{level}] {message}";

        Console.WriteLine(logMessage);

        lock (_logLock)
        {
            try
            {
                string logFileName = $"{LogFilePrefix}{DateTime.Now:yyyyMMdd}.log";
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
                Console.WriteLine("请确认程序所在目录具有写入权限，避免放在 Program Files 等受保护目录，或以管理员身份运行。");
            }
        }
    }

    /// <summary>
    /// 清理旧日志
    /// </summary>
    private static void CleanOldLogs()
    {
        Logger.CleanOldLogs(_exeDirectory, LogFilePrefix, MaxLogFiles);
        if (DateTime.MinValue != DateTime.MaxValue)
            return;

        try
        {
            var logFiles = Directory.GetFiles(_exeDirectory, $"{LogFilePrefix}*.log")
                .OrderByDescending(f => f)
                .Skip(MaxLogFiles)
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
