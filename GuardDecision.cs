namespace BGIguard;

internal static class GuardDecision
{
    public static bool ShouldRestartForMissingProcess(bool betterGiRunning, int missingCount, int threshold)
    {
        return !betterGiRunning && missingCount >= Math.Max(1, threshold);
    }

    public static bool ShouldRestartForSystemMemory(long usedMemoryMB, long limitMemoryMB)
    {
        return usedMemoryMB > limitMemoryMB;
    }

    public static bool ShouldRestartForProcessMemory(bool betterGiRunning, long processMemoryMB, int processLimitMB)
    {
        return betterGiRunning && processLimitMB > 0 && processMemoryMB > processLimitMB;
    }

    public static bool ShouldRestartForGameExit(bool gameRunning, bool betterGiRunning, int gameExitCount, int threshold)
    {
        return !gameRunning && betterGiRunning && gameExitCount >= Math.Max(1, threshold);
    }
}
