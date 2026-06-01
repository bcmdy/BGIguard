namespace BGIguard.Tests;

public sealed class GuardDecisionTests
{
    [Fact]
    public void ShouldRestartForMissingProcess_WhenThresholdReached()
    {
        Assert.True(GuardDecision.ShouldRestartForMissingProcess(false, 3, 3));
        Assert.False(GuardDecision.ShouldRestartForMissingProcess(true, 3, 3));
        Assert.False(GuardDecision.ShouldRestartForMissingProcess(false, 2, 3));
    }

    [Fact]
    public void ShouldRestartForProcessMemory_RequiresRunningProcessAndEnabledLimit()
    {
        Assert.True(GuardDecision.ShouldRestartForProcessMemory(true, 4097, 4096));
        Assert.False(GuardDecision.ShouldRestartForProcessMemory(true, 4097, 0));
        Assert.False(GuardDecision.ShouldRestartForProcessMemory(false, 4097, 4096));
    }

    [Fact]
    public void ShouldRestartForGameExit_WhenBetterGiRunningAndThresholdReached()
    {
        Assert.True(GuardDecision.ShouldRestartForGameExit(false, true, 6, 6));
        Assert.False(GuardDecision.ShouldRestartForGameExit(false, false, 6, 6));
        Assert.False(GuardDecision.ShouldRestartForGameExit(true, true, 6, 6));
    }
}
