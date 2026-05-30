using System.Text;

namespace BGIguard;

internal static class CommandLineArguments
{
    public static string ExtractArgs(string fullCommandLine, Func<string, List<string>> splitCommandLine)
    {
        if (string.IsNullOrWhiteSpace(fullCommandLine))
            return "";

        var parsedArgs = splitCommandLine(fullCommandLine);
        if (parsedArgs.Count > 1)
            return string.Join(" ", parsedArgs.Skip(1).Select(QuoteArgumentIfNeeded));

        string commandLine = fullCommandLine.Trim();
        int argStart = FindArgumentStart(commandLine);

        if (argStart >= commandLine.Length)
            return "";

        return CleanCommandArgs(commandLine[argStart..]);
    }

    public static string FilterDangerousCmdArguments(string args, char[] dangerousChars)
    {
        if (string.IsNullOrEmpty(args))
            return "";

        var builder = new StringBuilder(args.Length);
        foreach (char c in args)
        {
            if (Array.IndexOf(dangerousChars, c) >= 0)
                continue;

            builder.Append(c);
        }

        return builder.ToString().Trim();
    }

    public static string QuoteArgumentIfNeeded(string arg)
    {
        if (arg.Length == 0)
            return "\"\"";

        bool needsQuotes = arg.Any(char.IsWhiteSpace) || arg.Contains('"');
        if (!needsQuotes)
            return arg;

        var builder = new StringBuilder();
        builder.Append('"');

        int backslashCount = 0;
        foreach (char c in arg)
        {
            if (c == '\\')
            {
                backslashCount++;
                continue;
            }

            if (c == '"')
            {
                builder.Append('\\', backslashCount * 2 + 1);
                builder.Append('"');
                backslashCount = 0;
                continue;
            }

            if (backslashCount > 0)
            {
                builder.Append('\\', backslashCount);
                backslashCount = 0;
            }

            builder.Append(c);
        }

        if (backslashCount > 0)
            builder.Append('\\', backslashCount * 2);

        builder.Append('"');
        return builder.ToString();
    }

    public static int FindArgumentStart(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return 0;

        int index;
        if (commandLine[0] == '"')
        {
            int closingQuote = commandLine.IndexOf('"', 1);
            index = closingQuote >= 0 ? closingQuote + 1 : commandLine.Length;
        }
        else
        {
            int exeIndex = commandLine.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            index = exeIndex >= 0 ? exeIndex + 4 : commandLine.IndexOf(' ');
            if (index < 0)
                index = commandLine.Length;
        }

        while (index < commandLine.Length && char.IsWhiteSpace(commandLine[index]))
            index++;

        return index;
    }

    public static string CleanCommandArgs(string args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return "";

        string cleaned = args.Trim();
        while (cleaned.Length >= 2 && cleaned[0] == '"' && cleaned[^1] == '"' && IsSingleQuotedArgument(cleaned))
            cleaned = cleaned[1..^1].Trim();

        if (string.IsNullOrWhiteSpace(cleaned.Replace("\"", "")))
            return "";

        return NormalizeSeparatedQuotedArguments(cleaned);
    }

    public static bool IsSingleQuotedArgument(string value)
    {
        bool inQuotes = false;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(value[i]))
                return false;
        }

        return true;
    }

    public static string NormalizeSeparatedQuotedArguments(string args)
    {
        var builder = new StringBuilder(args.Length);
        bool inQuotes = false;

        for (int i = 0; i < args.Length; i++)
        {
            char current = args[i];
            if (current == '"')
            {
                bool previousIsBoundary = i == 0 || char.IsWhiteSpace(args[i - 1]);
                bool nextIsBoundary = i == args.Length - 1 || char.IsWhiteSpace(args[i + 1]);

                if (previousIsBoundary || nextIsBoundary)
                {
                    inQuotes = !inQuotes;
                    continue;
                }
            }

            builder.Append(current);
        }

        return builder.ToString().Trim();
    }
}
