namespace BGIguard;

partial class Program
{
    private static string ExtractArgs(string fullCommandLine)
    {
        return CommandLine.ExtractArgs(fullCommandLine);
    }

    private static string CleanCommandArgs(string args)
    {
        return CommandLineArguments.CleanCommandArgs(args);
    }
}
