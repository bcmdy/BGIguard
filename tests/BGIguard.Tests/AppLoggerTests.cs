using System.Text;

namespace BGIguard.Tests;

public sealed class AppLoggerTests
{
    [Fact]
    public void Write_CreatesUtf8LogFile()
    {
        string tempDir = CreateTempDir();

        try
        {
            var logger = new AppLogger(tempDir, "BGI_guard", 7, () => "5.0.0");

            logger.Write("INFO", "中文日志");

            string logPath = Path.Combine(tempDir, $"BGI_guard{DateTime.Now:yyyyMMdd}.log");
            string content = File.ReadAllText(logPath, Encoding.UTF8);

            Assert.Contains("[BGIguard_v5.0.0]", content);
            Assert.Contains("[INFO]", content);
            Assert.Contains("中文日志", content);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Write_CleansOldLogFiles()
    {
        string tempDir = CreateTempDir();

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "BGI_guard20000101.log"), "oldest");
            File.WriteAllText(Path.Combine(tempDir, "BGI_guard20000102.log"), "old");
            File.WriteAllText(Path.Combine(tempDir, "BGI_guard20000103.log"), "newer");
            var logger = new AppLogger(tempDir, "BGI_guard", 2, () => "5.0.0");

            logger.Write("INFO", "trigger cleanup");

            string[] files = Directory.GetFiles(tempDir, "BGI_guard*.log")
                .Select(Path.GetFileName)
                .OrderBy(name => name)
                .ToArray()!;

            Assert.DoesNotContain("BGI_guard20000101.log", files);
            Assert.DoesNotContain("BGI_guard20000102.log", files);
            Assert.Contains("BGI_guard20000103.log", files);
            Assert.Contains($"BGI_guard{DateTime.Now:yyyyMMdd}.log", files);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Write_DoesNotThrow_WhenLogDirectoryIsInvalid()
    {
        string tempDir = CreateTempDir();
        string fileAsDirectory = Path.Combine(tempDir, "not-a-directory");
        File.WriteAllText(fileAsDirectory, "");

        try
        {
            var logger = new AppLogger(fileAsDirectory, "BGI_guard", 7, () => "5.0.0");

            Exception? exception = Record.Exception(() => logger.Write("INFO", "message"));

            Assert.Null(exception);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static string CreateTempDir()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "BGIguard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }
}
