using System.Diagnostics;

namespace BGIguard;

internal static class ProcessService
{
    public static void ForEachProcessByName(string processName, Action<Process> action)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                action(process);
            }
            finally
            {
                process.Dispose();
            }
        }
    }
}
