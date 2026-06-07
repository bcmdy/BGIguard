namespace BGIguard.Tests;

public sealed class CommandLineArgumentsTests
{
    [Fact]
    public void CleanCommandArgs_RemovesOuterSingleArgumentQuotes()
    {
        string cleaned = CommandLineArguments.CleanCommandArgs("\"--foo\"");

        Assert.Equal("--foo", cleaned);
    }

    [Fact]
    public void CleanCommandArgs_PreservesSeparatedQuotedArguments()
    {
        string cleaned = CommandLineArguments.CleanCommandArgs("\"--path\" \"C:\\Program Files\\BetterGI\"");

        Assert.Equal("--path C:\\Program Files\\BetterGI", cleaned);
    }

    [Fact]
    public void ExtractArgs_UsesParsedArgumentsWhenAvailable()
    {
        static List<string> Split(string _) => ["C:\\Apps\\BetterGI.exe", "--config", "C:\\With Space\\cfg.json"];

        string extracted = CommandLineArguments.ExtractArgs("\"C:\\Apps\\BetterGI.exe\" --config \"C:\\With Space\\cfg.json\"", Split);

        Assert.Equal("--config \"C:\\With Space\\cfg.json\"", extracted);
    }

    [Fact]
    public void FilterDangerousCmdArguments_RemovesCmdControlCharacters()
    {
        char[] dangerous = ['&', '|', '<', '>', '^', '%', '\r', '\n'];

        string filtered = CommandLineArguments.FilterDangerousCmdArguments("--safe & del %TEMP%", dangerous);

        Assert.Equal("--safe  del TEMP", filtered);
    }

    [Fact]
    public void FilterDangerousCmdArguments_PreservesSpacesQuotesAndBackslashes()
    {
        char[] dangerous = ['&', '|', '<', '>', '^', '%', '\r', '\n'];

        string filtered = CommandLineArguments.FilterDangerousCmdArguments("--path \"C:\\Program Files\\BetterGI\\cfg.json\"", dangerous);

        Assert.Equal("--path \"C:\\Program Files\\BetterGI\\cfg.json\"", filtered);
    }

    [Fact]
    public void FilterDangerousCmdArguments_RemovesNewLinesAndRedirectionCharacters()
    {
        char[] dangerous = ['&', '|', '<', '>', '^', '%', '\r', '\n'];

        string filtered = CommandLineArguments.FilterDangerousCmdArguments("--ok\r\n> out.txt | more", dangerous);

        Assert.Equal("--ok out.txt  more", filtered);
    }
}
