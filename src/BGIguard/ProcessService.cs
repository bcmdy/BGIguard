using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace BGIguard;

internal static class ProcessService
{
    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenUser = 1;
    private const int PROCESS_QUERY_INFORMATION = 0x0400;
    private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int PROCESS_VM_READ = 0x0010;
    private const int ProcessBasicInformation = 0;
    private static readonly int PebProcessParametersOffset = IntPtr.Size == 8 ? 0x20 : 0x10;
    private static readonly int ProcessParametersCommandLineOffset = IntPtr.Size == 8 ? 0x70 : 0x40;

    [StructLayout(LayoutKind.Sequential)]
    private struct SID_AND_ATTRIBUTES
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_USER
    {
        public SID_AND_ATTRIBUTES User;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr Reserved3;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref PROCESS_BASIC_INFORMATION processInformation,
        int processInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        int dwDesiredAccess,
        bool bInheritHandle,
        int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        byte[] lpBuffer,
        int dwSize,
        out int lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass, IntPtr TokenInformation, int TokenInformationLength, out int ReturnLength);

    [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool LookupAccountSid(string? lpSystemName, IntPtr Sid, StringBuilder lpName, ref int cchName, StringBuilder? lpReferencedDomainName, ref int cchReferencedDomainName, out int peUse);

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

    public static Mutex EnsureSingleInstance(
        string mutexName,
        int waitExitMs,
        string currentUserSid,
        string currentUserName,
        Action<string, string> log)
    {
        var mutex = new Mutex(true, mutexName, out bool createdNew);

        if (!createdNew)
        {
            log("WARN", "检测到已存在的守护进程，正在终止...");
            using var currentProcess = Process.GetCurrentProcess();
            TerminateProcessesByCurrentUser(
                currentProcess.ProcessName,
                "旧守护进程",
                Environment.ProcessId,
                waitExitMs,
                currentUserSid,
                currentUserName,
                log);

            mutex.Dispose();
            mutex = new Mutex(true, mutexName, out _);
        }

        log("INFO", "单实例保护已生效");
        return mutex;
    }

    public static void TerminateProcessesByCurrentUser(
        string processName,
        string logPrefix,
        int? excludePid,
        int waitExitMs,
        string currentUserSid,
        string currentUserName,
        Action<string, string> log)
    {
        ForEachProcessByName(processName, process =>
        {
            try
            {
                if (excludePid.HasValue && process.Id == excludePid.Value)
                    return;

                var owner = GetProcessOwner(process.Id);
                if (IsCurrentUserProcess(owner, currentUserSid, currentUserName))
                {
                    process.Kill();
                    process.WaitForExit(waitExitMs);
                    log("INFO", $"已终止 {logPrefix} PID:{process.Id} ({owner.Display})");
                }
                else if (owner.HasIdentity)
                {
                    log("WARN", $"{logPrefix} PID:{process.Id} 属于 {owner.Display}，跳过终止");
                }
            }
            catch (Exception ex)
            {
                log("ERROR", $"终止 {logPrefix} PID:{process.Id} 失败: {ex.Message}");
            }
        });
    }

    public static bool StartDetachedWithCmdStart(string exePath, string commandArgs, Action<string, string> log)
    {
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = BuildCmdStartArguments(exePath, commandArgs),
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var startedProcess = Process.Start(startInfo);
            log("INFO", "已启动 BetterGI.exe" + (string.IsNullOrEmpty(commandArgs) ? "" : $" (参数: {commandArgs})"));
            return true;
        }
        catch (Exception ex)
        {
            log("ERROR", $"启动失败: {ex.Message}");
            return false;
        }
    }

    public static bool StartBetterGiProcess(
        string exePath,
        string commandLine,
        char[] dangerousCmdArgumentChars,
        Action<string, string> log)
    {
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            log("ERROR", $"找不到 BetterGI.exe: {exePath}");
            return false;
        }

        string cmdArgs = CommandLineArguments.CleanCommandArgs(commandLine);
        string filteredArgs = CommandLineArguments.FilterDangerousCmdArguments(cmdArgs, dangerousCmdArgumentChars);
        if (!string.Equals(cmdArgs, filteredArgs, StringComparison.Ordinal))
        {
            log("WARN", $"启动参数包含 cmd 特殊字符，已过滤。原始参数: {FormatArgumentForLog(cmdArgs)} | 过滤后: {FormatArgumentForLog(filteredArgs)}");
            cmdArgs = filteredArgs;
        }

        return StartDetachedWithCmdStart(exePath, cmdArgs, log);
    }

    public static string BuildCmdStartArguments(string exePath, string commandArgs)
    {
        return string.IsNullOrEmpty(commandArgs)
            ? $"/c start \"\" \"{exePath}\""
            : $"/c start \"\" \"{exePath}\" {commandArgs}";
    }

    private static string FormatArgumentForLog(string args)
    {
        return args.Replace("\r", "\\r").Replace("\n", "\\n");
    }

    public static (bool anyRunning, List<string> runningNames) GetRunningOwnedProcesses(
        IReadOnlyCollection<string> processNames,
        string currentUserSid,
        string currentUserName)
    {
        var running = new List<string>();
        foreach (var name in processNames)
        {
            ForEachProcessByName(name, process =>
            {
                var owner = GetProcessOwner(process.Id);
                if (IsCurrentUserProcess(owner, currentUserSid, currentUserName))
                    running.Add(name);
            });
        }

        return (running.Count > 0, running);
    }

    public static BetterGiProcessSnapshot GetOwnedProcessSnapshot(
        string processName,
        string expectedExePath,
        string currentUserSid,
        string currentUserName,
        bool includeCommandLine,
        bool includeMemory,
        Action<string, string> log)
    {
        if (string.IsNullOrEmpty(expectedExePath))
            return default;

        BetterGiProcessSnapshot snapshot = default;
        ForEachProcessByName(processName, process =>
        {
            if (snapshot.Exists)
                return;

            try
            {
                var owner = GetProcessOwner(process.Id);
                if (!IsCurrentUserProcess(owner, currentUserSid, currentUserName))
                    return;

                string? modulePath = process.MainModule?.FileName;
                if (!string.Equals(modulePath, expectedExePath, StringComparison.OrdinalIgnoreCase))
                    return;

                string? commandLine = includeCommandLine ? GetProcessCommandLine(process.Id) : null;
                long memoryMB = includeMemory ? process.PrivateMemorySize64 / 1024 / 1024 : 0;
                snapshot = new BetterGiProcessSnapshot(true, commandLine, memoryMB);
            }
            catch (Exception ex)
            {
                log("ERROR", $"获取 BetterGI 进程信息失败: {ex.Message}");
            }
        });

        return snapshot;
    }

    public static string? GetProcessCommandLine(int processId)
    {
        IntPtr hProcess = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, processId);
        if (hProcess == IntPtr.Zero)
            return null;

        try
        {
            var pbi = new PROCESS_BASIC_INFORMATION();
            int status = NtQueryInformationProcess(hProcess, ProcessBasicInformation, ref pbi, Marshal.SizeOf(pbi), out _);
            if (status != 0)
                return null;

            byte[] buffer = new byte[IntPtr.Size];
            if (!ReadProcessMemory(hProcess, IntPtr.Add(pbi.PebBaseAddress, PebProcessParametersOffset), buffer, IntPtr.Size, out _))
                return null;

            IntPtr processParameters = IntPtr.Size == 8
                ? (IntPtr)BitConverter.ToInt64(buffer, 0)
                : (IntPtr)BitConverter.ToInt32(buffer, 0);

            byte[] cmdLineBuffer = new byte[Marshal.SizeOf<UNICODE_STRING>()];
            if (!ReadProcessMemory(hProcess, IntPtr.Add(processParameters, ProcessParametersCommandLineOffset), cmdLineBuffer, cmdLineBuffer.Length, out _))
                return null;

            var unicodeString = MemoryMarshal.Read<UNICODE_STRING>(cmdLineBuffer);
            if (unicodeString.Buffer == IntPtr.Zero || unicodeString.Length == 0)
                return null;

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

    public static ProcessOwnerInfo GetProcessOwner(int processId)
    {
        IntPtr hProcess = IntPtr.Zero;
        IntPtr hToken = IntPtr.Zero;
        IntPtr tokenInfo = IntPtr.Zero;

        try
        {
            hProcess = OpenProcess(PROCESS_QUERY_INFORMATION, false, processId);
            if (hProcess == IntPtr.Zero)
            {
                hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
                if (hProcess == IntPtr.Zero)
                    return default;
            }

            if (!OpenProcessToken(hProcess, TOKEN_QUERY, out hToken))
                return default;

            int returnLength = 0;
            GetTokenInformation(hToken, TokenUser, IntPtr.Zero, 0, out returnLength);
            if (returnLength == 0)
                return default;

            tokenInfo = Marshal.AllocHGlobal(returnLength);
            if (!GetTokenInformation(hToken, TokenUser, tokenInfo, returnLength, out returnLength))
                return default;

            var tokenUser = Marshal.PtrToStructure<TOKEN_USER>(tokenInfo);
            if (tokenUser.User.Sid == IntPtr.Zero)
                return default;

            string sid = new SecurityIdentifier(tokenUser.User.Sid).Value;

            int nameSize = 0;
            int domainSize = 0;
            if (!LookupAccountSid(null, tokenUser.User.Sid, null!, ref nameSize, null!, ref domainSize, out _))
            {
                if (nameSize == 0)
                    return new ProcessOwnerInfo("", sid);
            }

            var nameBuilder = new StringBuilder(nameSize);
            var domainBuilder = new StringBuilder(Math.Max(1, domainSize));
            if (!LookupAccountSid(null, tokenUser.User.Sid, nameBuilder, ref nameSize, domainBuilder, ref domainSize, out _))
                return new ProcessOwnerInfo("", sid);

            string name = nameBuilder.ToString();
            string domain = domainBuilder.ToString();
            string displayName = string.IsNullOrEmpty(domain) ? name : $@"{domain}\{name}";
            return new ProcessOwnerInfo(displayName, sid);
        }
        catch
        {
            return default;
        }
        finally
        {
            if (tokenInfo != IntPtr.Zero)
                Marshal.FreeHGlobal(tokenInfo);
            if (hToken != IntPtr.Zero)
                CloseHandle(hToken);
            if (hProcess != IntPtr.Zero)
                CloseHandle(hProcess);
        }
    }

    public static bool IsCurrentUserProcess(ProcessOwnerInfo owner, string currentUserSid, string currentUserName)
    {
        if (string.IsNullOrEmpty(currentUserSid))
            return string.Equals(owner.UserName, currentUserName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(owner.UserName, Environment.UserName, StringComparison.OrdinalIgnoreCase);

        return string.Equals(owner.Sid, currentUserSid, StringComparison.OrdinalIgnoreCase);
    }
}

internal readonly record struct ProcessOwnerInfo(string UserName, string Sid)
{
    public bool HasIdentity => !string.IsNullOrEmpty(Sid);
    public string Display => string.IsNullOrEmpty(UserName) ? $"SID:{Sid}" : $"用户:{UserName}, SID:{Sid}";
}

internal readonly record struct BetterGiProcessSnapshot(bool Exists, string? CommandLine, long MemoryMB);
