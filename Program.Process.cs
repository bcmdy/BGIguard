using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BGIguard;

partial class Program
{
    /// <summary>
    /// 处理单实例保护
    /// </summary>
    private static void HandleSingleInstance()
    {
        string mutexName = "BGIguard_SingleInstance_Mutex";
        bool createdNew;
        _mutex = new Mutex(true, mutexName, out createdNew);

        if (!createdNew)
        {
            Log("WARN", "检测到已存在的守护进程，正在终止...");
            TerminateExistingGuard();
            _mutex = new Mutex(true, mutexName, out createdNew);
        }

        Log("INFO", "单实例保护已生效");
    }

    /// <summary>
    /// 按用户终止指定进程
    /// </summary>
    private static void TerminateProcessesByUser(string processName, string logPrefix, int? excludePid = null)
    {
        ProcessService.ForEachProcessByName(processName, process =>
        {
            try
            {
                if (excludePid.HasValue && process.Id == excludePid.Value)
                    return;

                var owner = GetProcessOwner(process.Id);
                if (IsCurrentUserProcess(owner))
                {
                    process.Kill();
                    process.WaitForExit(ProcessWaitExitMs);
                    Log("INFO", $"已终止 {logPrefix} PID:{process.Id} ({owner.Display})");
                }
                else if (owner.HasIdentity)
                {
                    Log("WARN", $"{logPrefix} PID:{process.Id} 属于{owner.Display}，跳过终止");
                }
            }
            catch (Exception ex)
            {
                Log("ERROR", $"终止 {logPrefix} PID:{process.Id} 失败: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 终止已存在的守护进程（按用户和进程名）
    /// </summary>
    private static void TerminateExistingGuard()
    {
        using var currentProcess = Process.GetCurrentProcess();
        string currentProcessName = currentProcess.ProcessName;
        TerminateProcessesByUser(currentProcessName, "旧守护进程", Environment.ProcessId);
    }

    private static ProcessOwnerInfo GetProcessOwner(int processId)
    {
        return ProcessService.GetProcessOwner(processId);
    }

    private static bool IsCurrentUserProcess(ProcessOwnerInfo owner)
    {
        return ProcessService.IsCurrentUserProcess(owner, _currentUserSid, _currentUserName);
    }

    /// <summary>
    /// 启动 BetterGI.exe 进程
    /// </summary>
    private static void StartBetterGiProcess(string? commandLine = null)
    {
        if (string.IsNullOrEmpty(_betterGiExePath) || !File.Exists(_betterGiExePath))
        {
            Log("ERROR", $"找不到 BetterGI.exe: {_betterGiExePath}");
            return;
        }

        // 使用传入的命令行或缓存的命令行
        string cmdArgs = CleanCommandArgs(commandLine ?? _cachedCommand);
        string filteredArgs = FilterCmdArguments(cmdArgs);
        if (!string.Equals(cmdArgs, filteredArgs, StringComparison.Ordinal))
        {
            Log("WARN", $"启动参数包含 cmd 特殊字符，已过滤。原始参数: {FormatArgumentForLog(cmdArgs)} | 过滤后: {FormatArgumentForLog(filteredArgs)}");
            cmdArgs = filteredArgs;
        }

        try
        {
            // 关键修复：使用 "" 作为空窗口标题占位
            // 否则如果 cmdArgs 以引号开头，会被 start 当作窗口标题解析
            string arguments = string.IsNullOrEmpty(cmdArgs)
                ? $"/c start \"\" \"{_betterGiExePath}\""
                : $"/c start \"\" \"{_betterGiExePath}\" {cmdArgs}";

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var startedProcess = Process.Start(startInfo);
            Log("INFO", $"已启动 BetterGI.exe" + (string.IsNullOrEmpty(cmdArgs) ? "" : $" (参数: {cmdArgs})"));
        }
        catch (Exception ex)
        {
            Log("ERROR", $"启动失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 按用户终止 BetterGI.exe 进程（终止当前用户启动的进程）
    /// </summary>
    private static void TerminateBetterGiProcessByUser()
    {
        TerminateProcessesByUser(BetterGiExeName.Replace(".exe", ""), "BetterGI.exe");
    }

    /// <summary>
    /// 获取 BetterGI 进程当前独占内存（PrivateMemorySize64），单位 MB
    /// </summary>
    private static long GetBetterGiMemoryMB()
    {
        return GetBetterGiSnapshot(includeCommandLine: false, includeMemory: true).MemoryMB;
    }

    /// <summary>
    /// 检查进程是否属于当前用户
    /// </summary>
    private static bool IsProcessOwnedByCurrentUser(int processId)
    {
        return IsCurrentUserProcess(GetProcessOwner(processId));
    }

    /// <summary>
    /// 过滤 cmd.exe /c start 参数中的高风险控制字符。
    /// </summary>
    private static string FilterCmdArguments(string args)
    {
        return CommandLineArguments.FilterDangerousCmdArguments(args, DangerousCmdArgumentChars);
    }

    private static string FormatArgumentForLog(string args)
    {
        return args.Replace("\r", "\\r").Replace("\n", "\\n");
    }

    /// <summary>
    /// 按用户和路径检查 BetterGI 是否运行
    /// </summary>
    private static bool IsBetterGiRunningByUser()
    {
        return GetBetterGiSnapshot(includeCommandLine: false, includeMemory: false).Exists;
    }

    // 重启 BetterGI.exe
    private static void RestartBetterGiProcess()
    {
        TerminateBetterGiProcessByUser();
        Thread.Sleep(RestartDelayMs);
        StartBetterGiProcess(_cachedCommand);
    }

    /// <summary>
    /// 获取当前用户 BetterGI 进程的信息
    /// </summary>
    private static (bool exists, string? commandLine) GetBetterGiInfo()
    {
        var snapshot = GetBetterGiSnapshot(includeCommandLine: true, includeMemory: false);
        return (snapshot.Exists, snapshot.CommandLine);
    }

    /// <summary>
    /// 获取当前用户 BetterGI 进程快照。
    /// </summary>
    private static BetterGiProcessSnapshot GetBetterGiSnapshot(bool includeCommandLine, bool includeMemory)
    {
        if (string.IsNullOrEmpty(_betterGiExePath)) return default;

        foreach (var process in Process.GetProcessesByName(BetterGiExeName.Replace(".exe", "")))
        {
            try
            {
                if (!IsProcessOwnedByCurrentUser(process.Id))
                    continue;
                string? modulePath = process.MainModule?.FileName;
                if (string.Equals(modulePath, _betterGiExePath, StringComparison.OrdinalIgnoreCase))
                {
                    string? commandLine = includeCommandLine ? GetProcessCommandLine(process.Id) : null;
                    long memoryMB = includeMemory ? process.PrivateMemorySize64 / 1024 / 1024 : 0;
                    return new BetterGiProcessSnapshot(true, commandLine, memoryMB);
                }
            }
            catch (Exception ex)
            {
                Log("ERROR", $"获取 BetterGI 进程信息失败: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }
        return default;
    }

    /// <summary>
    /// 获取当前用户正在运行的游戏进程。
    /// </summary>
    private static (bool anyRunning, List<string> runningNames) GetRunningGameProcesses()
    {
        var games = new List<string>();
        foreach (var name in GameProcessNames)
        {
            ProcessService.ForEachProcessByName(name, process =>
            {
                if (IsProcessOwnedByCurrentUser(process.Id))
                    games.Add(name);
            });
        }
        return (games.Count > 0, games);
    }

    /// <summary>
    /// 获取进程命令行（通过 NtQueryInformationProcess 读取 PEB）
    /// </summary>
    private static string? GetProcessCommandLine(int processId)
    {
        IntPtr hProcess = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, processId);
        if (hProcess == IntPtr.Zero)
            return null;

        try
        {
            var pbi = new PROCESS_BASIC_INFORMATION();
            int status = NtQueryInformationProcess(hProcess, ProcessBasicInformation, ref pbi, Marshal.SizeOf(pbi), out _);
            if (status != 0) return null;

            // 读取 PEB 中的 ProcessParameters 指针
            byte[] buffer = new byte[IntPtr.Size];
            if (!ReadProcessMemory(hProcess, IntPtr.Add(pbi.PebBaseAddress, PEB_OFFSET), buffer, IntPtr.Size, out _))
                return null;

            IntPtr processParameters = IntPtr.Size == 8
                ? (IntPtr)BitConverter.ToInt64(buffer, 0)
                : (IntPtr)BitConverter.ToInt32(buffer, 0);

            // 读取 CommandLine UNICODE_STRING
            byte[] cmdLineBuffer = new byte[Marshal.SizeOf<UNICODE_STRING>()];
            if (!ReadProcessMemory(hProcess, IntPtr.Add(processParameters, CMDLINE_OFFSET), cmdLineBuffer, cmdLineBuffer.Length, out _))
                return null;

            var unicodeString = MemoryMarshal.Read<UNICODE_STRING>(cmdLineBuffer);
            if (unicodeString.Buffer == IntPtr.Zero || unicodeString.Length == 0)
                return null;

            // 读取实际的命令行字符串
            byte[] cmdLineBytes = new byte[unicodeString.Length];
            if (!ReadProcessMemory(hProcess, unicodeString.Buffer, cmdLineBytes, unicodeString.Length, out _))
                return null;

            return Encoding.Unicode.GetString(cmdLineBytes);
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

}
