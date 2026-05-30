using System.Text;

namespace BGIguard;

internal static class Logger
{
    public static void Write(
        string exeDirectory,
        string logFilePrefix,
        int maxLogFiles,
        string displayVersion,
        object logLock,
        ref DateTime lastCleanupDate,
        string level,
        string message)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string logMessage = $"[{timestamp}] [BGIguard_v{displayVersion}] [{level}] {message}";

        Console.WriteLine(logMessage);

        lock (logLock)
        {
            try
            {
                string logFileName = $"{logFilePrefix}{DateTime.Now:yyyyMMdd}.log";
                string logPath = Path.Combine(exeDirectory, logFileName);
                File.AppendAllText(logPath, logMessage + Environment.NewLine, Encoding.UTF8);

                DateTime today = DateTime.Today;
                if (lastCleanupDate != today)
                {
                    CleanOldLogs(exeDirectory, logFilePrefix, maxLogFiles);
                    lastCleanupDate = today;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"写入日志失败: {ex.Message}");
                Console.WriteLine($"日志目录: {exeDirectory}");
                Console.WriteLine("请确认程序所在目录具有写入权限，避免放在 Program Files 等受保护目录，或以管理员身份运行。");
            }
        }
    }

    public static void CleanOldLogs(string exeDirectory, string logFilePrefix, int maxLogFiles)
    {
        try
        {
            var logFiles = Directory.GetFiles(exeDirectory, $"{logFilePrefix}*.log")
                .OrderByDescending(f => f)
                .Skip(maxLogFiles)
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
