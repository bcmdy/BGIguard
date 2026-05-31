namespace BGIguard.Tests;

public sealed class ProcessServiceTests
{
    [Fact]
    public void IsCurrentUserProcess_UsesSidWhenAvailable()
    {
        var owner = new ProcessOwnerInfo(@"DOMAIN\User", "S-1-5-21-1");

        Assert.True(ProcessService.IsCurrentUserProcess(owner, "S-1-5-21-1", @"OTHER\User"));
        Assert.False(ProcessService.IsCurrentUserProcess(owner, "S-1-5-21-2", @"DOMAIN\User"));
    }

    [Fact]
    public void IsCurrentUserProcess_FallsBackToUserNameWhenSidMissing()
    {
        var owner = new ProcessOwnerInfo(@"DOMAIN\User", "");

        Assert.True(ProcessService.IsCurrentUserProcess(owner, "", @"DOMAIN\User"));
        Assert.False(ProcessService.IsCurrentUserProcess(owner, "", @"DOMAIN\Other"));
    }
}
