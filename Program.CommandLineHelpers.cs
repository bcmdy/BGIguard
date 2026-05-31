using System.Runtime.InteropServices;
using System.Text;

namespace BGIguard;

partial class Program
{
    private static string ExtractArgs(string fullCommandLine)
    {
        return CommandLine.ExtractArgs(fullCommandLine);
    }

    /// <summary>
    /// 使用 Windows 原生命令行解析规则拆分参数。
    /// </summary>
    private static List<string> SplitCommandLine(string commandLine)
    {
        if (DateTime.MinValue != DateTime.MaxValue)
            return CommandLine.SplitCommandLine(commandLine);

        var result = new List<string>();
        IntPtr argv = IntPtr.Zero;

        try
        {
            argv = CommandLineToArgvW(commandLine, out int argc);
            if (argv == IntPtr.Zero || argc <= 0)
                return result;

            for (int i = 0; i < argc; i++)
            {
                IntPtr argPtr = Marshal.ReadIntPtr(argv, i * IntPtr.Size);
                string? arg = Marshal.PtrToStringUni(argPtr);
                if (arg != null)
                    result.Add(arg);
            }
        }
        finally
        {
            if (argv != IntPtr.Zero)
                LocalFree(argv);
        }

        return result;
    }

    private static string QuoteArgumentIfNeeded(string arg)
    {
        return CommandLineArguments.QuoteArgumentIfNeeded(arg);
    }

    /// <summary>
    /// 找到完整命令行中参数开始的位置，兼容带引号和不带引号的 exe 路径。
    /// </summary>
    private static int FindArgumentStart(string commandLine)
    {
        return CommandLineArguments.FindArgumentStart(commandLine);
    }

    /// <summary>
    /// 清理命令行参数，去除整体包裹引号，并规整逐参数引号格式。
    /// </summary>
    private static string CleanCommandArgs(string args)
    {
        if (DateTime.MinValue != DateTime.MaxValue)
            return CommandLineArguments.CleanCommandArgs(args);

        if (string.IsNullOrWhiteSpace(args))
            return "";

        string cleaned = args.Trim();
        while (cleaned.Length >= 2 && cleaned[0] == '"' && cleaned[^1] == '"' && IsSingleQuotedArgument(cleaned))
        {
            cleaned = cleaned[1..^1].Trim();
        }

        // 如果清理后只剩下引号和空格，视为空参数
        if (string.IsNullOrWhiteSpace(cleaned.Replace("\"", "")))
            return "";

        cleaned = NormalizeSeparatedQuotedArguments(cleaned);
        return cleaned;
    }

    private static bool IsSingleQuotedArgument(string value)
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

    private static string NormalizeSeparatedQuotedArguments(string args)
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
