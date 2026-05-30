using System.Runtime.InteropServices;

namespace BGIguard;

internal static class MemoryMonitor
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    public static SystemMemorySnapshot GetSystemMemory(Action<string, string> log)
    {
        var memStatus = new MEMORYSTATUSEX
        {
            dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
        };

        if (!GlobalMemoryStatusEx(ref memStatus))
        {
            log("ERROR", "获取系统内存信息失败，使用默认值");
            return new SystemMemorySnapshot(32 * 1024, 0, 32 * 1024, 16 * 1024);
        }

        long totalPhysMB = (long)(memStatus.ullTotalPhys / 1024 / 1024);
        long usedPhysMB = (long)((memStatus.ullTotalPhys - memStatus.ullAvailPhys) / 1024 / 1024);

        long totalCommitMB = (long)(memStatus.ullTotalPageFile / 1024 / 1024);
        long usedCommitMB = (long)((memStatus.ullTotalPageFile - memStatus.ullAvailPageFile) / 1024 / 1024);
        long usedPageFileMB = Math.Max(0, usedCommitMB - usedPhysMB);

        long totalCombinedMB = totalPhysMB + (totalCommitMB - totalPhysMB);
        long usedCombinedMB = usedPhysMB + usedPageFileMB;

        return new SystemMemorySnapshot(totalCombinedMB, usedCombinedMB, usedPhysMB, usedPageFileMB);
    }
}

internal readonly record struct SystemMemorySnapshot(
    long TotalMB,
    long UsedMB,
    long PhysicalMB,
    long VirtualMB);
