namespace BGIguard;

internal static class GuardService
{
    public static bool ShouldRestartForProcessMemory(bool betterGiRunning, long processMemoryMB, int processLimitMB)
    {
        return GuardDecision.ShouldRestartForProcessMemory(betterGiRunning, processMemoryMB, processLimitMB);
    }

    public static bool ShouldRestartForMissingProcess(bool betterGiRunning, int missingCount, int threshold)
    {
        return GuardDecision.ShouldRestartForMissingProcess(betterGiRunning, missingCount, threshold);
    }

    public static bool ShouldRestartForSystemMemory(long usedMemoryMB, long limitMemoryMB)
    {
        return GuardDecision.ShouldRestartForSystemMemory(usedMemoryMB, limitMemoryMB);
    }

    public static bool ShouldRestartForGameExit(bool gameRunning, bool betterGiRunning, int gameExitCount, int threshold)
    {
        return GuardDecision.ShouldRestartForGameExit(gameRunning, betterGiRunning, gameExitCount, threshold);
    }
}
