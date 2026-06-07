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

    [Fact]
    public void BuildCmdStartArguments_UsesEmptyTitleAndQuotedExecutable()
    {
        string args = ProcessService.BuildCmdStartArguments(@"C:\Program Files\App\Target.exe", "--foo bar");

        Assert.Equal("/c start \"\" \"C:\\Program Files\\App\\Target.exe\" --foo bar", args);
    }

    [Fact]
    public void BuildCmdStartArguments_PreservesQuotedArguments()
    {
        string args = ProcessService.BuildCmdStartArguments(
            @"C:\Program Files\App\Target.exe",
            "--config \"C:\\Users\\Name With Space\\config.json\"");

        Assert.Equal("/c start \"\" \"C:\\Program Files\\App\\Target.exe\" --config \"C:\\Users\\Name With Space\\config.json\"", args);
    }

    [Fact]
    public void BuildCmdStartArguments_OmitsTrailingArgumentsWhenEmpty()
    {
        string args = ProcessService.BuildCmdStartArguments(@"C:\App\Target.exe", "");

        Assert.Equal("/c start \"\" \"C:\\App\\Target.exe\"", args);
    }
}
