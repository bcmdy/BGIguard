namespace BGIguard.Tests;

public sealed class PathServiceTests
{
    [Fact]
    public void ValidateExecutablePath_NormalizesExistingExePath()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "BGIguard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string exePath = Path.Combine(tempDir, "BetterGI.exe");
        File.WriteAllText(exePath, "");

        try
        {
            PathValidationResult result = PathService.ValidateExecutablePath($"\"{exePath}\"");

            Assert.True(result.IsValid);
            Assert.Equal(Path.GetFullPath(exePath), result.NormalizedPath);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ValidateExecutablePath_RejectsNonExeFile()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"BGIguard-{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, "");

        try
        {
            PathValidationResult result = PathService.ValidateExecutablePath(tempFile);

            Assert.False(result.IsValid);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ResolveBetterGiPath_PrefersLocalExecutable()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "BGIguard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string localPath = Path.Combine(tempDir, "BetterGI.exe");
        string configuredPath = Path.Combine(tempDir, "Configured.exe");
        File.WriteAllText(localPath, "");
        File.WriteAllText(configuredPath, "");

        try
        {
            string resolved = PathService.ResolveBetterGiPath(tempDir, "BetterGI.exe", configuredPath);

            Assert.Equal(localPath, resolved);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ResolveBetterGiPath_FallsBackToConfiguredPath()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "BGIguard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string configuredPath = Path.Combine(tempDir, "Configured.exe");
        File.WriteAllText(configuredPath, "");

        try
        {
            string resolved = PathService.ResolveBetterGiPath(tempDir, "BetterGI.exe", configuredPath);

            Assert.Equal(configuredPath, resolved);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
