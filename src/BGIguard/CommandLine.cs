using System.Runtime.InteropServices;

namespace BGIguard;

internal static class CommandLine
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(
        [MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine,
        out int pNumArgs);

    public static string ExtractArgs(string fullCommandLine)
    {
        return CommandLineArguments.ExtractArgs(fullCommandLine, SplitCommandLine);
    }

    public static List<string> SplitCommandLine(string commandLine)
    {
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
}
