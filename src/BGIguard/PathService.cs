namespace BGIguard;

internal static class PathService
{
    public static string ResolveBetterGiPath(string baseDirectory, string exeName, string configuredPath)
    {
        string localPath = Path.Combine(baseDirectory, exeName);
        if (File.Exists(localPath))
            return localPath;

        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        return "";
    }

    public static PathValidationResult ValidateExecutablePath(string path)
    {
        string normalizedPath = path.Trim().Trim('"');

        if (string.IsNullOrWhiteSpace(normalizedPath))
            return PathValidationResult.Invalid(normalizedPath, "Path cannot be empty.");

        if (!File.Exists(normalizedPath))
            return PathValidationResult.Invalid(normalizedPath, $"File does not exist: {normalizedPath}");

        string extension = Path.GetExtension(normalizedPath);
        if (!string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase))
            return PathValidationResult.Invalid(normalizedPath, $"Not a valid executable file (.exe): {extension}");

        return PathValidationResult.Valid(Path.GetFullPath(normalizedPath));
    }
}

internal readonly record struct PathValidationResult(bool IsValid, string NormalizedPath, string? ErrorMessage)
{
    public static PathValidationResult Valid(string normalizedPath) => new(true, normalizedPath, null);

    public static PathValidationResult Invalid(string normalizedPath, string errorMessage) =>
        new(false, normalizedPath, errorMessage);
}
