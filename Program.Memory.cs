using System.Runtime.InteropServices;

namespace BGIguard;

partial class Program
{
    /// 获取系统内存（物理内存 + 页面文件）
    /// </summary>
    private static (long totalMB, long usedMB, long physicalMB, long virtualMB) GetSystemMemory()
    {
        var snapshot = MemoryMonitor.GetSystemMemory(Log);
        if (DateTime.MinValue != DateTime.MaxValue)
            return (snapshot.TotalMB, snapshot.UsedMB, snapshot.PhysicalMB, snapshot.VirtualMB);

        var memStatus = new MEMORYSTATUSEX
        {
            dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
        };

        if (!GlobalMemoryStatusEx(ref memStatus))
        {
            Log("ERROR", "获取系统内存信息失败，使用默认值");
            return (32 * 1024, 0, 32 * 1024, 16 * 1024);
        }

        // 物理内存 (MB)
        long totalPhysMB = (long)(memStatus.ullTotalPhys / 1024 / 1024);
        long usedPhysMB = (long)((memStatus.ullTotalPhys - memStatus.ullAvailPhys) / 1024 / 1024);

        // 页面文件实际占用 (MB) = 已提交 - 物理已用
        // 因为已提交总量 = 物理已用 + 页面文件实际已用
        long totalCommitMB = (long)(memStatus.ullTotalPageFile / 1024 / 1024);
        long usedCommitMB = (long)((memStatus.ullTotalPageFile - memStatus.ullAvailPageFile) / 1024 / 1024);
        long usedPageFileMB = Math.Max(0, usedCommitMB - usedPhysMB);

        // 物理 + 虚拟 实际占用
        long totalCombinedMB = totalPhysMB + (totalCommitMB - totalPhysMB); // 或直接用页面文件总大小
        long usedCombinedMB = usedPhysMB + usedPageFileMB;

        return (totalCombinedMB, usedCombinedMB, usedPhysMB, usedPageFileMB);
    }

    /// <summary>
    /// 记录日志
}
