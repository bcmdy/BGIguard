namespace BGIguard;

partial class Program
{
    /// <summary>
    /// 获取系统内存，单位 MB。
    /// </summary>
    private static (long totalMB, long usedMB, long physicalMB, long virtualMB) GetSystemMemory()
    {
        var snapshot = MemoryMonitor.GetSystemMemory(Log);
        return (snapshot.TotalMB, snapshot.UsedMB, snapshot.PhysicalMB, snapshot.VirtualMB);
    }
}
